using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Revit-independent geometry for the wall/roof interface. Roof undersides are planar
/// faces projected to XY; a wall face is a horizontal line s -> origin + dir*s.
/// Piecewise-linear results are exact for planar faces because every breakpoint
/// (face boundary crossings and plane/plane intersections) is enumerated.
/// </summary>
public static class WallRoofProfileMath
{
    public readonly record struct Pt2(double X, double Y);

    public sealed record UndersideFace(double Ox, double Oy, double Oz, double Nx, double Ny, double Nz,
        IReadOnlyList<IReadOnlyList<Pt2>> Loops)
    {
        public double ZAt(double x, double y) => Oz - (Nx * (x - Ox) + Ny * (y - Oy)) / Nz;

        /// <summary>Even-odd test over all loops, so inner loops behave as holes.</summary>
        public bool Contains(double x, double y)
        {
            var inside = false;
            foreach (var loop in Loops)
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    var a = loop[i]; var b = loop[j];
                    if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
            return inside;
        }
    }

    /// <summary>Linear piece z = A + B*s on [S0,S1]; Face = -1 means no roof above.</summary>
    public readonly record struct Segment(double S0, double S1, double A, double B, int Face)
    {
        public double Z(double s) => A + B * s;
        public bool Covered => Face >= 0;
    }

    const double Eps = 1e-9;

    public static List<Segment> Underside(IReadOnlyList<UndersideFace> faces, Pt2 origin, Pt2 dir,
        double s0, double s1, double minZ = double.NegativeInfinity)
    {
        if (!(s1 > s0)) throw new ArgumentException("Empty span.");
        var coeffs = faces.Select(f =>
        {
            var a = f.ZAt(origin.X, origin.Y);
            return (A: a, B: f.ZAt(origin.X + dir.X, origin.Y + dir.Y) - a);
        }).ToArray();
        var cands = new List<double> { s0, s1 };
        foreach (var f in faces)
            foreach (var loop in f.Loops)
                for (int i = 0; i < loop.Count; i++)
                {
                    var a = loop[i]; var b = loop[(i + 1) % loop.Count];
                    double ex = b.X - a.X, ey = b.Y - a.Y, wx = a.X - origin.X, wy = a.Y - origin.Y;
                    var cross = dir.X * ey - dir.Y * ex;
                    if (Math.Abs(cross) < Eps)
                    {
                        // Edge parallel to the wall line: endpoints matter only when collinear.
                        if (Math.Abs(wx * dir.Y - wy * dir.X) < 1e-7)
                        {
                            cands.Add(wx * dir.X + wy * dir.Y);
                            cands.Add((b.X - origin.X) * dir.X + (b.Y - origin.Y) * dir.Y);
                        }
                        continue;
                    }
                    var s = (wx * ey - wy * ex) / cross;
                    var u = (wx * dir.Y - wy * dir.X) / cross;
                    if (u >= -1e-7 && u <= 1 + 1e-7) cands.Add(s);
                }
        for (int i = 0; i < coeffs.Length; i++)
            for (int j = i + 1; j < coeffs.Length; j++)
            {
                var db = coeffs[i].B - coeffs[j].B;
                if (Math.Abs(db) > Eps) cands.Add((coeffs[j].A - coeffs[i].A) / db);
            }
        var sorted = SortedUnique(cands.Where(s => s >= s0 && s <= s1));
        var result = new List<Segment>();
        for (int k = 0; k + 1 < sorted.Count; k++)
        {
            var mid = (sorted[k] + sorted[k + 1]) / 2;
            double mx = origin.X + dir.X * mid, my = origin.Y + dir.Y * mid;
            int best = -1; double bestZ = double.PositiveInfinity;
            for (int f = 0; f < faces.Count; f++)
            {
                if (!faces[f].Contains(mx, my)) continue;
                var z = coeffs[f].A + coeffs[f].B * mid;
                if (z >= minZ - 1e-7 && z < bestZ) { bestZ = z; best = f; }
            }
            var piece = best < 0 ? new Segment(sorted[k], sorted[k + 1], double.NaN, double.NaN, -1)
                : new Segment(sorted[k], sorted[k + 1], coeffs[best].A, coeffs[best].B, best);
            Append(result, piece);
        }
        return result;
    }

    /// <summary>Pointwise minimum of two underside functions; uncovered wins (conservative).</summary>
    public static List<Segment> Min(IReadOnlyList<Segment> first, IReadOnlyList<Segment> second)
    {
        var cands = SortedUnique(first.SelectMany(g => new[] { g.S0, g.S1 })
            .Concat(second.SelectMany(g => new[] { g.S0, g.S1 }))
            .Where(s => s >= Math.Max(first[0].S0, second[0].S0) && s <= Math.Min(first[^1].S1, second[^1].S1)));
        var result = new List<Segment>();
        for (int k = 0; k + 1 < cands.Count; k++)
        {
            double a = cands[k], b = cands[k + 1], mid = (a + b) / 2;
            var p = At(first, mid); var q = At(second, mid);
            if (!p.Covered || !q.Covered) { Append(result, new Segment(a, b, double.NaN, double.NaN, -1)); continue; }
            var db = p.B - q.B;
            var cross = Math.Abs(db) > Eps ? (q.A - p.A) / db : double.NaN;
            if (cross > a + 1e-9 && cross < b - 1e-9)
            {
                Append(result, Lower(p, q, a, cross));
                Append(result, Lower(p, q, cross, b));
            }
            else Append(result, Lower(p, q, a, b));
        }
        return result;
    }

    public sealed record ProfileResult(IReadOnlyList<(double S, double Z)> Points, bool Convex, string? Problem);

    /// <summary>
    /// Closed vertical polygon between baseZ and (underside - clearance) over the covered span.
    /// Returns Problem instead of points when the roof leaves gaps or the region splits.
    /// </summary>
    public static ProfileResult Profile(IReadOnlyList<Segment> underside, double baseZ, double clearance)
    {
        if (underside.Any(g => !g.Covered))
            return new ProfileResult(Array.Empty<(double, double)>(), false, "roof_does_not_cover_span");
        // Top polyline; a jump between pieces keeps two points at the same s.
        var poly = new List<(double S, double Z)>();
        foreach (var g in underside) { poly.Add((g.S0, g.Z(g.S0) - clearance)); poly.Add((g.S1, g.Z(g.S1) - clearance)); }
        bool Up((double S, double Z) v) => v.Z > baseZ + 1e-7;
        var chain = new List<(double S, double Z)>();
        var runs = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            if (Up(p))
            {
                if (i == 0) { runs++; chain.Add((p.S, baseZ)); }
                chain.Add(p);
            }
            if (i + 1 < poly.Count && Up(p) != Up(poly[i + 1]))
            {
                var q = poly[i + 1];
                var t = Math.Abs(q.Z - p.Z) > Eps ? (baseZ - p.Z) / (q.Z - p.Z) : 0;
                if (!Up(p)) runs++;
                chain.Add((p.S + t * (q.S - p.S), baseZ));
            }
        }
        if (poly.Count > 0 && Up(poly[^1])) chain.Add((poly[^1].S, baseZ));
        if (runs == 0) return new ProfileResult(Array.Empty<(double, double)>(), false, "underside_not_above_base");
        if (runs > 1) return new ProfileResult(Array.Empty<(double, double)>(), false, "profile_splits_into_several_regions");
        var cleaned = Simplify(chain);
        if (cleaned.Count < 3) return new ProfileResult(cleaned, false, "degenerate_profile");
        return new ProfileResult(cleaned, IsStrictlyConvex(cleaned), null);
    }

    public static List<(double S, double Z)> Simplify(IReadOnlyList<(double S, double Z)> points)
    {
        var list = new List<(double S, double Z)>();
        foreach (var p in points)
            if (list.Count == 0 || Math.Abs(list[^1].S - p.S) > 1e-7 || Math.Abs(list[^1].Z - p.Z) > 1e-7) list.Add(p);
        if (list.Count > 1 && Math.Abs(list[0].S - list[^1].S) < 1e-7 && Math.Abs(list[0].Z - list[^1].Z) < 1e-7)
            list.RemoveAt(list.Count - 1);
        bool changed = true;
        while (changed && list.Count > 3)
        {
            changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[(i - 1 + list.Count) % list.Count]; var b = list[i]; var c = list[(i + 1) % list.Count];
                var cross = (b.S - a.S) * (c.Z - a.Z) - (b.Z - a.Z) * (c.S - a.S);
                var scale = Math.Max(1e-12, Math.Sqrt((c.S - a.S) * (c.S - a.S) + (c.Z - a.Z) * (c.Z - a.Z)));
                if (Math.Abs(cross) / scale < 1e-7) { list.RemoveAt(i); changed = true; break; }
            }
        }
        return list;
    }

    /// <summary>Same rule as create_profile_wall: every other vertex strictly on one side of each edge.</summary>
    public static bool IsStrictlyConvex(IReadOnlyList<(double S, double Z)> uv)
    {
        int winding = 0;
        for (int i = 0; i < uv.Count; i++)
        {
            var next = (i + 1) % uv.Count;
            for (int j = 0; j < uv.Count; j++)
            {
                if (j == i || j == next) continue;
                var cross = (uv[next].S - uv[i].S) * (uv[j].Z - uv[i].Z) - (uv[next].Z - uv[i].Z) * (uv[j].S - uv[i].S);
                if (Math.Abs(cross) < 1e-9 || (winding != 0 && Math.Sign(cross) != winding)) return false;
                winding = Math.Sign(cross);
            }
        }
        return true;
    }

    public static Segment At(IReadOnlyList<Segment> segments, double s)
        => segments.First(g => s >= g.S0 - 1e-12 && s <= g.S1 + 1e-12);

    static Segment Lower(Segment p, Segment q, double a, double b)
    {
        var mid = (a + b) / 2;
        var low = p.Z(mid) <= q.Z(mid) ? p : q;
        return new Segment(a, b, low.A, low.B, low.Face);
    }

    static void Append(List<Segment> list, Segment piece)
    {
        if (list.Count > 0)
        {
            var last = list[^1];
            bool same = last.Face == piece.Face && (piece.Face < 0
                || (Math.Abs(last.A - piece.A) < 1e-9 && Math.Abs(last.B - piece.B) < 1e-9));
            if (same) { list[^1] = last with { S1 = piece.S1 }; return; }
        }
        list.Add(piece);
    }

    static List<double> SortedUnique(IEnumerable<double> values)
    {
        var list = new List<double>();
        foreach (var v in values.OrderBy(v => v))
            if (list.Count == 0 || v - list[^1] > 1e-9) list.Add(v);
        return list;
    }
}
