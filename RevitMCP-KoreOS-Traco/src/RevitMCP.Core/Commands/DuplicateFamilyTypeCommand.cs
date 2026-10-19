using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Duplicate a family type (FamilySymbol) under a new name.  Set parameters on the
/// new type afterwards with set_parameter (using the returned typeId), which already
/// handles unit conversion.
///
/// Params:
///   - sourceTypeId: long, required — ElementId of the FamilySymbol to copy.
///   - newName:      string, required — name for the new type (must be unique in the family).
///
/// Returns: { typeId, name, familyName }.
/// </summary>
public sealed class DuplicateFamilyTypeCommand : IRevitCommand
{
    public string Name => "duplicate_family_type";
    public bool IsReadOnly => false;
    public string RiskLevel => "low";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;

        var srcId = new ElementId(P.Long(p, "sourceTypeId"));
        // Any ElementType: family symbols and system types (fascia/gutter, roof, wall...).
        var src = doc.GetElement(srcId) as ElementType
            ?? throw new RevitCommandException("not_found",
                $"Element {srcId.Value} is not an element type.");

        var newName = P.Str(p, "newName");

        var dup = src.Duplicate(newName) as ElementType
            ?? throw new RevitCommandException("command_failed",
                "Duplicate did not return an element type.");

        return new JsonObject
        {
            ["typeId"] = dup.Id.Value,
            ["name"] = dup.Name,
            ["familyName"] = dup.FamilyName,
            ["className"] = dup.GetType().Name,
        };
    }
}
