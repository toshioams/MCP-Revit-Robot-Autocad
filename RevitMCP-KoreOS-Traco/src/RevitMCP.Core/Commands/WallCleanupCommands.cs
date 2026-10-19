using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Close wall ends that stop short of, or overshoot, the wall they meet (L, T, collinear gaps), then
/// allow joins at the moved ends. Walls traced from a poor DWG did not meet; rooms leaked. Curtain
/// walls are skipped unless includeCurtain (glass stops at columns on purpose). Hosted doors/windows
/// are kept at their absolute position. dryRun previews every move through the dispatcher rollback.
/// </summary>
public sealed class SnapWallEndsCommand : IRevitCommand
{
    public string Name => "snap_wall_ends";
    public bool IsReadOnly => false;
    public string RiskLevel => "medium";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var tolerance = P.DblOr(p, "toleranceMeters", 0.6);
        if (tolerance <= 0 || tolerance > 3) throw new RevitCommandException("invalid_parameter", "toleranceMeters: (0, 3].");
        var includeCurtain = P.BoolOr(p, "includeCurtain", false);

        IEnumerable<Wall> source;
        if (p["wallIds"] is JsonArray ids && ids.Count > 0)
            source = ids.Select(n => doc.GetElement(new ElementId(n!.GetValue<long>())) as Wall
                ?? throw new RevitCommandException("invalid_parameter", $"{n} is not a wall."));
        else
        {
            var levelName = P.StrOrNull(p, "levelName");
            source = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Where(w => levelName is null || (doc.GetElement(w.LevelId) as Level)?.Name == levelName);
        }
        var walls = source.Where(w => includeCurtain || w.WallType.Kind != WallKind.Curtain)
            .Where(w => w.Location is LocationCurve { Curve: Line }).ToList();
        if (walls.Count < 2) throw new RevitCommandException("invalid_parameter", "Need at least two straight walls.");

        var byId = walls.ToDictionary(w => w.Id.Value);
        var plan = WallEndSnapper.Plan(walls.Select(w =>
        {
            var c = ((LocationCurve)w.Location).Curve;
            var a = c.GetEndPoint(0); var b = c.GetEndPoint(1);
            return new WallEndSnapper.Wall(w.Id.Value, a.X / P.MetersToFeet, a.Y / P.MetersToFeet, b.X / P.MetersToFeet, b.Y / P.MetersToFeet, w.Width / P.MetersToFeet);
        }).ToList(), tolerance, P.DblOr(p, "minAngleDeg", 5));

        var report = new JsonArray();
        foreach (var group in plan.GroupBy(m => m.Id))
        {
            var wall = byId[group.Key];
            var lc = (LocationCurve)wall.Location;
            var line = (Line)lc.Curve;
            var a = line.GetEndPoint(0); var b = line.GetEndPoint(1);
            var inserts = wall.FindInserts(true, false, false, false).Select(id => doc.GetElement(id))
                .Where(e => e?.Location is LocationPoint).Select(e => (e: e!, at: ((LocationPoint)e!.Location).Point)).ToList();
            foreach (var m in group)
            {
                var to = new XYZ(m.ToX * P.MetersToFeet, m.ToY * P.MetersToFeet, m.End == 0 ? a.Z : b.Z);
                if (m.End == 0) a = to; else b = to;
            }
            if (a.DistanceTo(b) < 0.1 * P.MetersToFeet) continue;
            lc.Curve = Line.CreateBound(a, b);
            foreach (var m in group) WallUtils.AllowWallJoinAtEnd(wall, m.End);
            doc.Regenerate();
            var corrected = 0;
            foreach (var (e, at) in inserts)
            {
                var now = ((LocationPoint)e.Location).Point;
                if (now.DistanceTo(at) > 1e-4) { ElementTransformUtils.MoveElement(doc, e.Id, at - now); corrected++; }
            }
            foreach (var m in group)
                report.Add(new JsonObject
                {
                    ["wallId"] = m.Id, ["end"] = m.End, ["kind"] = m.Kind, ["targetWallId"] = m.TargetId,
                    ["moveMeters"] = Math.Round(m.Distance, 4),
                    ["from"] = new JsonArray(Math.Round(m.FromX, 4), Math.Round(m.FromY, 4)), ["to"] = new JsonArray(Math.Round(m.ToX, 4), Math.Round(m.ToY, 4)),
                    ["insertsRestored"] = corrected,
                });
        }
        doc.Regenerate();
        var joinedEnds = 0; var freeEnds = 0;
        foreach (var w in walls)
            for (int end = 0; end < 2; end++)
            {
                var n = ((LocationCurve)w.Location).get_ElementsAtJoin(end)?.Size ?? 0;
                if (n > 1) joinedEnds++; else freeEnds++;
            }
        return new JsonObject
        {
            ["wallsConsidered"] = walls.Count, ["moves"] = report.Count, ["changes"] = report,
            ["joinedEndsAfter"] = joinedEnds, ["freeEndsAfter"] = freeEnds, ["toleranceMeters"] = tolerance,
            ["note"] = "Free ends can be real (site walls, counters). Check rooms and a cropped plan image after committing.",
        };
    }
}

/// <summary>
/// Room separation lines on a level's plan view: close rooms where the boundary is not a wall (glass
/// gaps at columns, open fronts, circulation limits). Needed for the showroom: glass runs stop 4-19 cm
/// short of the columns, so the room never closed.
/// </summary>
public sealed class CreateRoomSeparatorsCommand : IRevitCommand
{
    public string Name => "create_room_separators";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);
        var level = CreateWallCommand.ResolveLevel(doc, P.StrOrNull(p, "levelName"));
        ViewPlan view;
        if (p["viewId"] is not null)
            view = doc.GetElement(new ElementId(P.Long(p, "viewId"))) as ViewPlan
                ?? throw new RevitCommandException("invalid_parameter", "viewId must identify a plan view.");
        else
            view = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel?.Id == level.Id)
                ?? throw new RevitCommandException("not_found", $"No floor plan for level '{level.Name}'; pass viewId.");
        if (p["lines"] is not JsonArray lines || lines.Count == 0)
            throw new RevitCommandException("invalid_parameter", "lines: [{start, end}] required.");

        var plane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation)));
        var curves = new CurveArray();
        foreach (var node in lines.OfType<JsonObject>())
        {
            var s = P.Xyz(node, "start", units); var e = P.Xyz(node, "end", units);
            var a = new XYZ(s.X, s.Y, level.Elevation); var b = new XYZ(e.X, e.Y, level.Elevation);
            if (a.DistanceTo(b) < 0.01 * P.MetersToFeet) throw new RevitCommandException("invalid_parameter", "Separator shorter than 1 cm.");
            curves.Append(Line.CreateBound(a, b));
        }
        var created = doc.Create.NewRoomBoundaryLines(plane, curves, view);
        var outIds = new JsonArray();
        foreach (ModelCurve mc in created) outIds.Add(mc.Id.Value);
        return new JsonObject { ["levelName"] = level.Name, ["viewId"] = view.Id.Value, ["count"] = outIds.Count, ["ids"] = outIds };
    }
}
