using System.Linq;
using System.Text.Json.Nodes;

namespace RevitMCPAddin.Commands;

public sealed class GetWarningsCommand : IRevitCommand
{
    public string Name => "get_warnings";
    public bool IsReadOnly => true;
    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var offset = System.Math.Max(0, P.IntOr(p, "offset", 0));
        var limit = System.Math.Clamp(P.IntOr(p, "limit", 100), 1, 500);
        long? elementId = p["elementId"] is null ? null : P.Long(p, "elementId");
        var warnings = doc.GetWarnings().Where(w => !elementId.HasValue
            || w.GetFailingElements().Any(id => id.Value == elementId)
            || w.GetAdditionalElements().Any(id => id.Value == elementId)).ToList();
        var items = new JsonArray();
        foreach (var warning in warnings.Skip(offset).Take(limit))
        {
            var ids = new JsonArray();
            foreach (var id in warning.GetFailingElements()) ids.Add(id.Value);
            var additional = new JsonArray();
            foreach (var id in warning.GetAdditionalElements()) additional.Add(id.Value);
            items.Add(new JsonObject { ["failureId"] = warning.GetFailureDefinitionId().Guid.ToString(),
                ["description"] = warning.GetDescriptionText(), ["severity"] = warning.GetSeverity().ToString(),
                ["elementIds"] = ids, ["additionalElementIds"] = additional });
        }
        var more = offset + items.Count < warnings.Count;
        return new JsonObject { ["total"] = warnings.Count, ["count"] = items.Count,
            ["offset"] = offset, ["hasMore"] = more,
            ["nextOffset"] = more ? offset + items.Count : null, ["warnings"] = items };
    }
}
