using System;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Join the geometry of one cutter element with several others (JoinGeometryUtils) and make
/// sure the cutter is the one that cuts. Used so the roof covering trims the chamfer of square
/// boards rising into the sloped boarding (eave blocking, recessed fascia). Reports solid
/// volume before/after so the trim is verifiable. Idempotent for already joined pairs.
/// </summary>
public sealed class JoinGeometryCommand : IRevitCommand
{
    public string Name => "join_geometry";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var cutter = doc.GetElement(new ElementId(P.Long(p, "cutterId")))
            ?? throw new RevitCommandException("not_found", "cutterId not found.");
        if (p["elementIds"] is not JsonArray raw || raw.Count == 0 || raw.Count > 500)
            throw new RevitCommandException("invalid_parameter", "elementIds: 1..500 ids to be cut.");
        var requireCut = P.BoolOr(p, "requireVolumeChange", true);
        var results = new JsonArray();
        foreach (var node in raw)
        {
            var id = new ElementId(node!.GetValue<long>());
            var other = doc.GetElement(id) ?? throw new RevitCommandException("not_found", $"Element {id.Value} not found.");
            double before = Volume(other);
            bool wasJoined = JoinGeometryUtils.AreElementsJoined(doc, cutter, other);
            bool intersects = SolidsIntersect(cutter, other);
            if (!wasJoined)
            {
                if (!intersects && requireCut)
                    throw new RevitCommandException("no_intersection", $"Element {id.Value} does not intersect the cutter; nothing to trim.");
                try { JoinGeometryUtils.JoinGeometry(doc, cutter, other); }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                { throw new RevitCommandException("unsupported_join", $"Revit cannot join {cutter.Id.Value} and {id.Value}: {ex.Message}"); }
            }
            if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, cutter, other))
                JoinGeometryUtils.SwitchJoinOrder(doc, cutter, other);
            doc.Regenerate();
            double after = Volume(other);
            if (!JoinGeometryUtils.AreElementsJoined(doc, cutter, other) || !JoinGeometryUtils.IsCuttingElementInJoin(doc, cutter, other))
                throw new RevitCommandException("verification_failed", $"Join with {id.Value} was not retained with the cutter cutting.");
            if (requireCut && intersects && !(after < before - 1e-9))
                throw new RevitCommandException("verification_failed", $"Element {id.Value} volume did not decrease after the join.");
            double ft3 = Math.Pow(P.MetersToFeet, 3);
            results.Add(new JsonObject
            {
                ["id"] = id.Value, ["alreadyJoined"] = wasJoined, ["intersectedBefore"] = intersects,
                ["volumeBeforeCubicMeters"] = before / ft3, ["volumeAfterCubicMeters"] = after / ft3,
            });
        }
        return new JsonObject { ["cutterId"] = cutter.Id.Value, ["results"] = results,
            ["note"] = "Geometry join only; no structural or material relationship. Unjoin by deleting/recreating or in the Revit UI." };
    }

    static double Volume(Element e)
    {
        double v = 0;
        foreach (var o in e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }) ?? Enumerable.Empty<GeometryObject>())
        {
            if (o is Solid s) v += s.Volume;
            else if (o is GeometryInstance gi) foreach (var io in gi.GetInstanceGeometry()) if (io is Solid si) v += si.Volume;
        }
        return v;
    }

    static bool SolidsIntersect(Element a, Element b)
    {
        var sa = Solids(a).ToList();
        foreach (var s1 in Solids(b))
            foreach (var s0 in sa)
            {
                try
                {
                    var i = BooleanOperationsUtils.ExecuteBooleanOperation(s0, s1, BooleanOperationsType.Intersect);
                    if (i is not null && i.Volume > 1e-9) return true;
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException) { return true; } // conservative
            }
        return false;
    }

    static System.Collections.Generic.IEnumerable<Solid> Solids(Element e)
    {
        foreach (var o in e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }) ?? Enumerable.Empty<GeometryObject>())
        {
            if (o is Solid s && s.Volume > 0) yield return s;
            else if (o is GeometryInstance gi) foreach (var io in gi.GetInstanceGeometry()) if (io is Solid si && si.Volume > 0) yield return si;
        }
    }
}
