using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Real extents from tessellated solid edges. Element bounding boxes of roof edge sweeps and
/// line-based profile families were inflated live (a 30 x 20 cm gutter reported ~0.9 x 1.1 m;
/// a 2 cm tabica ~22 cm), so placement checks must not rely on them.
/// </summary>
public static class SolidExtents
{
    public static List<XYZ> Points(Element element)
    {
        var points = new List<XYZ>();
        void Visit(GeometryElement? g)
        {
            if (g is null) return;
            foreach (GeometryObject o in g)
            {
                if (o is GeometryInstance i) { Visit(i.GetInstanceGeometry()); continue; }
                if (o is Solid s && s.Volume > 0) foreach (Edge e in s.Edges) points.AddRange(e.Tessellate());
            }
        }
        Visit(element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));
        return points;
    }

    public static JsonObject? Json(IReadOnlyList<XYZ> points)
    {
        if (points.Count == 0) return null;
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var p in points)
        { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z); }
        double m = P.MetersToFeet;
        return new JsonObject
        {
            ["min"] = new JsonObject { ["x"] = x0 / m, ["y"] = y0 / m, ["z"] = z0 / m },
            ["max"] = new JsonObject { ["x"] = x1 / m, ["y"] = y1 / m, ["z"] = z1 / m },
        };
    }

    /// <summary>Largest distance from any solid point to the infinite line through a and b (feet).</summary>
    public static double MaxDistanceToLine(IReadOnlyList<XYZ> points, XYZ a, XYZ b)
    {
        var d = (b - a).Normalize(); double max = 0;
        foreach (var p in points) { var v = p - a; max = Math.Max(max, (v - d * v.DotProduct(d)).GetLength()); }
        return max;
    }

    /// <summary>
    /// Side of a horizontal-ish line where the solid mass lies, seen from above: "right" when the
    /// centroid is on direction x Z, "left" otherwise. Used to confirm trims face the intended side.
    /// </summary>
    public static string SideOfLine(IReadOnlyList<XYZ> points, XYZ a, XYZ b)
    {
        if (points.Count == 0) return "unknown";
        var c = XYZ.Zero; foreach (var p in points) c += p; c /= points.Count;
        var dir = new XYZ(b.X - a.X, b.Y - a.Y, 0); if (dir.GetLength() < 1e-9) return "unknown";
        var right = dir.Normalize().CrossProduct(XYZ.BasisZ);
        var s = new XYZ(c.X - a.X, c.Y - a.Y, 0).DotProduct(right);
        return Math.Abs(s) < 1e-6 ? "on_line" : s > 0 ? "right" : "left";
    }
}
