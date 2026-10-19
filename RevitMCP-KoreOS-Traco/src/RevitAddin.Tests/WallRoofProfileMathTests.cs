using System;
using System.Collections.Generic;
using System.Linq;
using RevitMCPAddin.Commands;
using Xunit;
using static RevitMCPAddin.Commands.WallRoofProfileMath;

namespace RevitMCPAddin.Tests;

/// <summary>
/// Geometry of the pilot cabin (meters): footprint -0.6..6.6 x -0.6..8.6, ridge along Y
/// at x=3, underside 2.2 at the eaves and 45 degrees, walls 0..6 x 0..8, top 2.80.
/// </summary>
public class WallRoofProfileMathTests
{
    const double R = 0.7071067811865476;

    static IReadOnlyList<Pt2> Rect(double x0, double y0, double x1, double y1)
        => new[] { new Pt2(x0, y0), new Pt2(x1, y0), new Pt2(x1, y1), new Pt2(x0, y1) };

    static readonly UndersideFace West = new(-0.6, 0, 2.2, R, 0, -R, new[] { Rect(-0.6, -0.6, 3, 8.6) });
    static readonly UndersideFace East = new(6.6, 0, 2.2, -R, 0, -R, new[] { Rect(3, -0.6, 6.6, 8.6) });
    static readonly UndersideFace[] Cabin = { West, East };

    static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) < 1e-6, $"expected {expected}, got {actual}");

    static void SamePolygon(IEnumerable<(double S, double Z)> expected, IReadOnlyList<(double S, double Z)> actual)
    {
        var e = expected.ToList();
        Assert.Equal(e.Count, actual.Count);
        foreach (var v in e)
            Assert.Contains(actual, a => Math.Abs(a.S - v.S) < 1e-6 && Math.Abs(a.Z - v.Z) < 1e-6);
    }

    [Fact]
    public void Face_plane_matches_cabin_underside()
    {
        Near(2.8, West.ZAt(0, 4));
        Near(5.8, West.ZAt(3, 4));
        Near(3.0, East.ZAt(5.8, 4));
    }

    [Fact]
    public void South_gable_is_a_triangle_up_to_the_ridge()
    {
        var line = Underside(Cabin, new Pt2(0, 0.1), new Pt2(1, 0), 0, 6);
        Assert.Equal(2, line.Count);
        Near(5.8, line[0].Z(3));
        var profile = Profile(line, 2.8, 0);
        Assert.Null(profile.Problem);
        Assert.True(profile.Convex);
        SamePolygon(new[] { (0.0, 2.8), (3.0, 5.8), (6.0, 2.8) }, profile.Points);
    }

    [Fact]
    public void Clearance_lowers_the_top_and_moves_base_corners()
    {
        var profile = Profile(Underside(Cabin, new Pt2(0, 0.1), new Pt2(1, 0), 0, 6), 2.8, 0.05);
        Assert.Null(profile.Problem);
        SamePolygon(new[] { (0.05, 2.8), (3.0, 5.75), (5.95, 2.8) }, profile.Points);
    }

    [Fact]
    public void Gable_stopping_at_eave_wall_inner_face_has_a_vertical_end()
    {
        var profile = Profile(Underside(Cabin, new Pt2(0, 7.9), new Pt2(1, 0), 0, 5.8), 2.8, 0);
        Assert.Null(profile.Problem);
        Assert.True(profile.Convex);
        SamePolygon(new[] { (0.0, 2.8), (3.0, 5.8), (5.8, 3.0), (5.8, 2.8) }, profile.Points);
    }

    [Fact]
    public void Eave_wall_shows_wedge_across_thickness_and_no_profile_above_base()
    {
        var interior = Underside(Cabin, new Pt2(5.8, 0), new Pt2(0, 1), 0.2, 8);
        var exterior = Underside(Cabin, new Pt2(6.0, 0), new Pt2(0, 1), 0.2, 8);
        Near(3.0, interior.Single().Z(4));
        Near(2.8, exterior.Single().Z(4));
        var combined = WallRoofProfileMath.Min(interior, exterior);
        Near(2.8, combined.Single().Z(4));
        Assert.Equal("underside_not_above_base", Profile(combined, 2.8, 0).Problem);
    }

    [Fact]
    public void Wall_beyond_the_roof_is_reported_uncovered()
    {
        var line = Underside(Cabin, new Pt2(0, 0.1), new Pt2(1, 0), -1, 7);
        Assert.False(line.First().Covered);
        Assert.False(line.Last().Covered);
        Near(0.4, line.First().S1 - line.First().S0);
        Assert.Equal("roof_does_not_cover_span", Profile(line, 2.8, 0).Problem);
    }

    [Fact]
    public void Wall_diagonal_to_ridge_uses_the_lower_face_across_thickness()
    {
        // Gable wall rotated slightly: the two faces see different heights near the ridge.
        var dir = new Pt2(1, 0);
        var a = Underside(Cabin, new Pt2(0, 0), dir, 0, 6);
        var b = Underside(new[] { West with { Oz = 2.1 }, East }, new Pt2(0, 0.2), dir, 0, 6);
        var combined = WallRoofProfileMath.Min(a, b);
        Near(2.7, At(combined, 0).Z(0));
        Near(5.8, At(combined, 3.05).Z(3.05) + 0.05);
    }

    [Fact]
    public void Valley_roof_gives_non_convex_profile()
    {
        var left = new UndersideFace(0, 0, 5, -R, 0, -R, new[] { Rect(0, -1, 3, 1) });
        var right = new UndersideFace(6, 0, 5, R, 0, -R, new[] { Rect(3, -1, 6, 1) });
        var profile = Profile(Underside(new[] { left, right }, new Pt2(0, 0), new Pt2(1, 0), 0, 6), 1, 0);
        Assert.Null(profile.Problem);
        Assert.False(profile.Convex);
    }

    [Fact]
    public void Hole_in_face_is_uncovered()
    {
        var face = new UndersideFace(0, 0, 3, 0, 0, -1, new[] { Rect(0, -1, 10, 1), Rect(4, -0.5, 6, 0.5) });
        var line = Underside(new[] { face }, new Pt2(0, 0), new Pt2(1, 0), 0, 10);
        Assert.Equal(3, line.Count);
        Assert.False(line[1].Covered);
        Near(4, line[1].S0);
        Near(6, line[1].S1);
    }
}
