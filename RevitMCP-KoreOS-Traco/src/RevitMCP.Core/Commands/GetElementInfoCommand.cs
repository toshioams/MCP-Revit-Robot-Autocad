using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Returns identity + parameter values for a single element.
///
/// Parameters:
///   - id: long, required.  ElementId.Value
/// </summary>
public sealed class GetElementInfoCommand : IRevitCommand
{
    public string Name => "get_element_info";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var idValue = P.Long(ctx.Parameters, "id");

        var element = doc.GetElement(new ElementId(idValue))
            ?? throw new RevitCommandException("not_found", $"No element with id {idValue}.");

        var paramsArray = new JsonArray();
        foreach (Parameter p in element.Parameters)
        {
            paramsArray.Add(new JsonObject
            {
                ["name"] = p.Definition?.Name,
                ["parameterId"] = p.Id.Value,
                ["isShared"] = p.IsShared,
                ["sharedGuid"] = p.IsShared ? p.GUID.ToString() : null,
                ["dataTypeId"] = p.Definition?.GetDataType().TypeId,
                ["storageType"] = p.StorageType.ToString(),
                ["isReadOnly"] = p.IsReadOnly,
                ["value"] = ReadValue(p),
                ["valueString"] = SafeAsValueString(p),
                ["referencedElementName"] = p.StorageType == StorageType.ElementId && p.HasValue
                    ? doc.GetElement(p.AsElementId())?.Name : null,
            });
        }

        BoundingBoxXYZ? bbox = null;
        try { bbox = element.get_BoundingBox(null); } catch { }

        return new JsonObject
        {
            ["id"] = element.Id.Value,
            ["name"] = element.Name,
            // Stable cross-session/cross-model identifier (45-char "<guid-36>-<8 hex>").
            // Lets a caller round-trip an ACC/BIM 360 externalId back to a Revit element
            // without reverse-engineering the ElementId via the XOR heuristic.
            ["uniqueId"] = element.UniqueId,
            ["className"] = element.GetType().Name,
            ["isElementType"] = element is ElementType,
            ["boundingBoxUnits"] = "feet",
            ["parameterValueConvention"] = "Double values use Revit internal units; use dataTypeId and valueString to interpret them.",
            ["familyPlacement"] = PlacementInfo(element),
            ["category"] = element.Category?.Name,
            ["categoryEnum"] = element.Category?.BuiltInCategory.ToString(),
            ["typeId"] = element.GetTypeId()?.Value,
            ["levelId"] = element.LevelId?.Value,
            ["boundingBox"] = bbox is null ? null : new JsonObject
            {
                ["min"] = XyzToJson(bbox.Min),
                ["max"] = XyzToJson(bbox.Max),
            },
            ["parameters"] = paramsArray,
        };
    }

    private static JsonNode? ReadValue(Parameter p)
    {
        if (!p.HasValue) return null;
        return p.StorageType switch
        {
            StorageType.String    => JsonValue.Create(p.AsString()),
            StorageType.Integer   => JsonValue.Create(p.AsInteger()),
            StorageType.Double    => JsonValue.Create(p.AsDouble()),
            StorageType.ElementId => JsonValue.Create(p.AsElementId()?.Value),
            _ => null,
        };
    }

    private static JsonObject? PlacementInfo(Element element)
    {
        var instance = element as FamilyInstance;
        var symbol = element as FamilySymbol ?? instance?.Symbol;
        var family = element as Family ?? symbol?.Family;
        if (family is null) return null;
        var result = new JsonObject
        {
            ["familyId"] = family.Id.Value,
            ["familyName"] = family.Name,
            ["placementType"] = family.FamilyPlacementType.ToString(),
            ["isInPlace"] = family.IsInPlace,
        };
        if (instance is null) return result;
        result["hostId"] = instance.Host?.Id.Value;
        result["hostClassName"] = instance.Host?.GetType().Name;
        result["facingOrientation"] = XyzToJson(instance.FacingOrientation);
        result["handOrientation"] = XyzToJson(instance.HandOrientation);
        result["canFlipFacing"] = instance.CanFlipFacing;
        result["canFlipHand"] = instance.CanFlipHand;
        result["facingFlipped"] = instance.FacingFlipped;
        result["handFlipped"] = instance.HandFlipped;
        if (instance.Location is LocationPoint point)
        {
            result["insertionPointFeet"] = XyzToJson(point.Point);
            result["rotationRadians"] = point.Rotation;
        }
        return result;
    }

    private static string? SafeAsValueString(Parameter p)
    {
        try { return p.AsValueString(); } catch { return null; }
    }

    private static JsonObject XyzToJson(XYZ p) => new()
    {
        ["x"] = p.X,
        ["y"] = p.Y,
        ["z"] = p.Z,
    };
}
