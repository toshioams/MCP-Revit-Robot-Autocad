using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// List all loaded Families, optionally filtered by category.
/// Params: category (optional BuiltInCategory name), offset (default 0), limit (default 500).
/// offset was silently ignored before 0.8.50: paging returned the same first page forever.
/// </summary>
public sealed class ListFamiliesCommand : IRevitCommand
{
    public string Name => "list_families";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var categoryFilter = P.StrOrNull(p, "category");
        var limit = System.Math.Clamp(P.IntOr(p, "limit", 500), 1, 5000);
        var offset = System.Math.Max(0, P.IntOr(p, "offset", 0));

        var query = new FilteredElementCollector(doc)
            .OfClass(typeof(Family))
            .Cast<Family>();

        if (!string.IsNullOrWhiteSpace(categoryFilter)
            && System.Enum.TryParse<BuiltInCategory>(categoryFilter, true, out var bic))
        {
            query = query.Where(f => f.FamilyCategory?.BuiltInCategory == bic);
        }

        var all = query.OrderBy(f => f.Name).ToList();
        var families = all.Skip(offset).Take(limit).ToList();
        var arr = new JsonArray();
        foreach (var f in families)
        {
            arr.Add(new JsonObject
            {
                ["id"] = f.Id.Value,
                ["name"] = f.Name,
                ["category"] = f.FamilyCategory?.Name,
                ["categoryEnum"] = f.FamilyCategory?.BuiltInCategory.ToString(),
                ["isInPlace"] = f.IsInPlace,
                ["placementType"] = f.FamilyPlacementType.ToString(),
                ["isEditable"] = f.IsEditable,
                ["typeCount"] = f.GetFamilySymbolIds()?.Count ?? 0,
            });
        }

        return new JsonObject
        {
            ["count"] = arr.Count, ["total"] = all.Count, ["offset"] = offset,
            ["hasMore"] = offset + arr.Count < all.Count, ["nextOffset"] = offset + arr.Count, ["families"] = arr,
        };
    }
}
