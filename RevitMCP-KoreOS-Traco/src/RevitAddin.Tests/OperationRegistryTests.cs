using System.Text.Json.Nodes;
using RevitMCPAddin.Server;
using Xunit;

namespace RevitMCPAddin.Tests;

public class OperationRegistryTests
{
    private static JsonObject Payload(bool dry = false) => new() { ["command"] = "create_wall", ["dryRun"] = dry };
    private static JsonObject Success() => new() { ["ok"] = true, ["committed"] = true };
    private static string? Code(JsonObject r) => r["error"]?["code"]?.GetValue<string>();

    [Fact]
    public async Task Concurrent_duplicates_share_execution_and_status_survives_caller()
    {
        var registry = new OperationRegistry();
        var key = registry.SessionId + ":wall-1";
        var gate = new TaskCompletionSource<JsonObject>();
        int executions = 0;
        Task<JsonObject> Run() { executions++; return gate.Task; }
        var first = registry.ExecuteAsync(key, Payload(), Run);
        var second = registry.ExecuteAsync(key, Payload(), Run);
        Assert.Equal("pending", registry.Get(key)["data"]?["state"]?.GetValue<string>());
        gate.SetResult(Success());
        var responses = await Task.WhenAll(first, second);
        Assert.Equal(1, executions);
        Assert.False(responses[0]["operation"]!["replayed"]!.GetValue<bool>());
        Assert.True(responses[1]["operation"]!["replayed"]!.GetValue<bool>());
        responses[0]["committed"] = false;
        Assert.True(registry.Get(key)["data"]!["result"]!["committed"]!.GetValue<bool>());
        await registry.ExecuteAsync(key, Payload(), Run);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task Preview_cannot_be_reused_for_commit_and_other_sessions_are_rejected()
    {
        var r = new OperationRegistry();
        var key = r.SessionId + ":one";
        await r.ExecuteAsync(key, Payload(true), () => Task.FromResult(Success()));
        var conflict = await r.ExecuteAsync(key, Payload(false), () => throw new Exception("must not run"));
        Assert.Equal("idempotency_conflict", Code(conflict));
        Assert.Equal("session_mismatch", Code(new OperationRegistry().Get(key)));
    }

    [Fact]
    public async Task Full_registry_does_not_evict_old_keys()
    {
        var r = new OperationRegistry(capacity: 1);
        await r.ExecuteAsync(r.SessionId + ":a", Payload(), () => Task.FromResult(Success()));
        Assert.Equal("operation_capacity", Code(await r.ExecuteAsync(r.SessionId + ":b", Payload(), () => throw new Exception())));
        Assert.True((await r.ExecuteAsync(r.SessionId + ":a", Payload(), () => throw new Exception()))["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Oversized_result_and_exception_never_allow_reexecution()
    {
        var r = new OperationRegistry(maxResultBytes: 100);
        int executions = 0;
        Task<JsonObject> Large() { executions++; var p = Success(); p["data"] = new string('x', 500); return Task.FromResult(p); }
        var key = r.SessionId + ":large";
        Assert.Equal("operation_result_too_large", Code(await r.ExecuteAsync(key, Payload(), Large)));
        await r.ExecuteAsync(key, Payload(), Large);
        Assert.Equal(1, executions);
        var broken = new OperationRegistry();
        key = broken.SessionId + ":broken";
        Assert.Equal("dispatch_failed", Code(await broken.ExecuteAsync(key, Payload(), () => throw new InvalidOperationException("failure"))));
        Assert.Equal("dispatch_failed", Code(await broken.ExecuteAsync(key, Payload(), () => Task.FromResult(Success()))));
    }

    [Fact]
    public async Task Property_order_is_ignored_but_document_change_conflicts()
    {
        var r = new OperationRegistry(); var key = r.SessionId + ":a";
        await r.ExecuteAsync(key, JsonNode.Parse("{\"a\":1,\"params\":{\"documentTitle\":\"A\"}}")!.AsObject(), () => Task.FromResult(Success()));
        Assert.True((await r.ExecuteAsync(key, JsonNode.Parse("{\"params\":{\"documentTitle\":\"A\"},\"a\":1}")!.AsObject(), () => throw new Exception()))["ok"]!.GetValue<bool>());
        Assert.Equal("idempotency_conflict", Code(await r.ExecuteAsync(key, JsonNode.Parse("{\"a\":1,\"params\":{\"documentTitle\":\"B\"}}")!.AsObject(), () => throw new Exception())));
    }

    [Fact]
    public void Every_step_requires_an_explicit_document()
    {
        Assert.False(McpHttpServer.HasExplicitTargets("create_wall", new()));
        Assert.True(McpHttpServer.HasExplicitTargets("create_wall", new() { ["documentTitle"] = "A" }));
        Assert.False(McpHttpServer.HasExplicitTargets("batch", JsonNode.Parse("{\"steps\":[{\"params\":{\"documentTitle\":\"A\"}},{\"params\":{}}]}")!.AsObject()));
    }
}
