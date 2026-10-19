using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Create a Floor from a closed polygonal profile.
///
/// Parameters:
///   - profile:        [{x,y,z?}, ...]   required, at least 3 points, closed automatically
///   - levelName:      string             optional, defaults to lowest level
///   - floorTypeName:  string             optional, defaults to first FloorType
///   - heightOffset:   number             optional, top of floor above the level (units), default 0.
///                                        Canopies, steps and yard paving no longer need a level each.
///   - structural:     bool               optional, default false
///   - units:          "meters"|"feet"    optional, default "meters"
/// </summary>
public sealed class CreateFloorCommand : IRevitCommand
{
    public string Name => "create_floor";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);

        var pts = P.XyzList(p, "profile", units);
        if (pts.Count < 3)
            throw new RevitCommandException("invalid_parameter", "Floor profile must have at least 3 points.");

        var level = CreateWallCommand.ResolveLevel(doc, P.StrOrNull(p, "levelName"));
        var floorType = ResolveFloorType(doc, P.StrOrNull(p, "floorTypeName"));

        // Project all points to the level elevation and build CurveLoops (outer profile + optional holes).
        var snapDeg = P.DblOr(p, "snapNearAxisDegrees", 0.5);
        var snapped = 0;
        CurveLoop Loop(IList<XYZ> raw, string what)
        {
            var xy = raw.Select(v => (v.X, v.Y)).ToList();
            if (snapDeg > 0) snapped += ProfileSnap.SnapNearAxis(xy, snapDeg);
            var loop = new CurveLoop();
            for (var i = 0; i < xy.Count; i++)
            {
                var a = new XYZ(xy[i].X, xy[i].Y, level.Elevation);
                var b = new XYZ(xy[(i + 1) % xy.Count].X, xy[(i + 1) % xy.Count].Y, level.Elevation);
                if (a.DistanceTo(b) < 1e-6)
                    throw new RevitCommandException("invalid_parameter", $"{what} segment {i} has zero length.");
                loop.Append(Line.CreateBound(a, b));
            }
            return loop;
        }

        var loops = new List<CurveLoop> { Loop(pts, "Profile") };
        if (p["holes"] is System.Text.Json.Nodes.JsonArray holes)
        {
            for (var h = 0; h < holes.Count; h++)
            {
                if (holes[h] is not System.Text.Json.Nodes.JsonArray hole || hole.Count < 3)
                    throw new RevitCommandException("invalid_parameter", $"holes[{h}] needs at least 3 points.");
                var wrapper = new System.Text.Json.Nodes.JsonObject { ["pts"] = hole.DeepClone() };
                loops.Add(Loop(P.XyzList(wrapper, "pts", units), $"holes[{h}]"));
            }
        }
        var toFeet = units == "feet" ? 1.0 : P.MetersToFeet;
        // Sloped floor (ramps): slope {from:{x,y}, to:{x,y}, rise} rises `rise` from `from` to `to`; the arrow
        // tail sits at the level (+ heightOffset). Floor.Create takes the angle in radians.
        Line? slopeArrow = null;
        double slopeAngle = 0;
        if (p["slope"] is JsonObject sl)
        {
            var from = P.Xyz(sl, "from", units); var to = P.Xyz(sl, "to", units);
            var a = new XYZ(from.X, from.Y, level.Elevation); var b = new XYZ(to.X, to.Y, level.Elevation);
            if (a.DistanceTo(b) < 0.01)
                throw new RevitCommandException("invalid_parameter", "slope.from and slope.to must be distinct plan points.");
            slopeArrow = Line.CreateBound(a, b);
            slopeAngle = System.Math.Atan2(P.Dbl(sl, "rise") * toFeet, a.DistanceTo(b));
        }
        var floor = Floor.Create(doc, loops, floorType.Id, level.Id, P.BoolOr(p, "structural", false), slopeArrow, slopeAngle);
        var offset = P.DblOr(p, "heightOffset", 0) * toFeet;
        if (System.Math.Abs(offset) > 1e-9)
        {
            var param = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)
                ?? throw new RevitCommandException("command_failed", "Floor has no height offset parameter.");
            param.Set(offset);
            doc.Regenerate();
            if (System.Math.Abs(param.AsDouble() - offset) > 1e-6)
                throw new RevitCommandException("verification_failed", "Revit did not keep the height offset.");
        }
        var box = floor.get_BoundingBox(null);

        return new JsonObject
        {
            ["id"] = floor.Id.Value,
            ["levelName"] = level.Name,
            ["floorTypeName"] = floorType.Name,
            ["pointCount"] = pts.Count,
            ["holeCount"] = loops.Count - 1,
            ["edgesSnappedToAxis"] = snapped,
            ["heightOffsetMeters"] = offset / P.MetersToFeet,
            ["topElevationMeters"] = box is null ? null : box.Max.Z / P.MetersToFeet,
            ["bottomElevationMeters"] = box is null ? null : box.Min.Z / P.MetersToFeet,
        };
    }

    private static FloorType ResolveFloorType(Document doc, string? name)
    {
        var query = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return query.FirstOrDefault()
                ?? throw new RevitCommandException("not_found", "No FloorType exists in the document.");
        }
        return query.FirstOrDefault(t => t.Name == name)
            ?? throw new RevitCommandException("not_found", $"FloorType '{name}' not found.");
    }
}
