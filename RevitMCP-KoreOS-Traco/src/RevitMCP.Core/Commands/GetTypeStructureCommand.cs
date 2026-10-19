using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

public sealed class GetTypeStructureCommand : IRevitCommand
{
    public string Name => "get_type_structure";
    public bool IsReadOnly => true;
    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var id = P.Long(ctx.Parameters, "typeId");
        var type = doc.GetElement(new ElementId(id)) as HostObjAttributes
            ?? throw new RevitCommandException("unsupported_type", "typeId must identify a compound-capable wall, floor, roof or ceiling type.");
        using var structure = type.GetCompoundStructure();
        var layers = new JsonArray();
        if (structure is not null)
        {
            var index = 0;
            foreach (var layer in structure.GetLayers())
            {
                layers.Add(new JsonObject { ["index"] = index,
                    ["function"] = layer.Function.ToString(),
                    ["widthMeters"] = layer.Width / P.MetersToFeet,
                    ["materialId"] = layer.MaterialId.Value,
                    ["materialName"] = doc.GetElement(layer.MaterialId)?.Name,
                    ["isCore"] = index >= structure.GetFirstCoreLayerIndex() && index <= structure.GetLastCoreLayerIndex() });
                index++;
            }
        }
        return new JsonObject { ["typeId"] = id, ["name"] = type.Name,
            ["className"] = type.GetType().Name, ["hasCompoundStructure"] = structure is not null,
            ["isVerticallyCompound"] = structure?.IsVerticallyCompound,
            ["totalWidthMeters"] = structure is null ? null : structure.GetWidth() / P.MetersToFeet,
            ["layers"] = layers,
            ["note"] = "Layer order is Revit's native order. Vertically compound walls require region geometry for local thickness; this is the type layer list." };
    }
}
