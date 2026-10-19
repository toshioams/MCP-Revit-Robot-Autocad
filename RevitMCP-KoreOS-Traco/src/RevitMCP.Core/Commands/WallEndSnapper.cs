using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Pure plan math for closing wall ends that stop short of (or overshoot) the wall they should meet.
/// Walls traced from a poor DWG left 5-50 cm gaps at corners and T junctions (11/09/2026, Ford pilot),
/// so rooms leaked and Revit could not join them. For every end, candidates are:
///   L/T: the intersection with a non-parallel wall's centerline, if it lies within tolerance along the
///        wall direction and inside the other wall (extended by half this wall's thickness + tolerance);
///   collinear: the nearest end of a wall on the same line, if the gap is within tolerance.
/// The smallest move wins. Ends already on their target are left alone. Meters, plan coordinates.
/// </summary>
public static class WallEndSnapper
{
    public sealed record Wall(long Id, double Ax, double Ay, double Bx, double By, double Thickness);
    public sealed record Move(long Id, int End, double FromX, double FromY, double ToX, double ToY, long TargetId, string Kind, double Distance);

    static double Len(double x, double y) => Math.Sqrt(x * x + y * y);

    static bool Connected(double x, double y, Wall o)
    {
        const double eps = 0.005;
        if (Len(x - o.Ax, y - o.Ay) <= eps || Len(x - o.Bx, y - o.By) <= eps) return true;
        var ol = Len(o.Bx - o.Ax, o.By - o.Ay);
        if (ol < 1e-6) return false;
        double vx = (o.Bx - o.Ax) / ol, vy = (o.By - o.Ay) / ol;
        var s = (x - o.Ax) * vx + (y - o.Ay) * vy;
        var off = Math.Abs((x - o.Ax) * -vy + (y - o.Ay) * vx);
        return s >= -eps && s <= ol + eps && off <= o.Thickness / 2 + eps;
    }

    public static List<Move> Plan(IReadOnlyList<Wall> walls, double tolerance = 0.6, double minAngleDeg = 5)
    {
        var sinMin = Math.Sin(minAngleDeg * Math.PI / 180);
        var moves = new List<Move>();
        foreach (var w in walls)
        {
            var len = Len(w.Bx - w.Ax, w.By - w.Ay);
            if (len < 1e-6) continue;
            double ux = (w.Bx - w.Ax) / len, uy = (w.By - w.Ay) / len;
            for (int end = 0; end < 2; end++)
            {
                double ex = end == 0 ? w.Ax : w.Bx, ey = end == 0 ? w.Ay : w.By;
                double ox = end == 0 ? -ux : ux, oy = end == 0 ? -uy : uy; // outward direction
                // Already connected: the end touches another wall's end, or sits inside another wall's band
                // within its extent. Live 11/09: two joined collinear pieces of a facade were pulled 57 cm
                // onto a cubicle divider because a joined end was not recognised as satisfied.
                if (walls.Any(o => o.Id != w.Id && Connected(ex, ey, o))) continue;
                Move? best = null;
                foreach (var o in walls)
                {
                    if (o.Id == w.Id) continue;
                    var ol = Len(o.Bx - o.Ax, o.By - o.Ay);
                    if (ol < 1e-6) continue;
                    double vx = (o.Bx - o.Ax) / ol, vy = (o.By - o.Ay) / ol;
                    var cross = ux * vy - uy * vx;
                    Move? cand = null;
                    if (Math.Abs(cross) >= sinMin)
                    {
                        // e + t*o_dir hits o's line
                        var t = ((o.Ax - ex) * vy - (o.Ay - ey) * vx) / (ox * vy - oy * vx);
                        if (Math.Abs(t) > tolerance + o.Thickness / 2) continue;
                        if (t < 0 && -t > len - 0.1) continue; // trimming would destroy the wall
                        double xx = ex + ox * t, xy = ey + oy * t;
                        var s = (xx - o.Ax) * vx + (xy - o.Ay) * vy;
                        var reach = w.Thickness / 2 + tolerance;
                        if (s < -reach || s > ol + reach) continue;
                        var kind = s > o.Thickness && s < ol - o.Thickness ? "T" : "L";
                        cand = new Move(w.Id, end, ex, ey, xx, xy, o.Id, kind, Math.Abs(t));
                    }
                    else
                    {
                        // same line: perpendicular offset small, nearest end of o in front of this end
                        var off = Math.Abs((o.Ax - ex) * -uy + (o.Ay - ey) * ux);
                        if (off > 0.05) continue;
                        foreach (var (px, py) in new[] { (o.Ax, o.Ay), (o.Bx, o.By) })
                        {
                            var gap = (px - ex) * ox + (py - ey) * oy;
                            if (gap <= 1e-4 || gap > tolerance) continue;
                            var c = new Move(w.Id, end, ex, ey, px, py, o.Id, "collinear", gap);
                            if (cand is null || c.Distance < cand.Distance) cand = c;
                        }
                    }
                    // ties (a joint between two collinear pieces): prefer the wall the end actually lands on (T)
                    if (cand is not null && (best is null || cand.Distance < best.Distance - 1e-6
                        || (Math.Abs(cand.Distance - best.Distance) <= 1e-6 && cand.Kind == "T" && best.Kind != "T"))) best = cand;
                }
                if (best is not null && best.Distance > 1e-3) moves.Add(best);
            }
        }
        return moves;
    }
}
