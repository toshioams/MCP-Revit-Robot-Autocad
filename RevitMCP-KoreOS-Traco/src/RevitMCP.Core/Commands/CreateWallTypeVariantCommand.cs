using System;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Duplicate a basic wall type with a new total width (the structural core layer absorbs the
/// difference) or explicit layer widths. Needed to model walls traced from a DWG, whose thicknesses
/// rarely match the template types. Reuses an existing type with the same name and width.
/// </summary>
public sealed class CreateWallTypeVariantCommand : IRevitCommand
{
    public string Name => "create_wall_type_variant";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var source = doc.GetElement(new ElementId(P.Long(p, "sourceTypeId"))) as WallType
            ?? throw new RevitCommandException("invalid_parameter", "sourceTypeId must identify a WallType.");
        if (source.Kind != WallKind.Basic)
            throw new RevitCommandException("unsupported_type", "Only basic walls (not curtain/stacked) are supported.");
        var name = P.StrOrNull(p, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new RevitCommandException("invalid_parameter", "A nonempty new type name is required.");

        using var structure = SimpleStructure(source.GetCompoundStructure());
        if (structure is null || structure.IsVerticallyCompound)
            throw new RevitCommandException("unsupported_type", "Source type needs a simple (not vertically compound) structure.");

        if (p["layerWidthsMeters"] is JsonArray widths)
        {
            if (widths.Count != structure.LayerCount)
                throw new RevitCommandException("invalid_parameter", $"Supply {structure.LayerCount} widths (native layer order).");
            for (int i = 0; i < widths.Count; i++)
            {
                var w = widths[i]?.GetValue<double>() ?? double.NaN;
                if (!double.IsFinite(w) || w < 0 || (w == 0 && structure.GetLayerFunction(i) != MaterialFunctionAssignment.Membrane))
                    throw new RevitCommandException("invalid_parameter", "Widths must be finite and positive, except zero-width membranes.");
                structure.SetLayerWidth(i, w * P.MetersToFeet);
            }
        }
        else if (p["widthMeters"] is not null)
        {
            var target = P.Dbl(p, "widthMeters");
            var first = structure.GetFirstCoreLayerIndex(); var last = structure.GetLastCoreLayerIndex();
            var core = Enumerable.Range(first, last - first + 1).ToList();
            var absorb = core.Where(i => structure.GetLayerFunction(i) == MaterialFunctionAssignment.Structure)
                .DefaultIfEmpty(core.OrderByDescending(structure.GetLayerWidth).First())
                .OrderByDescending(structure.GetLayerWidth).First();
            var others = structure.GetWidth() - structure.GetLayerWidth(absorb);
            var w = target * P.MetersToFeet - others;
            if (!double.IsFinite(target) || w <= 1e-6)
                throw new RevitCommandException("invalid_parameter",
                    $"widthMeters {target} is not larger than the other layers ({others / P.MetersToFeet:0.###} m); pass layerWidthsMeters.");
            structure.SetLayerWidth(absorb, w);
        }
        else if (p["layerMaterialIds"] is null)
            throw new RevitCommandException("invalid_parameter", "Pass widthMeters, layerWidthsMeters or layerMaterialIds.");

        // Native order: exterior to interior. Null preserves a layer's material.
        if (p["layerMaterialIds"] is JsonArray materials)
        {
            if (materials.Count != structure.LayerCount)
                throw new RevitCommandException("invalid_parameter", $"Supply {structure.LayerCount} material IDs (null preserves a layer).");
            for (int i = 0; i < materials.Count; i++)
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

        var existing = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault(t => t.Name == name);
        if (existing is not null)
        {
            using var es = existing.GetCompoundStructure();
            if (existing.Kind != WallKind.Basic || es is null || es.IsVerticallyCompound ||
                es.LayerCount != structure.LayerCount ||
                es.GetFirstCoreLayerIndex() != structure.GetFirstCoreLayerIndex() ||
                es.GetLastCoreLayerIndex() != structure.GetLastCoreLayerIndex() ||
                Enumerable.Range(0, structure.LayerCount).Any(i =>
                    Math.Abs(es.GetLayerWidth(i) - structure.GetLayerWidth(i)) > 1e-6 ||
                    es.GetMaterialId(i) != structure.GetMaterialId(i) ||
                    es.GetLayerFunction(i) != structure.GetLayerFunction(i)))
                throw new RevitCommandException("name_collision", $"Type '{name}' exists with a different layer structure or materials; choose a new name.");
            return Report(existing, source, reused: true);
        }

        var variant = (WallType)source.Duplicate(name);
        var mark = variant.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
        if (mark is not null && !mark.IsReadOnly) mark.Set("");
        if (!structure.IsValid(doc, out var errors, out _))
            throw new RevitCommandException("invalid_parameter", $"Resulting structure is invalid ({errors.Count} errors).");
        variant.SetCompoundStructure(structure);
        doc.Regenerate();
        if (Math.Abs(variant.Width - structure.GetWidth()) > 1e-6)
            throw new RevitCommandException("verification_failed", "Revit did not retain the requested width.");
        using var actual = variant.GetCompoundStructure();
        if (Enumerable.Range(0, structure.LayerCount).Any(i => actual.GetMaterialId(i) != structure.GetMaterialId(i)))
            throw new RevitCommandException("verification_failed", "Revit did not retain the requested layer materials.");
        return Report(variant, source, reused: false);
    }

    static JsonObject Report(WallType t, WallType source, bool reused)
    {
        using var s = t.GetCompoundStructure();
        return new JsonObject
        {
            ["typeId"] = t.Id.Value, ["name"] = t.Name, ["sourceTypeId"] = source.Id.Value, ["reused"] = reused,
            ["widthMeters"] = Math.Round(t.Width / P.MetersToFeet, 6),
            ["layers"] = new JsonArray(Enumerable.Range(0, s.LayerCount).Select(i => (JsonNode)new JsonObject
            {
                ["index"] = i, ["function"] = s.GetLayerFunction(i).ToString(),
                ["widthMeters"] = Math.Round(s.GetLayerWidth(i) / P.MetersToFeet, 6), ["materialId"] = s.GetMaterialId(i).Value,
            }).ToArray()),
        };
    }

    /// <summary>
    /// Revit flags some single-layer types as vertically compound (seen in a template upgraded from
    /// Revit 2025). With one layer the layer is the whole wall, so an equivalent simple structure
    /// (same function, width and material) loses nothing and can be resized.
    /// </summary>
    internal static CompoundStructure? SimpleStructure(CompoundStructure? structure)
    {
        if (structure is null || !structure.IsVerticallyCompound || structure.LayerCount != 1) return structure;
        using (structure)
            return CompoundStructure.CreateSingleLayerCompoundStructure(
                structure.GetLayerFunction(0), structure.GetLayerWidth(0), structure.GetMaterialId(0));
    }
}
