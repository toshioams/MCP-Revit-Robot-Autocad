using System;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

public sealed class CreateFloorTypeVariantCommand : IRevitCommand
{
    public string Name => "create_floor_type_variant";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var source = doc.GetElement(new ElementId(P.Long(p, "sourceTypeId"))) as FloorType
            ?? throw new RevitCommandException("invalid_parameter", "sourceTypeId must identify a FloorType.");
        var name = P.StrOrNull(p, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new RevitCommandException("invalid_parameter", "A nonempty new type name is required.");
        if (new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().Any(t => t.Name == name))
            throw new RevitCommandException("invalid_parameter", "Type name already exists; inspect it before retrying.");
        using var structure = source.GetCompoundStructure();
        if (structure is null || structure.IsVerticallyCompound)
            throw new RevitCommandException("unsupported_type", "A simple compound structure is required.");
        var widths = p["layerWidthsMeters"] as JsonArray;
        if (widths is null || widths.Count != structure.LayerCount)
            throw new RevitCommandException("invalid_parameter", "Supply one width in meters for every existing layer, in native order.");
        for (var i = 0; i < widths.Count; i++)
        {
            var width = widths[i]?.GetValue<double>() ?? double.NaN;
            if (!double.IsFinite(width) || width < 0 || (width == 0 && structure.GetLayerFunction(i) != MaterialFunctionAssignment.Membrane))
                throw new RevitCommandException("invalid_parameter", "Widths must be finite and positive, except zero-width membranes.");
            structure.SetLayerWidth(i, width * P.MetersToFeet);
        }
        // Native order, top to bottom. Null preserves a layer's material.
        if (p["layerMaterialIds"] is JsonArray materials)
        {
            if (materials.Count != structure.LayerCount)
                throw new RevitCommandException("invalid_parameter", $"Supply {structure.LayerCount} material IDs (null preserves a layer).");
            for (var i = 0; i < materials.Count; i++)
            {
                if (materials[i] is null) continue;
                var materialId = new ElementId(materials[i]!.GetValue<long>());
                if (doc.GetElement(materialId) is not Material)
                    throw new RevitCommandException("invalid_parameter", $"layerMaterialIds[{i}] must identify a Material in this document.");
                structure.SetMaterialId(i, materialId);
            }
        }
        else if (p["layerMaterialIds"] is not null)
            throw new RevitCommandException("invalid_parameter", "layerMaterialIds must be an array.");
        var ids = p["floorIds"] as JsonArray ?? new JsonArray();
        var floors = ids.Select(n => doc.GetElement(new ElementId(n!.GetValue<long>())) as Floor
            ?? throw new RevitCommandException("invalid_parameter", "floorIds must identify existing floors.")).ToList();
        if (floors.Any(f => f.GetTypeId() != source.Id || f.Pinned || f.GroupId != ElementId.InvalidElementId))
            throw new RevitCommandException("invalid_parameter", "Target floors must use the source type and must not be pinned or grouped.");
        var variant = (FloorType)source.Duplicate(name);
        // A distinct type must not inherit a conflicting type mark.
        var mark = variant.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
        if (mark is not null && !mark.IsReadOnly) mark.Set("");
        variant.SetCompoundStructure(structure);
        foreach (var floor in floors) floor.ChangeTypeId(variant.Id);
        doc.Regenerate();
        using var actual = variant.GetCompoundStructure();
        for (var i = 0; i < widths.Count; i++)
            if (Math.Abs(actual.GetLayerWidth(i) / P.MetersToFeet - widths[i]!.GetValue<double>()) > 1e-8)
                throw new RevitCommandException("verification_failed", "Revit did not retain requested layer width.");
        if (Enumerable.Range(0, structure.LayerCount).Any(i => actual.GetMaterialId(i) != structure.GetMaterialId(i)))
            throw new RevitCommandException("verification_failed", "Revit did not retain the requested layer materials.");
        var changed = new JsonArray();
        foreach (var floor in floors) changed.Add(floor.Id.Value);
        return new JsonObject { ["typeId"] = variant.Id.Value, ["sourceTypeId"] = source.Id.Value,
            ["name"] = variant.Name, ["totalWidthMeters"] = actual.GetWidth() / P.MetersToFeet,
            ["floorIds"] = changed, ["sourceUnchanged"] = true };
    }
}
