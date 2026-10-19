using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Revit-independent closed profile loops (u horizontal, v vertical, meters, origin on the
/// host edge) for roof edge sweeps. Seen live: the office "Cumeeira metálica" profile is fixed
/// at 20 degrees and floated ~17 cm above a 45 degree roof at its wing tips.
/// </summary>
public static class ProfileShapes
{
    public readonly record struct UV(double U, double V);

    /// <summary>
    /// Symmetric ridge cap shell: the underside follows both roof planes (pitch) from the ridge
    /// line down to each wing tip (wingLength measured along the slope); the top is offset by
    /// thickness with a mitred apex. Origin = ridge edge on the roof top surface.
    /// </summary>
    public static IReadOnlyList<UV> RidgeCap(double pitchDegrees, double wingLength, double thickness)
    {
        if (!(pitchDegrees > 0 && pitchDegrees < 80)) throw new ArgumentException("pitchDegrees must be within 0..80.");
        if (!(wingLength > 0) || !(thickness > 0) || thickness >= wingLength) throw new ArgumentException("wingLength and thickness must be positive, thickness < wingLength.");
        double a = pitchDegrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var tipR = new UV(wingLength * c, -wingLength * s);
        var tipL = new UV(-tipR.U, tipR.V);
        var tipRTop = new UV(tipR.U + thickness * s, tipR.V + thickness * c);
        var tipLTop = new UV(-tipRTop.U, tipRTop.V);
        var apexTop = new UV(0, thickness / c);
        return new[] { tipL, new UV(0, 0), tipR, tipRTop, apexTop, tipLTop };
    }

    /// <summary>Validates a closed loop: >= 3 finite distinct points, no zero edges, non-self-intersecting, nonzero area.</summary>
    public static void ValidateLoop(IReadOnlyList<UV> loop, double minEdge = 0.0008)
    {
        if (loop.Count < 3) throw new ArgumentException("A profile loop needs at least 3 points.");
        if (loop.Any(p => !double.IsFinite(p.U) || !double.IsFinite(p.V))) throw new ArgumentException("Profile coordinates must be finite.");
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i]; var b = loop[(i + 1) % loop.Count];
            if (Math.Sqrt((b.U - a.U) * (b.U - a.U) + (b.V - a.V) * (b.V - a.V)) < minEdge)
                throw new ArgumentException($"Edge {i} is shorter than Revit's short curve tolerance.");
        }
        for (int i = 0; i < loop.Count; i++)
            for (int j = i + 1; j < loop.Count; j++)
            {
                if (j == i + 1 || (i == 0 && j == loop.Count - 1)) continue;
                if (SegmentsCross(loop[i], loop[(i + 1) % loop.Count], loop[j], loop[(j + 1) % loop.Count]))
                    throw new ArgumentException($"Profile edges {i} and {j} intersect.");
            }
        if (Math.Abs(Area(loop)) < 1e-8) throw new ArgumentException("Profile loop has no area.");
    }

    public static double Area(IReadOnlyList<UV> loop)
    {
        double sum = 0;
        for (int i = 0; i < loop.Count; i++) { var a = loop[i]; var b = loop[(i + 1) % loop.Count]; sum += a.U * b.V - b.U * a.V; }
        return sum / 2;
    }

    static bool SegmentsCross(UV p1, UV p2, UV q1, UV q2)
    {
        static double Cross(UV o, UV a, UV b) => (a.U - o.U) * (b.V - o.V) - (a.V - o.V) * (b.U - o.U);
        double d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2), d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
        return ((d1 > 1e-12 && d2 < -1e-12) || (d1 < -1e-12 && d2 > 1e-12)) && ((d3 > 1e-12 && d4 < -1e-12) || (d3 < -1e-12 && d4 > 1e-12));
    }
}
