using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using static RevitMCPAddin.Commands.WallRoofProfileMath;
using Segment = RevitMCPAddin.Commands.WallRoofProfileMath.Segment;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Read-only analysis of a straight wall under a roof: underside elevation along both
/// wall faces (full thickness), gaps to a reference top and, optionally, the convex
/// profile ready for create_profile_wall. Planar roof faces only.
/// </summary>
public sealed class AnalyzeWallRoofInterfaceCommand : IRevitCommand
{
    public string Name => "analyze_wall_roof_interface";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var scale = P.Units(p) == "feet" ? 1.0 : P.MetersToFeet;
        var wall = doc.GetElement(new ElementId(P.Long(p, "wallId"))) as Wall
            ?? throw new RevitCommandException("invalid_parameter", "wallId must identify a wall.");
        var roof = doc.GetElement(new ElementId(P.Long(p, "roofId"))) as RoofBase
            ?? throw new RevitCommandException("invalid_parameter", "roofId must identify a roof.");
        if ((wall.Location as LocationCurve)?.Curve is not Line line)
            throw new RevitCommandException("unsupported_wall", "Only straight walls are supported.");
        var clearance = P.DblOr(p, "clearance", 0) * scale;
        if (!double.IsFinite(clearance) || clearance < 0)
            throw new RevitCommandException("invalid_parameter", "clearance must be finite and >= 0.");

        var start = line.GetEndPoint(0);
        var along = new XYZ(line.Direction.X, line.Direction.Y, 0);
        if (along.GetLength() < 1e-9) throw new RevitCommandException("unsupported_wall", "Wall location is vertical.");
        along = along.Normalize();
        var normal = wall.Orientation;
        normal = new XYZ(normal.X, normal.Y, 0).Normalize();

        // Wall extents from the regenerated solid: joins and attachments change them.
        double sMin = double.MaxValue, sMax = double.MinValue, zMin = double.MaxValue, zMax = double.MinValue;
        double offMin = double.MaxValue, offMax = double.MinValue;
        foreach (var solid in Solids(wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })))
        {
            foreach (Edge edge in solid.Edges)
                foreach (var v in edge.Tessellate())
                {
                    var s = (v - start).DotProduct(along);
                    sMin = Math.Min(sMin, s); sMax = Math.Max(sMax, s);
                    zMin = Math.Min(zMin, v.Z); zMax = Math.Max(zMax, v.Z);
                }
            foreach (Face face in solid.Faces)
                if (face is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) < 1e-6 && Math.Abs(Math.Abs(pf.FaceNormal.DotProduct(normal)) - 1) < 1e-6)
                {
                    var off = (pf.Origin - start).DotProduct(normal);
                    offMin = Math.Min(offMin, off); offMax = Math.Max(offMax, off);
                }
        }
        if (sMin >= sMax || offMin >= offMax)
            throw new RevitCommandException("unsupported_wall", "Could not read wall solid faces.");

        var faces = new List<UndersideFace>();
        int nonPlanar = 0;
        foreach (var solid in Solids(roof.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })))
            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace pf) { nonPlanar++; continue; }
                var n = pf.FaceNormal;
                if (n.Z > -1e-6) continue;
                var loops = new List<IReadOnlyList<Pt2>>();
                foreach (var loop in pf.GetEdgesAsCurveLoops())
                {
                    var pts = new List<Pt2>();
                    foreach (var curve in loop)
                    {
                        var tess = curve.Tessellate();
                        for (int i = 0; i < tess.Count - 1; i++) pts.Add(new Pt2(tess[i].X, tess[i].Y));
                    }
                    if (pts.Count >= 3) loops.Add(pts);
                }
                faces.Add(new UndersideFace(pf.Origin.X, pf.Origin.Y, pf.Origin.Z, n.X, n.Y, n.Z, loops));
            }
        if (faces.Count == 0)
            throw new RevitCommandException("unsupported_roof", "Roof has no planar downward faces.");

        var baseZ = p["baseElevation"] is null ? zMax : P.Dbl(p, "baseElevation") * scale;
        Pt2 LineOrigin(double off) { var o = start + normal * off; return new Pt2(o.X, o.Y); }
        var dir = new Pt2(along.X, along.Y);
        var interior = Underside(faces, LineOrigin(offMin), dir, sMin, sMax, zMin);
        var exterior = Underside(faces, LineOrigin(offMax), dir, sMin, sMax, zMin);
        var combined = WallRoofProfileMath.Min(interior, exterior);

        JsonObject Side(string name, IReadOnlyList<Segment> segments, double off)
        {
            var covered = segments.Where(g => g.Covered).ToList();
            var zs = covered.SelectMany(g => new[] { g.Z(g.S0), g.Z(g.S1) }).ToList();
            return new JsonObject
            {
                ["side"] = name,
                ["offsetFromLocationLine"] = off / scale,
                // Wall.Orientation may point into the building (seen live on the east wall),
                // so give the face position explicitly.
                ["faceLineOrigin"] = new JsonObject { ["x"] = LineOrigin(off).X / scale, ["y"] = LineOrigin(off).Y / scale },
                ["uncoveredLength"] = segments.Where(g => !g.Covered).Sum(g => g.S1 - g.S0) / scale,
                ["undersideMin"] = zs.Count == 0 ? null : zs.Min() / scale,
                ["undersideMax"] = zs.Count == 0 ? null : zs.Max() / scale,
                ["gapToBaseMin"] = zs.Count == 0 ? null : (zs.Min() - baseZ) / scale,
                ["gapToBaseMax"] = zs.Count == 0 ? null : (zs.Max() - baseZ) / scale,
                ["segments"] = new JsonArray(segments.Select(g => (JsonNode)new JsonObject
                {
                    ["s0"] = g.S0 / scale, ["s1"] = g.S1 / scale, ["covered"] = g.Covered,
                    ["z0"] = g.Covered ? g.Z(g.S0) / scale : null, ["z1"] = g.Covered ? g.Z(g.S1) / scale : null,
                }).ToArray()),
            };
        }

        // Largest vertical difference between the two faces at shared breakpoints.
        double acrossMax = 0;
        foreach (var s in combined.SelectMany(g => new[] { g.S0, g.S1 }))
        {
            var a = At(interior, s); var b = At(exterior, s);
            if (a.Covered && b.Covered) acrossMax = Math.Max(acrossMax, Math.Abs(a.Z(s) - b.Z(s)));
        }

        var centre = start + normal * ((offMin + offMax) / 2);
        var profile = Profile(combined, baseZ, clearance);
        JsonNode? profileJson = profile.Problem is null
            ? new JsonArray(profile.Points.Select(v =>
            {
                var xyz = centre + along * v.S;
                return (JsonNode)new JsonObject { ["x"] = xyz.X / scale, ["y"] = xyz.Y / scale, ["z"] = v.Z / scale };
            }).ToArray())
            : null;

        return new JsonObject
        {
            ["wallId"] = wall.Id.Value, ["wallUniqueId"] = wall.UniqueId,
            ["roofId"] = roof.Id.Value, ["roofUniqueId"] = roof.UniqueId,
            ["units"] = scale == 1.0 ? "feet" : "meters",
            ["wallWidth"] = (offMax - offMin) / scale,
            ["wallSolidElevation"] = new JsonObject { ["min"] = zMin / scale, ["max"] = zMax / scale },
            ["wallTopAttachedToRoof"] = wall.GetAttachmentIds(AttachmentLocation.Top).Contains(roof.Id),
            ["baseElevation"] = baseZ / scale, ["clearance"] = clearance / scale,
            ["direction"] = new JsonObject { ["x"] = along.X, ["y"] = along.Y },
            ["spanAlongLocationLine"] = new JsonObject { ["start"] = sMin / scale, ["end"] = sMax / scale },
            ["faces"] = new JsonArray(Side("interior", interior, offMin), Side("exterior", exterior, offMax)),
            ["undersideDifferenceAcrossThicknessMax"] = acrossMax / scale,
            ["roofPlanarUndersideFaces"] = faces.Count, ["roofNonPlanarFacesIgnored"] = nonPlanar,
            ["proposedProfile"] = profileJson,
            ["proposedProfileConvex"] = profile.Problem is null ? profile.Convex : null,
            ["proposedProfileProblem"] = profile.Problem,
            ["note"] = "Profile = region between baseElevation and the lower roof underside of both wall faces minus clearance, "
                + "over the wall solid span, on the wall centre plane. Interior/exterior follow Wall.Orientation, not rooms. "
                + "baseElevation defaults to the wall solid maximum (meaningless once the wall is attached). "
                + "Does not design bearings, plates, purlins or ring beams; confirm with sections after any change.",
        };
    }

    static IEnumerable<Solid> Solids(GeometryElement? geometry)
    {
        if (geometry is null) yield break;
        foreach (GeometryObject o in geometry)
        {
            if (o is GeometryInstance gi) { foreach (var s in Solids(gi.GetInstanceGeometry())) yield return s; }
            else if (o is Solid s && s.Volume > 0) yield return s;
        }
    }
}
