using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RevitMCPAddin.Commands;

namespace RevitMCPAddin.Server;

/// <summary>
/// Session-scoped, opt-in at-most-once dispatch. Keys are never evicted: an old
/// operation must not become executable again because its cached result expired.
/// Concept informed by mskim274/revit-mcp-v2; independently implemented for HTTP.
/// </summary>
internal sealed class OperationRegistry
{
    private sealed record Entry(string Fingerprint, TaskCompletionSource<string> Completion);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private readonly int _maxResultBytes;
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public OperationRegistry(int capacity = 1024, int maxResultBytes = 65536)
    {
        _capacity = capacity;
        _maxResultBytes = maxResultBytes;
    }

    private JsonObject? ValidateKey(string key)
    {
        if (!Regex.IsMatch(key, "^[a-f0-9]{32}:[A-Za-z0-9_-]{1,96}$"))
            return JsonResult.Error("bad_request", "idempotencyKey must be <health.sessionId>:<unique-operation-id>.");
        if (!key.StartsWith(SessionId + ":", StringComparison.Ordinal))
            return JsonResult.Error("session_mismatch", "This key belongs to another server session. Reconcile the model before issuing a new operation.");
        return null;
    }

    public Task<JsonObject> ExecuteAsync(string key, JsonObject payload, Func<Task<JsonObject>> execute)
    {
        var error = ValidateKey(key);
        if (error is not null) return Task.FromResult(error);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(payload).ToJsonString())));
        Entry entry;
        bool replay;
        lock (_gate)
        {
            replay = _entries.TryGetValue(key, out entry!);
            if (replay && entry.Fingerprint != fingerprint)
                return Task.FromResult(JsonResult.Error("idempotency_conflict", "Key already reserved for a different command, parameters or preview mode."));
            if (!replay)
            {
                if (_entries.Count >= _capacity)
                    return Task.FromResult(JsonResult.Error("operation_capacity", "Session operation ledger is full. Existing keys remain queryable; new keyed operations are refused."));
                entry = new Entry(fingerprint, new(TaskCreationOptions.RunContinuationsAsynchronously));
                _entries.Add(key, entry);
            }
        }
        if (!replay) _ = CompleteAsync(entry, execute);
        return ReadAsync(entry, key, replay);
    }

    private async Task CompleteAsync(Entry entry, Func<Task<JsonObject>> execute)
    {
        JsonObject result;
        try { result = await execute().ConfigureAwait(false); }
        catch (Exception ex)
        {
            result = JsonResult.Error("dispatch_failed", ex.Message);
            result["outcomeUnknown"] = true;
        }
        var json = result.ToJsonString();
        if (Encoding.UTF8.GetByteCount(json) > _maxResultBytes)
        {
            var summary = JsonResult.Error("operation_result_too_large", "Operation finished, but its result exceeds the ledger limit. Inspect the model; this key will not execute again.");
            summary["committed"] = result["committed"]?.DeepClone();
            summary["originalOk"] = result["ok"]?.DeepClone();
            json = summary.ToJsonString();
        }
        entry.Completion.TrySetResult(json);
    }

    private async Task<JsonObject> ReadAsync(Entry entry, string key, bool replay)
    {
        var json = await entry.Completion.Task.ConfigureAwait(false);
        var result = JsonNode.Parse(json)!.AsObject();
        result["operation"] = new JsonObject { ["key"] = key, ["sessionId"] = SessionId, ["state"] = "completed", ["replayed"] = replay };
        return result;
    }

    public JsonObject Get(string key)
    {
        var error = ValidateKey(key);
        if (error is not null) return error;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return JsonResult.Error("not_found", "No operation with this key was accepted in this session.");
            return JsonResult.Success(new JsonObject
            {
                ["key"] = key, ["sessionId"] = SessionId,
                ["state"] = entry.Completion.Task.IsCompleted ? "completed" : "pending",
                ["result"] = entry.Completion.Task.IsCompleted ? JsonNode.Parse(entry.Completion.Task.Result) : null,
            });
        }
    }

    private static JsonNode Canonical(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value is null ? null : Canonical(p.Value)))),
        JsonArray arr => new JsonArray(arr.Select(v => v is null ? null : Canonical(v)).ToArray()),
        _ => node.DeepClone(),
    };
}
