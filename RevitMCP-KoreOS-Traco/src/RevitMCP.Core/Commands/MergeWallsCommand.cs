using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Replace nearly collinear wall segments by one straight wall: the longest segment's type, level,
/// base offset, height and centerline; extents = union of the segments, optionally extended to the
/// nearest crossing wall centerline at each end. Hosted doors/windows are recreated on the new wall at
/// the same point with the same type and flip state. User rule (11/09/2026): "delete the two segments
/// and extend the remaining one to the perpendicular wall" instead of nudging pieces into line; moving a
/// joined wall drags its collinear neighbours, so alignment by translation never removed the steps.
/// </summary>
public sealed class MergeWallsCommand : IRevitCommand
{
    public string Name => "merge_walls";
    public bool IsReadOnly => false;
    public string RiskLevel => "high";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        if (p["wallIds"] is not JsonArray raw || raw.Count < 2)
            throw new RevitCommandException("invalid_parameter", "wallIds: two or more walls.");
        var walls = raw.Select(n => doc.GetElement(new ElementId(n!.GetValue<long>())) as Wall
            ?? throw new RevitCommandException("invalid_parameter", $"{n} is not a wall.")).ToList();
        if (walls.Any(w => w.Location is not LocationCurve { Curve: Line }))
            throw new RevitCommandException("unsupported_type", "Only straight walls can be merged.");
        var maxOffset = P.DblOr(p, "maxOffsetMeters", 0.1) * P.MetersToFeet;
        var extend = P.BoolOr(p, "extendToWalls", true);
        var reach = P.DblOr(p, "extendReachMeters", 1.0) * P.MetersToFeet;

        Line L(Wall w) => (Line)((LocationCurve)w.Location).Curve;
        var lead = walls.OrderByDescending(w => L(w).Length).First();
        var leadLine = L(lead);
        var u = leadLine.Direction.Normalize();
        var origin = leadLine.GetEndPoint(0);
        var n = new XYZ(-u.Y, u.X, 0);
        foreach (var w in walls)
        {
            var d = L(w).Direction.Normalize();
            if (Math.Abs(u.X * d.Y - u.Y * d.X) > Math.Sin(Math.PI / 180))
                throw new RevitCommandException("invalid_parameter", $"Wall {w.Id.Value} is not parallel to {lead.Id.Value}.");
            foreach (var q in new[] { L(w).GetEndPoint(0), L(w).GetEndPoint(1) })
                if (Math.Abs((q - origin).DotProduct(n)) > maxOffset)
                    throw new RevitCommandException("invalid_parameter", $"Wall {w.Id.Value} is more than maxOffsetMeters off the line of {lead.Id.Value}.");
            if (w.WallType.Id != lead.WallType.Id)
                throw new RevitCommandException("invalid_parameter", $"Wall {w.Id.Value} has another type; merge walls of one type.");
        }
        double s0 = walls.Min(w => Math.Min((L(w).GetEndPoint(0) - origin).DotProduct(u), (L(w).GetEndPoint(1) - origin).DotProduct(u)));
        double s1 = walls.Max(w => Math.Max((L(w).GetEndPoint(0) - origin).DotProduct(u), (L(w).GetEndPoint(1) - origin).DotProduct(u)));

        var ids = walls.Select(w => w.Id).ToHashSet();
        var extended = new JsonArray();
        if (extend)
        {
            var others = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
                .Where(o => !ids.Contains(o.Id) && o.Location is LocationCurve { Curve: Line } && o.LevelId == lead.LevelId).ToList();
            foreach (var end in new[] { 0, 1 })
            {
                var s = end == 0 ? s0 : s1;
                var dir = end == 0 ? -1 : 1;
                double? best = null; long bestId = 0;
                foreach (var o in others)
                {
                    var ol = L(o); var v = ol.Direction.Normalize();
                    var cross = u.X * v.Y - u.Y * v.X;
                    if (Math.Abs(cross) < Math.Sin(5 * Math.PI / 180)) continue;
                    var a = ol.GetEndPoint(0);
                    // origin + t u on o's line
                    var t = ((a.X - origin.X) * v.Y - (a.Y - origin.Y) * v.X) / cross;
                    var move = (t - s) * dir;
                    if (move < -o.Width || move > reach) continue;
                    var hit = origin + u * t;
                    var along = (hit - a).DotProduct(v);
                    if (along < -lead.Width || along > ol.Length + lead.Width) continue;
                    if (best is null || Math.Abs(t - s) < Math.Abs(best.Value - s)) { best = t; bestId = o.Id.Value; }
                }
                if (best is not null)
                {
                    extended.Add(new JsonObject { ["end"] = end, ["toWallId"] = bestId, ["moveMeters"] = Math.Round((best.Value - s) * dir / P.MetersToFeet, 4) });
                    if (end == 0) s0 = best.Value; else s1 = best.Value;
                }
            }
        }

        // remember hosted inserts before deleting the segments
        var inserts = walls.SelectMany(w => w.FindInserts(true, false, false, false)).Distinct()
            .Select(id => doc.GetElement(id) as FamilyInstance).Where(fi => fi?.Location is LocationPoint)
            .Select(fi => (symbol: fi!.Symbol, at: ((LocationPoint)fi.Location).Point, facing: fi.FacingFlipped, hand: fi.HandFlipped,
                level: doc.GetElement(fi.LevelId) as Level ?? doc.GetElement(lead.LevelId) as Level, sill: fi.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.AsDouble()))
            .ToList();

        // everything read from the lead must be captured before its deletion (live: InvalidObjectException)
        var level = (Level)doc.GetElement(lead.LevelId);
        var typeId = lead.WallType.Id; var typeName = lead.WallType.Name; var leadId = lead.Id.Value; var leadWidth = lead.Width;
        var baseOffset = lead.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0;
        var height = lead.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 3.0 * P.MetersToFeet;
        var structural = lead.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() == 1;
        var z = leadLine.GetEndPoint(0).Z;
        var start = new XYZ(origin.X + u.X * s0, origin.Y + u.Y * s0, z);
        var end1 = new XYZ(origin.X + u.X * s1, origin.Y + u.Y * s1, z);
        if (start.DistanceTo(end1) < 0.1 * P.MetersToFeet) throw new RevitCommandException("invalid_parameter", "Merged wall would be shorter than 10 cm.");

        doc.Delete(ids.ToList());
        var merged = Wall.Create(doc, Line.CreateBound(start, end1), typeId, level.Id, height, baseOffset, false, structural);
        merged.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM)?.Set((int)WallLocationLine.WallCenterline);
        doc.Regenerate();
        var lc = (LocationCurve)merged.Location;
        var drift = start - lc.Curve.GetEndPoint(0);
        if (drift.GetLength() > 1e-6) { ElementTransformUtils.MoveElement(doc, merged.Id, new XYZ(drift.X, drift.Y, 0)); doc.Regenerate(); }

        var rehosted = new JsonArray();
        foreach (var ins in inserts)
        {
            if (!ins.symbol.IsActive) ins.symbol.Activate();
            var fi = doc.Create.NewFamilyInstance(ins.at, ins.symbol, merged, ins.level, StructuralType.NonStructural);
            if (ins.sill is double sill) fi.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.Set(sill);
            doc.Regenerate();
            if (fi.FacingFlipped != ins.facing && fi.CanFlipFacing) fi.flipFacing();
            if (fi.HandFlipped != ins.hand && fi.CanFlipHand) fi.flipHand();
            rehosted.Add(new JsonObject { ["id"] = fi.Id.Value, ["type"] = ins.symbol.Name, ["family"] = ins.symbol.FamilyName, ["facingFlipped"] = fi.FacingFlipped, ["handFlipped"] = fi.HandFlipped });
        }
        foreach (var e in new[] { 0, 1 }) WallUtils.AllowWallJoinAtEnd(merged, e);
        doc.Regenerate();
        var final = (Line)((LocationCurve)merged.Location).Curve;
        return new JsonObject
        {
            ["wallId"] = merged.Id.Value, ["replaced"] = new JsonArray(ids.Select(i => (JsonNode)i.Value).ToArray()),
            ["type"] = typeName, ["leadWallId"] = leadId,
            ["start"] = new JsonArray(Math.Round(final.GetEndPoint(0).X / P.MetersToFeet, 4), Math.Round(final.GetEndPoint(0).Y / P.MetersToFeet, 4)),
            ["end"] = new JsonArray(Math.Round(final.GetEndPoint(1).X / P.MetersToFeet, 4), Math.Round(final.GetEndPoint(1).Y / P.MetersToFeet, 4)),
            ["lengthMeters"] = Math.Round(final.Length / P.MetersToFeet, 4), ["extended"] = extended,
            ["insertsRecreated"] = rehosted, ["insertsBefore"] = inserts.Count,
            ["note"] = "Rooms, tags and dimensions attached to the deleted segments are not transferred; check rooms after merging.",
        };
    }
}
