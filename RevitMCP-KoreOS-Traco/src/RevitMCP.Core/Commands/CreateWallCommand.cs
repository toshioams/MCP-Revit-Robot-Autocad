using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Creates a single straight wall.  Transaction is owned by the dispatcher.
///
/// Parameters (units: meters by default; pass "units":"feet" for imperial):
///   - start:        { x, y, z? }     required
///   - end:          { x, y, z? }     required
///   - height:       number           optional, default 3.0 (in user units)
///   - levelName:    string           optional, defaults to lowest level
///   - wallTypeName: string           optional, defaults to first WallType
///   - wallTypeId:   long             optional, wins over wallTypeName
///   - locationLine: centerline | coreCenterline | finishExterior | finishInterior | coreExterior | coreInterior
///                   optional, default centerline. start/end lie on that line after creation (verified).
///                   Before 0.8.50 the template's default location line was left in place (live: walls
///                   reported "Face de acabamento: Externa" while traced from centerlines).
///   - structural:   bool             optional, default false
///   - units:        "meters"|"feet"  optional, default "meters"
/// </summary>
public sealed class CreateWallCommand : IRevitCommand
{
    public string Name => "create_wall";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);
        var toFeet = units == "feet" ? 1.0 : P.MetersToFeet;

        var start = P.Xyz(p, "start", units);
        var end = P.Xyz(p, "end", units);
        if (start.DistanceTo(end) < 1e-6)
            throw new RevitCommandException("invalid_parameter", "Start and end points are coincident.");

        var height = P.DblOr(p, "height", 3.0) * toFeet;
        var baseOffset = P.DblOr(p, "baseOffset", 0) * toFeet;
        if (height <= 0)
            throw new RevitCommandException("invalid_parameter", "height must be positive.");
        var structural = P.BoolOr(p, "structural", false);

        var level = ResolveLevel(doc, P.StrOrNull(p, "levelName"));
        var wallType = p["wallTypeId"] is not null
            ? doc.GetElement(new ElementId(P.Long(p, "wallTypeId"))) as WallType
                ?? throw new RevitCommandException("invalid_parameter", "wallTypeId must identify a WallType.")
            : ResolveWallType(doc, P.StrOrNull(p, "wallTypeName"));
        var locationLine = (P.StrOrNull(p, "locationLine") ?? "centerline").ToLowerInvariant() switch
        {
            "centerline" => WallLocationLine.WallCenterline, "corecenterline" => WallLocationLine.CoreCenterline,
            "finishexterior" => WallLocationLine.FinishFaceExterior, "finishinterior" => WallLocationLine.FinishFaceInterior,
            "coreexterior" => WallLocationLine.CoreExterior, "coreinterior" => WallLocationLine.CoreInterior,
            _ => throw new RevitCommandException("invalid_parameter", "locationLine: centerline | coreCenterline | finishExterior | finishInterior | coreExterior | coreInterior."),
        };

        var baseline = Line.CreateBound(
            new XYZ(start.X, start.Y, level.Elevation),
            new XYZ(end.X, end.Y, level.Elevation));

        var created = Wall.Create(
            document: doc,
            curve: baseline,
            wallTypeId: wallType.Id,
            levelId: level.Id,
            height: height,
            offset: baseOffset,
            flip: false,
            structural: structural);

        // Put the requested location line on the given baseline: set the parameter, then move the wall if
        // its location curve drifted (Revit keeps the geometry and moves the curve when the parameter changes).
        var keyRef = created.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
        if (keyRef is not null && !keyRef.IsReadOnly && keyRef.AsInteger() != (int)locationLine)
            keyRef.Set((int)locationLine);
        doc.Regenerate();
        if (created.Location is LocationCurve lc && lc.Curve is Line placed)
        {
            var delta = baseline.GetEndPoint(0) - placed.GetEndPoint(0);
            if (delta.GetLength() > 1e-6)
            {
                ElementTransformUtils.MoveElement(doc, created.Id, new XYZ(delta.X, delta.Y, 0));
                doc.Regenerate();
            }
            var finalCurve = ((LocationCurve)created.Location).Curve;
            if (finalCurve.GetEndPoint(0).DistanceTo(baseline.GetEndPoint(0)) > 1e-4 || finalCurve.GetEndPoint(1).DistanceTo(baseline.GetEndPoint(1)) > 1e-4)
                throw new RevitCommandException("verification_failed", "Wall location line does not match start/end after creation.");
        }

        return new JsonObject
        {
            ["id"] = created.Id.Value,
            ["levelName"] = level.Name,
            ["wallTypeName"] = wallType.Name,
            ["wallTypeId"] = wallType.Id.Value,
            ["locationLine"] = ((WallLocationLine)(created.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM)?.AsInteger() ?? 0)).ToString(),
            ["lengthFeet"] = baseline.Length,
            ["heightFeet"] = height,
            ["baseOffsetMeters"] = baseOffset / P.MetersToFeet,
            ["baseElevationMeters"] = (level.Elevation + baseOffset) / P.MetersToFeet,
            ["topElevationMeters"] = (level.Elevation + baseOffset + height) / P.MetersToFeet,
        };
    }

    internal static Level ResolveLevel(Document doc, string? name)
    {
        var query = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return query.OrderBy(l => l.Elevation).FirstOrDefault()
                ?? throw new RevitCommandException("not_found", "No Level exists in the document.");
        }
        return query.FirstOrDefault(l => l.Name == name)
            ?? throw new RevitCommandException("not_found", $"Level '{name}' not found.");
    }

    internal static WallType ResolveWallType(Document doc, string? name)
    {
        var query = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return query.FirstOrDefault()
                ?? throw new RevitCommandException("not_found", "No WallType exists in the document.");
        }
        return query.FirstOrDefault(w => w.Name == name)
            ?? throw new RevitCommandException("not_found", $"WallType '{name}' not found.");
    }
}
