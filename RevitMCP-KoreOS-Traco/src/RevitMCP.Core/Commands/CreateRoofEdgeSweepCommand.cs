using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Place roof edge sweeps (Fascia: ridge caps, flashings, fascia boards; Gutter) on
/// classified edges of a roof: ridge (shared by two top or two bottom faces), eave
/// (horizontal, not shared) and verge (sloped). One element per edge. The showroom file
/// has "Cumeeira metálica", "Rufo metálico" (Fascia) and "Calha" (Gutter) types to copy.
/// </summary>
public sealed class CreateRoofEdgeSweepCommand : IRevitCommand
{
    public string Name => "create_roof_edge_sweep";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var roof = doc.GetElement(new ElementId(P.Long(p, "roofId"))) as RoofBase
            ?? throw new RevitCommandException("invalid_parameter", "roofId must identify a roof.");
        var type = doc.GetElement(new ElementId(P.Long(p, "typeId")));
        if (type is not FasciaType && type is not GutterType)
            throw new RevitCommandException("invalid_parameter", "typeId must be a Fascia (edge sweep) or Gutter type.");
        var kinds = (p["edgeKinds"] as JsonArray)?.Select(n => n!.GetValue<string>().ToLowerInvariant()).ToHashSet()
            ?? throw new RevitCommandException("invalid_parameter", "edgeKinds: any of ridge, eave, verge.");
        if (kinds.Count == 0 || kinds.Any(k => k is not ("ridge" or "eave" or "verge")))
            throw new RevitCommandException("invalid_parameter", "edgeKinds: any of ridge, eave, verge.");
        var side = (P.StrOrNull(p, "faceSide") ?? "top").ToLowerInvariant();
        if (side is not ("top" or "bottom")) throw new RevitCommandException("invalid_parameter", "faceSide: top|bottom.");

        var edges = ClassifyEdges(roof, side == "top").Where(e => kinds.Contains(e.Kind)).ToList();
        if (edges.Count == 0) throw new RevitCommandException("not_found", "No roof edges of the requested kinds.");

        var created = new JsonArray();
        foreach (var e in edges)
        {
            HostedSweep sweep = type is FasciaType ft ? doc.Create.NewFascia(ft, e.Reference) : doc.Create.NewGutter((GutterType)type, e.Reference);
            // Profile offsets relative to the host edge, e.g. a gutter hung on a recessed fascia.
            if (p["horizontalOffset"] is not null) sweep.HorizontalOffset = P.Dbl(p, "horizontalOffset") * P.MetersToFeet;
            if (p["verticalOffset"] is not null) sweep.VerticalOffset = P.Dbl(p, "verticalOffset") * P.MetersToFeet;
            if (P.BoolOr(p, "flipHorizontal", false)) sweep.HorizontalFlip();
            if (P.BoolOr(p, "flipVertical", false)) sweep.VerticalFlip();
            doc.Regenerate();
            // Element bounding boxes of sweeps are inflated (seen live); check the solid instead.
            var points = SolidExtents.Points(sweep);
            if (points.Count == 0)
                throw new RevitCommandException("verification_failed", $"Sweep on {e.Kind} edge has no solid geometry.");
            var reach = SolidExtents.MaxDistanceToLine(points, e.Start, e.End);
            var maxReach = P.DblOr(p, "maxProfileReachMeters", 1.0) * P.MetersToFeet;
            if (reach > maxReach)
                throw new RevitCommandException("verification_failed", $"Sweep on {e.Kind} edge extends {reach / P.MetersToFeet:0.###} m from it (limit {maxReach / P.MetersToFeet:0.###} m).");
            created.Add(new JsonObject
            {
                ["id"] = sweep.Id.Value, ["uniqueId"] = sweep.UniqueId, ["edgeKind"] = e.Kind,
                ["edgeStart"] = Pt(e.Start), ["edgeEnd"] = Pt(e.End), ["edgeLengthMeters"] = e.Start.DistanceTo(e.End) / P.MetersToFeet,
                ["profileReachMeters"] = reach / P.MetersToFeet, ["solidExtents"] = SolidExtents.Json(points),
                ["horizontalOffsetMeters"] = sweep.HorizontalOffset / P.MetersToFeet, ["verticalOffsetMeters"] = sweep.VerticalOffset / P.MetersToFeet,
                ["horizontalFlipped"] = sweep.HorizontalFlipped, ["verticalFlipped"] = sweep.VerticalFlipped,
                ["slopeCheck"] = SlopeCheck(sweep, e),
            });
        }
        return new JsonObject
        {
            ["roofId"] = roof.Id.Value, ["typeId"] = type.Id.Value, ["typeName"] = type.Name,
            ["category"] = type is FasciaType ? "Fascia" : "Gutter", ["faceSide"] = side,
            ["created"] = created,
            ["note"] = "One sweep per edge. Profile orientation/offset come from the type; inspect sections. Edge classification assumes planar roof faces.",
        };
    }

    public sealed record ClassifiedEdge(string Kind, Reference Reference, XYZ Start, XYZ End, double RoofPitchDegrees);

    internal static List<ClassifiedEdge> ClassifyEdges(RoofBase roof, bool top)
    {
        var result = new List<ClassifiedEdge>();
        var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        bool Wanted(Face? f) => f is PlanarFace pf && (top ? pf.FaceNormal.Z > 0.05 : pf.FaceNormal.Z < -0.05);
        var seen = new HashSet<string>();
        foreach (var obj in roof.get_Geometry(options) ?? throw new RevitCommandException("unsupported_roof", "Roof has no geometry."))
        {
            if (obj is not Solid solid || solid.Volume <= 0) continue;
            foreach (Face face in solid.Faces)
            {
                if (!Wanted(face)) continue;
                foreach (EdgeArray loop in face.EdgeLoops)
                    foreach (Edge edge in loop)
                    {
                        if (edge.Reference is null || edge.AsCurve() is not Line line) continue;
                        var key = edge.Reference.ConvertToStableRepresentation(roof.Document);
                        if (!seen.Add(key)) continue;
                        var shared = Wanted(edge.GetFace(0)) && Wanted(edge.GetFace(1));
                        var kind = Math.Abs(line.Direction.Z) < 1e-6 ? (shared ? "ridge" : "eave") : (shared ? "hip_or_valley" : "verge");
                        var pitch = Math.Acos(Math.Min(1, Math.Abs(((PlanarFace)face).FaceNormal.Z))) * 180 / Math.PI;
                        result.Add(new ClassifiedEdge(kind, edge.Reference, line.GetEndPoint(0), line.GetEndPoint(1), pitch));
                    }
            }
        }
        return result;
    }

    /// <summary>
    /// Compares the dominant inclined faces of the sweep with the roof pitch. A ridge cap or
    /// flashing whose wings do not follow the roof (office metal cap: 20 deg on a 45 deg roof)
    /// is reported as a mismatch; the element is kept so the agent can decide.
    /// </summary>
    static JsonObject SlopeCheck(Element sweep, ClassifiedEdge edge)
    {
        var slopes = new System.Collections.Generic.Dictionary<int, double>();
        foreach (var obj in sweep.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }) ?? Enumerable.Empty<GeometryObject>())
            if (obj is Solid s && s.Volume > 0)
                foreach (Face f in s.Faces)
                    if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) > 0.05 && Math.Abs(pf.FaceNormal.Z) < 0.999)
                    {
                        var deg = (int)Math.Round(Math.Acos(Math.Abs(pf.FaceNormal.Z)) * 180 / Math.PI);
                        slopes[deg] = slopes.TryGetValue(deg, out var a) ? a + pf.Area : pf.Area;
                    }
        var dominant = slopes.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault(-1);
        var matches = dominant >= 0 && Math.Abs(dominant - edge.RoofPitchDegrees) <= 3;
        return new JsonObject
        {
            ["roofPitchDegrees"] = Math.Round(edge.RoofPitchDegrees, 2),
            ["dominantProfileSlopeDegrees"] = dominant < 0 ? null : dominant,
            ["matchesRoofPitch"] = dominant < 0 ? null : matches,
            ["warning"] = dominant >= 0 && !matches && edge.Kind == "ridge"
                ? "Profile wings do not follow the roof pitch; use a profile made for this pitch (create_profile_family ridgeCap)." : null,
        };
    }

    static JsonObject Pt(XYZ v) => new() { ["x"] = v.X / P.MetersToFeet, ["y"] = v.Y / P.MetersToFeet, ["z"] = v.Z / P.MetersToFeet };
}
