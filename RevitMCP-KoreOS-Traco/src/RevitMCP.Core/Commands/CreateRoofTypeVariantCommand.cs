using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Duplicate a roof type and give it an explicit layer list, top (exterior) to bottom.
/// Used for the covering package over exposed rafters: tile, battens, membrane, boarding.
/// </summary>
public sealed class CreateRoofTypeVariantCommand : IRevitCommand
{
    public string Name => "create_roof_type_variant";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var source = doc.GetElement(new ElementId(P.Long(p, "sourceTypeId"))) as RoofType
            ?? throw new RevitCommandException("invalid_parameter", "sourceTypeId must identify a RoofType.");
        var name = P.StrOrNull(p, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new RevitCommandException("invalid_parameter", "A nonempty new type name is required.");
        if (new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Any(t => t.Name == name))
            throw new RevitCommandException("name_collision", "Roof type name already exists; inspect it before retrying.");
        if (p["layers"] is not JsonArray raw || raw.Count == 0 || raw.Count > 20)
            throw new RevitCommandException("invalid_parameter", "layers: 1..20 objects, top (exterior) first.");

        var layers = new List<CompoundStructureLayer>();
        int core = -1;
        for (int i = 0; i < raw.Count; i++)
        {
            if (raw[i] is not JsonObject l) throw new RevitCommandException("invalid_parameter", "Each layer must be an object.");
            var function = Enum.TryParse<MaterialFunctionAssignment>(P.StrOrNull(l, "function"), true, out var f) ? f
                : throw new RevitCommandException("invalid_parameter", "function: Structure|Substrate|Insulation|Finish1|Finish2|Membrane|StructuralDeck.");
            var width = P.Dbl(l, "widthMeters");
            if (!double.IsFinite(width) || width < 0 || (function == MaterialFunctionAssignment.Membrane) != (width == 0))
                throw new RevitCommandException("invalid_parameter", "Widths must be positive; membranes (and only membranes) have zero width.");
            var material = ResolveMaterial(doc, l);
            layers.Add(new CompoundStructureLayer(width * P.MetersToFeet, function, material));
            if (P.BoolOr(l, "core", false)) { if (core >= 0) throw new RevitCommandException("invalid_parameter", "Only one core layer is supported."); core = i; }
        }
        if (core < 0) core = layers.FindIndex(l => l.Function == MaterialFunctionAssignment.Structure);
        if (core < 0) throw new RevitCommandException("invalid_parameter", "Mark one layer core:true or supply a Structure layer.");

        var structure = CompoundStructure.CreateSimpleCompoundStructure(layers);
        structure.SetNumberOfShellLayers(ShellLayerType.Exterior, core);
        structure.SetNumberOfShellLayers(ShellLayerType.Interior, layers.Count - core - 1);
        structure.StructuralMaterialIndex = core;
        if (!structure.IsValid(doc, out IDictionary<int, CompoundStructureError> errors, out _))
            throw new RevitCommandException("invalid_parameter", "Invalid layer structure: "
                + string.Join("; ", errors.Select(e => $"layer {e.Key}: {e.Value}")));

        var variant = (RoofType)source.Duplicate(name);
        var mark = variant.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
        if (mark is not null && !mark.IsReadOnly) mark.Set("");
        variant.SetCompoundStructure(structure);
        doc.Regenerate();

        using var actual = variant.GetCompoundStructure();
        if (actual.LayerCount != layers.Count)
            throw new RevitCommandException("verification_failed", "Revit did not retain the layer count.");
        var result = new JsonArray();
        for (int i = 0; i < actual.LayerCount; i++)
        {
            if (Math.Abs(actual.GetLayerWidth(i) - layers[i].Width) > 1e-8 || actual.GetLayerFunction(i) != layers[i].Function)
                throw new RevitCommandException("verification_failed", $"Layer {i} differs after saving.");
            var m = doc.GetElement(actual.GetMaterialId(i)) as Material;
            result.Add(new JsonObject { ["index"] = i, ["function"] = actual.GetLayerFunction(i).ToString(),
                ["widthMeters"] = actual.GetLayerWidth(i) / P.MetersToFeet, ["materialId"] = actual.GetMaterialId(i).Value,
                ["materialName"] = m?.Name, ["isCore"] = i == core });
        }
        return new JsonObject { ["typeId"] = variant.Id.Value, ["name"] = variant.Name, ["sourceTypeId"] = source.Id.Value,
            ["totalWidthMeters"] = actual.GetWidth() / P.MetersToFeet, ["layers"] = result,
            ["note"] = "Type only; no roof changed. Layer widths are nominal envelopes (tiles/battens are not individual elements)." };
    }

    static ElementId ResolveMaterial(Document doc, JsonObject layer)
    {
        if (layer["materialId"] is not null)
            return doc.GetElement(new ElementId(P.Long(layer, "materialId"))) is Material m ? m.Id
                : throw new RevitCommandException("invalid_parameter", "materialId must identify a material.");
        var name = P.StrOrNull(layer, "materialName");
        if (name is null) return ElementId.InvalidElementId;
        var matches = new FilteredElementCollector(doc).OfClass(typeof(Material)).Where(e => e.Name == name).ToList();
        return matches.Count == 1 ? matches[0].Id
            : throw new RevitCommandException("invalid_parameter", $"materialName '{name}' matched {matches.Count} materials.");
    }
}
