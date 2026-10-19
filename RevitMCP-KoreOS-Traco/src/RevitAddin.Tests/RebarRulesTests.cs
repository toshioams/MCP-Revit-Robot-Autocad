using System;
using System.Linq;
using RevitMCPAddin.Commands;
using Xunit;
using static RevitMCPAddin.Commands.CurvedWallRebarPlanner;

namespace RevitMCPAddin.Tests;

/// <summary>NBR 6118 helpers (hand-checked values) and the circular wall planner.</summary>
public class RebarRulesTests
{
    static void Near(double e, double a, double tol = 1e-6) => Assert.True(Math.Abs(e - a) < tol, $"expected {e}, got {a}");

    [Fact]
    public void Lap_of_phi10_CA50_C30_half_spliced()
    {
        // fctd = 0.21 * 30^(2/3) / 1.4 = 1.448 MPa; poor bond fbd = 2.25 * 0.7 * 1.448 = 2.281 MPa
        var poor = Nbr6118.TensionLap(10, 30, "CA-50", goodBond: false, fractionSpliced: 0.5);
        Near(476.5, poor.LbMm, 0.1);
        Near(1.8, poor.Alpha0t);
        Near(900, poor.LapMm);           // 1.8 * 476.5 = 857.8 -> 900
        var good = Nbr6118.TensionLap(10, 30, "CA-50", goodBond: true, fractionSpliced: 0.5);
        Near(333.6, good.LbMm, 0.1);
        Near(650, good.LapMm);           // 600.4 -> 650
    }

    [Fact]
    public void Alpha0t_follows_table_9_4()
    {
        Near(1.2, Nbr6118.Alpha0t(0.2)); Near(1.4, Nbr6118.Alpha0t(0.25));
        Near(1.6, Nbr6118.Alpha0t(1.0 / 3)); Near(1.8, Nbr6118.Alpha0t(0.5)); Near(2.0, Nbr6118.Alpha0t(1));
    }

    [Fact]
    public void Mass_per_metre_matches_NBR_7480()
    {
        Near(0.395, Nbr6118.MassPerMetre(8), 0.001);
        Near(0.617, Nbr6118.MassPerMetre(10), 0.001);
        Near(0.245, Nbr6118.MassPerMetre(6.3), 0.001);
    }

    [Fact]
    public void Covers_and_bends()
    {
        Near(30, Nbr6118.NominalCoverMm("beam", 2));
        Near(25, Nbr6118.NominalCoverMm("viga", 2, rigorousControl: true));
        Near(45, Nbr6118.NominalCoverMm("slab", 4));
        Near(50, Nbr6118.MinBendDiameterMm(10, "CA-50", stirrup: false));
        Near(160, Nbr6118.MinBendDiameterMm(20, "CA-50", stirrup: false));
        Near(15, Nbr6118.MinBendDiameterMm(5, "CA-60", stirrup: true));
        Assert.Equal("CA-50", Nbr6118.GradeFromName("8 CA-50"));
        Assert.Equal("CA-60", Nbr6118.GradeFromName("Aço CA60 5.0"));
        Assert.Null(Nbr6118.GradeFromName("10M"));
    }

    // Demo wall: inner diameter 10.70 m, 20 cm thick, 3 m high, cover 3 cm, phi10 c/15 both ways, lap 90 cm.
    static readonly Input Tank = new(InnerRadius: 5.35, Thickness: 0.20, Height: 3.0, Cover: 0.03,
        RingDiameter: 0.010, VerticalDiameter: 0.010, RingSpacing: 0.15, VerticalSpacing: 0.15, LapLength: 0.90);

    [Fact]
    public void Rings_are_split_within_12m_and_laps_are_staggered()
    {
        var plan = Build(Tank);
        Assert.True(plan.Closed);
        Assert.Equal(21, plan.RingCount);                    // 2.93 m / 0.15 -> 20 spaces
        Assert.True(plan.RingSpacingActual <= 0.15 + 1e-9);
        Assert.Equal(4, plan.SegmentsPerRing);               // 34.65 m ring / (12 - 0.9) -> 4 bars
        Assert.Equal(4, plan.RingSets.Count);                // 2 faces x 2 stagger groups
        foreach (var set in plan.RingSets)
        {
            foreach (var seg in set.Segments) Assert.True(seg.Sweep * set.Radius <= 12.0 + 1e-9);
            // consecutive bars overlap by exactly the lap
            var s = set.Segments;
            for (var k = 0; k < s.Count; k++)
            {
                var next = s[(k + 1) % s.Count].StartAngle + (k + 1 == s.Count ? 2 * Math.PI : 0);
                Near(0.90, (s[k].EndAngle - next) * set.Radius, 1e-9);
            }
        }
        var interior = plan.RingSets.Where(r => r.Face == "interior").OrderBy(r => r.Group).ToList();
        Assert.Equal(11, interior[0].Count); Assert.Equal(10, interior[1].Count);
        Near(interior[0].Z0 + plan.RingSpacingActual, interior[1].Z0);
        // group 1 laps sit half a bar away from group 0 laps
        Near(Math.PI / 4, interior[1].Segments[0].StartAngle - interior[0].Segments[0].StartAngle);
        Near(5.385, interior[0].Radius);                     // rings outside the verticals
    }

    [Fact]
    public void Verticals_respect_spacing_on_their_circle()
    {
        var plan = Build(Tank);
        var inner = plan.VerticalSets.Single(v => v.Face == "interior");
        var outer = plan.VerticalSets.Single(v => v.Face == "exterior");
        Near(5.395, inner.Radius); Near(5.505, outer.Radius);
        Assert.Equal(226, inner.Angles.Count);
        Assert.Equal(231, outer.Angles.Count);
        Assert.True(2 * Math.PI * outer.Radius / outer.Angles.Count <= 0.15);
        Near(0.03, inner.Z0); Near(2.97, inner.Z1);
    }

    [Fact]
    public void Open_arc_bars_cover_the_arc_and_fit_the_bar_length()
    {
        var plan = Build(Tank with { InnerRadius = 10, Sweep = Math.PI / 2 });
        Assert.False(plan.Closed);
        foreach (var set in plan.RingSets)
        {
            Near(0, set.Segments.First().StartAngle);
            Near(Math.PI / 2, set.Segments.Last().EndAngle);
            foreach (var seg in set.Segments) Assert.True(seg.Sweep * set.Radius <= 12.0 + 1e-9);
        }
        var v = plan.VerticalSets.First();
        Near(0.03, (v.Angles.First()) * v.Radius, 1e-9);
    }

    // Silo 26-060: axis diameter 10.70 m, 25 cm thick, 25.50 m high, cover 4 cm.
    static readonly Input Silo = new(InnerRadius: 5.225, Thickness: 0.25, Height: 25.50, Cover: 0.04,
        RingDiameter: 0.010, VerticalDiameter: 0.010, RingSpacing: 0.15, VerticalSpacing: 0.15, LapLength: 0.90,
        VerticalLapLength: 0.65);

    [Fact]
    public void Tall_wall_verticals_are_split_with_staggered_laps()
    {
        var plan = Build(Silo);
        foreach (var face in new[] { "interior", "exterior" })
        {
            var sets = plan.VerticalSets.Where(v => v.Face == face).OrderBy(v => v.Group).ToList();
            Assert.Equal(2, sets.Count);
            Assert.Equal(3, sets[0].Pieces.Count);           // 25.42 m / (12 - 0.65) -> 3 bars of 9.12 m
            Assert.Equal(4, sets[1].Pieces.Count);           // shifted group: half bar at both ends
            foreach (var s in sets)
            {
                Near(0.04, s.Z0); Near(25.46, s.Z1);
                foreach (var piece in s.Pieces) Assert.True(piece.Sweep <= 12.0 + 1e-9);
                for (var k = 0; k + 1 < s.Pieces.Count; k++) Near(0.65, s.Pieces[k].EndAngle - s.Pieces[k + 1].StartAngle);
            }
            // splices of neighbouring bars are half a bar apart
            Near(sets[0].Pieces[1].StartAngle - sets[0].Pieces[0].StartAngle, 2 * (sets[1].Pieces[1].StartAngle - sets[1].Pieces[0].StartAngle));
            Assert.Equal(sets[0].Angles.Count + sets[1].Angles.Count, face == "interior" ? 222 : 228);
        }
        // every layer is concentric with the wall, so the clear cover is the same all around
        Near(5.225 + 0.04 + 0.005, plan.RingSets.First(r => r.Face == "interior").Radius);
        Near(5.475 - 0.04 - 0.005, plan.RingSets.First(r => r.Face == "exterior").Radius);
    }

    [Fact]
    public void Lapped_bars_touch_instead_of_coinciding()
    {
        foreach (var plan in new[] { Build(Tank), Build(Silo) })
        {
            foreach (var set in plan.RingSets)
            {
                var s = set.Segments;
                for (var k = 0; k < s.Count; k++)
                {
                    var next = s[(k + 1) % s.Count];
                    Assert.True(Math.Abs(s[k].Shift - next.Shift) >= 0.010 - 1e-9, $"ring bars {k} and {(k + 1) % s.Count} coincide");
                    Assert.True(Math.Abs(s[k].Shift - next.Shift) <= 0.020 + 1e-9);
                }
            }
            foreach (var v in plan.VerticalSets)
                for (var k = 0; k + 1 < v.Pieces.Count; k++)
                    Near(0.010, Math.Abs(v.Pieces[k].Shift - v.Pieces[k + 1].Shift));
        }
        // silo interior: 3 bars per ring (odd) -> 0, 1 and 2 diameters
        var interior = Build(Silo).RingSets.First(r => r.Face == "interior").Segments;
        Assert.Equal(new[] { 0.0, 0.010, 0.020 }, interior.Select(g => Math.Round(g.Shift, 3)));
    }

    [Fact]
    public void Short_wall_verticals_are_one_bar()
    {
        var plan = Build(Tank);
        Assert.All(plan.VerticalSets, v => Assert.Single(v.Pieces));
    }

    [Fact]
    public void Rejects_walls_too_thin_for_two_layers()
    {
        Assert.Throws<ArgumentException>(() => Build(Tank with { Thickness = 0.09 }));
    }
}
