using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// List FamilySymbol (types) for a specific family or category.
/// Params: familyName (optional), category (optional BuiltInCategory), limit (default 500).
/// </summary>
public sealed class ListFamilyTypesCommand : IRevitCommand
{
    public string Name => "list_family_types";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var familyName = P.StrOrNull(p, "familyName");
        var categoryFilter = P.StrOrNull(p, "category");
        var limit = System.Math.Clamp(P.IntOr(p, "limit", 500), 1, 5000);
        var includeSystemTypes = P.BoolOr(p, "includeSystemTypes", false);

        var query = new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .Cast<ElementType>()
            .Where(s => includeSystemTypes || s is FamilySymbol);

        if (!string.IsNullOrWhiteSpace(familyName))
            query = query.Where(s => s.FamilyName == familyName);

        if (!string.IsNullOrWhiteSpace(categoryFilter))
        {
            if (!System.Enum.TryParse<BuiltInCategory>(categoryFilter, true, out var bic)
                || !System.Enum.IsDefined(typeof(BuiltInCategory), bic))
                throw new RevitCommandException("invalid_parameter", $"Unknown category '{categoryFilter}'.");
            query = query.Where(s => s.Category?.BuiltInCategory == bic);
        }

        var symbols = query.OrderBy(s => s.FamilyName).ThenBy(s => s.Name).Take(limit).ToList();
        var arr = new JsonArray();
        foreach (var s in symbols)
        {
            arr.Add(new JsonObject
            {
                ["id"] = s.Id.Value,
                ["name"] = s.Name,
                ["familyName"] = s.FamilyName,
                ["familyId"] = (s as FamilySymbol)?.Family?.Id.Value,
                ["category"] = s.Category?.Name,
                ["categoryEnum"] = s.Category?.BuiltInCategory.ToString(),
                ["isActive"] = (s as FamilySymbol)?.IsActive,
                ["className"] = s.GetType().Name,
                ["isLoadableFamily"] = s is FamilySymbol,
                ["placementType"] = (s as FamilySymbol)?.Family?.FamilyPlacementType.ToString(),
            });
        }

        return new JsonObject { ["count"] = arr.Count, ["familyTypes"] = arr };
    }
}
