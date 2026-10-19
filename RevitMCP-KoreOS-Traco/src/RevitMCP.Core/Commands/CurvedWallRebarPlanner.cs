using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Pure geometry for the reinforcement of a circular (or arc) concrete wall: horizontal rings
/// split into bars no longer than the commercial length, overlapping by the lap length, with the
/// laps of consecutive rings staggered by half a bar; vertical bars spread around the wall.
/// No Revit types, so it is unit-tested. Units: metres and radians; z from the wall base.
/// </summary>
public static class CurvedWallRebarPlanner
{
    public sealed record Input(
        double InnerRadius, double Thickness, double Height,
        double Cover, double RingDiameter, double VerticalDiameter,
        double RingSpacing, double VerticalSpacing, double LapLength,
        double MaxBarLength = 12.0, double StartAngle = 0.0, double Sweep = 2 * Math.PI,
        bool RingsOutside = true, bool InteriorFace = true, bool ExteriorFace = true,
        double VerticalLapLength = 0.0);

    /// <summary>
    /// One bar: for rings an arc from StartAngle sweeping Sweep (counter-clockwise); for verticals the
    /// z range. Shift is the contact-lap offset inside the layer (rings: lift in z; verticals: distance
    /// along the circle) so that two lapped bars sit side by side instead of inside each other.
    /// </summary>
    public sealed record Segment(double StartAngle, double Sweep, double Shift = 0)
    {
        public double EndAngle => StartAngle + Sweep;
    }

    /// <summary>A set of identical rings (same segments) repeated vertically.</summary>
    public sealed record RingSet(string Face, int Group, double Radius, double Z0, int Count, double Spacing,
        IReadOnlyList<Segment> Segments);

    /// <summary>
    /// Vertical bars of one face and stagger group. Pieces are the bars of one vertical line, bottom to top:
    /// a tall wall is split into bars no longer than MaxBarLength, each lapping the next by VerticalLapLength,
    /// with the splices of group 1 (odd positions) half a bar above those of group 0.
    /// </summary>
    public sealed record VerticalSet(string Face, double Radius, int Group, IReadOnlyList<double> Angles, IReadOnlyList<Segment> Pieces)
    {
        public double Z0 => Pieces[0].StartAngle;
        public double Z1 => Pieces[^1].EndAngle;
    }

    public sealed record Plan(
        bool Closed, IReadOnlyList<RingSet> RingSets, IReadOnlyList<VerticalSet> VerticalSets,
        int RingCount, double RingSpacingActual, int SegmentsPerRing, double LapLength);

    public static bool IsClosed(double sweep) => sweep >= 2 * Math.PI - 1e-6;

    public static Plan Build(Input i)
    {
        Validate(i);
        var closed = IsClosed(i.Sweep);
        var sweep = closed ? 2 * Math.PI : i.Sweep;
        var outer = i.InnerRadius + i.Thickness;

        // Rings: first and last at cover + half bar from base and top; spacing never above the asked one.
        var zStart = i.Cover + i.RingDiameter / 2;
        var zEnd = i.Height - i.Cover - i.RingDiameter / 2;
        var rings = (int)Math.Ceiling((zEnd - zStart) / i.RingSpacing - 1e-9) + 1;
        var actual = rings > 1 ? (zEnd - zStart) / (rings - 1) : 0;

        var faces = new List<(string face, double ringR, double vertR)>();
        double ringOff = i.Cover + i.RingDiameter / 2, vertOff = i.Cover + i.VerticalDiameter / 2;
        if (i.RingsOutside) vertOff += i.RingDiameter; else ringOff += i.VerticalDiameter;
        if (i.InteriorFace) faces.Add(("interior", i.InnerRadius + ringOff, i.InnerRadius + vertOff));
        if (i.ExteriorFace) faces.Add(("exterior", outer - ringOff, outer - vertOff));

        var ringSets = new List<RingSet>();
        var segmentsPerRing = 0;
        foreach (var (face, r, _) in faces)
        {
            var step = SegmentStep(r, sweep, closed, i.LapLength, i.MaxBarLength, out var n);
            segmentsPerRing = Math.Max(segmentsPerRing, n);
            for (var g = 0; g < 2; g++)
            {
                var count = g == 0 ? (rings + 1) / 2 : rings / 2;
                if (count == 0) continue;
                var segs = ContactLaps(Segments(i.StartAngle, sweep, closed, step, n, g == 0 || n == 1 ? 0 : step / 2, i.LapLength / r),
                    closed, i.RingDiameter);
                ringSets.Add(new RingSet(face, g, r, zStart + g * actual, count, 2 * actual, segs));
            }
        }

        var verticals = new List<VerticalSet>();
        foreach (var (face, _, r) in faces)
        {
            var angles = new List<double>();
            if (closed)
            {
                var n = (int)Math.Ceiling(2 * Math.PI * r / i.VerticalSpacing - 1e-9);
                for (var k = 0; k < n; k++) angles.Add(i.StartAngle + k * 2 * Math.PI / n);
            }
            else
            {
                // End bars at cover from the wall ends (measured on the bar circle).
                var a0 = i.StartAngle + i.Cover / r; var a1 = i.StartAngle + sweep - i.Cover / r;
                var n = (int)Math.Ceiling((a1 - a0) * r / i.VerticalSpacing - 1e-9) + 1;
                for (var k = 0; k < n; k++) angles.Add(n == 1 ? a0 : a0 + k * (a1 - a0) / (n - 1));
            }
            var z0 = i.Cover; var span = i.Height - 2 * i.Cover;
            if (span <= i.MaxBarLength + 1e-9)
            {
                verticals.Add(new VerticalSet(face, r, 0, angles, new[] { new Segment(z0, span) }));
                continue;
            }
            if (i.VerticalLapLength <= 0) throw new ArgumentException("Wall taller than one bar: a vertical lap length is required.");
            var vstep = SegmentStep(1.0, span, false, i.VerticalLapLength, i.MaxBarLength, out var vn);
            for (var g = 0; g < 2; g++)
            {
                var ga = angles.Where((_, k) => k % 2 == g).ToList();
                if (ga.Count == 0) continue;
                verticals.Add(new VerticalSet(face, r, g, ga,
                    ContactLaps(Segments(z0, span, false, vstep, vn, g == 0 ? 0 : vstep / 2, i.VerticalLapLength), false, i.VerticalDiameter)));
            }
        }

        return new Plan(closed, ringSets, verticals, rings, actual, segmentsPerRing, i.LapLength);
    }

    /// <summary>
    /// Angular step between splice points so that every bar (step plus lap) fits the commercial
    /// length. A closed ring needs at least two bars; n is the number of bars of one ring.
    /// </summary>
    internal static double SegmentStep(double radius, double sweep, bool closed, double lap, double maxLen, out int n)
    {
        if (lap >= maxLen) throw new ArgumentException("Lap length must be shorter than the maximum bar length.");
        var length = sweep * radius;
        // n bars of (step + lap) each: step + lap <= maxLen holds for both stagger groups (the shifted group
        // only adds shorter end bars). A closed ring needs at least two bars; a run that fits one bar is not spliced.
        n = closed ? Math.Max(2, (int)Math.Ceiling(length / (maxLen - lap) - 1e-9))
            : length <= maxLen + 1e-9 ? 1 : (int)Math.Ceiling(length / (maxLen - lap) - 1e-9);
        return sweep / n;
    }

    internal static List<Segment> Segments(double start, double sweep, bool closed, double step, int n, double offset, double lapAngle)
    {
        var segs = new List<Segment>();
        if (closed)
        {
            for (var k = 0; k < n; k++) segs.Add(new Segment(start + offset + k * step, step + lapAngle));
            return segs;
        }
        // Open arc: cut points inside the arc, each bar extends forward by the lap, the last ends at the arc end.
        var cuts = new List<double> { start };
        for (var a = start + (offset > 0 ? offset : step); a < start + sweep - 1e-9; a += step) cuts.Add(a);
        for (var k = 0; k < cuts.Count; k++)
        {
            var end = k + 1 < cuts.Count ? Math.Min(cuts[k + 1] + lapAngle, start + sweep) : start + sweep;
            segs.Add(new Segment(cuts[k], end - cuts[k]));
        }
        return segs;
    }

    /// <summary>
    /// Offsets every other bar by one diameter so lapped bars touch instead of coinciding. A closed run
    /// with an odd number of bars would lap its last bar on the first at the same offset: that bar goes
    /// two diameters away.
    /// </summary>
    internal static List<Segment> ContactLaps(List<Segment> segs, bool closed, double diameter)
    {
        if (segs.Count < 2) return segs;
        return segs.Select((g, k) => g with
        {
            Shift = k % 2 == 1 ? diameter : closed && segs.Count % 2 == 1 && k == segs.Count - 1 ? 2 * diameter : 0,
        }).ToList();
    }

    private static void Validate(Input i)
    {
        if (i.InnerRadius <= 0 || i.Thickness <= 0 || i.Height <= 0) throw new ArgumentException("Radius, thickness and height must be positive.");
        if (i.RingSpacing <= 0 || i.VerticalSpacing <= 0) throw new ArgumentException("Spacings must be positive.");
        if (i.Sweep <= 0) throw new ArgumentException("Sweep must be positive.");
        if (!i.InteriorFace && !i.ExteriorFace) throw new ArgumentException("Choose at least one face.");
        var core = i.Thickness - 2 * i.Cover - 2 * (i.RingDiameter + i.VerticalDiameter);
        if (i.InteriorFace && i.ExteriorFace && core < 0)
            throw new ArgumentException($"Wall too thin: two layers need {2 * (i.Cover + i.RingDiameter + i.VerticalDiameter):0.###} m.");
        if (i.Height <= 2 * i.Cover + i.RingDiameter) throw new ArgumentException("Wall height smaller than the covers.");
    }

    /// <summary>Total length of one ring set (all rings), metres.</summary>
    public static double Length(RingSet s) => s.Count * s.Segments.Sum(g => g.Sweep * s.Radius);

    public static double Length(VerticalSet v) => v.Angles.Count * v.Pieces.Sum(g => g.Sweep);
}
