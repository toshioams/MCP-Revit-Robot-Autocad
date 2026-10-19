using System.Linq;
using RevitMCPAddin.Commands;
using Xunit;
using W = RevitMCPAddin.Commands.WallEndSnapper.Wall;

namespace RevitMCPAddin.Tests;

public class WallEndSnapperTests
{
    static void Near(double e, double a) => Assert.True(System.Math.Abs(e - a) < 1e-6, $"expected {e}, got {a}");

    [Fact]
    public void L_corner_with_a_gap_extends_both_walls_to_the_centerline_intersection()
    {
        var walls = new[] { new W(1, 0, 0, 4.8, 0, 0.15), new W(2, 5, 0.3, 5, 4, 0.15) };
        var moves = WallEndSnapper.Plan(walls);
        Assert.Equal(2, moves.Count);
        var m1 = moves.Single(m => m.Id == 1); Assert.Equal(1, m1.End); Near(5, m1.ToX); Near(0, m1.ToY); Assert.Equal("L", m1.Kind);
        var m2 = moves.Single(m => m.Id == 2); Assert.Equal(0, m2.End); Near(5, m2.ToX); Near(0, m2.ToY);
    }

    [Fact]
    public void T_stem_short_of_the_through_wall_reaches_its_centerline_and_the_through_wall_is_untouched()
    {
        var walls = new[] { new W(1, 0, 0, 10, 0, 0.25), new W(2, 5, 0.5, 5, 4, 0.1) };
        var moves = WallEndSnapper.Plan(walls);
        var m = Assert.Single(moves);
        Assert.Equal(2, m.Id); Near(0, m.ToY); Assert.Equal("T", m.Kind); Near(0.5, m.Distance);
    }

    [Fact]
    public void Overshooting_end_is_trimmed_back_to_the_centerline()
    {
        var walls = new[] { new W(1, 0, 0, 10, 0, 0.25), new W(2, 5, -0.2, 5, 4, 0.1) };
        var m = Assert.Single(WallEndSnapper.Plan(walls));
        Near(0, m.ToY); Near(0.2, m.Distance);
    }

    [Fact]
    public void Collinear_gap_closes_to_the_other_end()
    {
        var walls = new[] { new W(1, 0, 0, 3, 0, 0.15), new W(2, 3.4, 0.01, 6, 0.01, 0.15) };
        var moves = WallEndSnapper.Plan(walls);
        var m1 = moves.Single(m => m.Id == 1);
        Assert.Equal("collinear", m1.Kind); Near(3.4, m1.ToX);
    }

    [Fact]
    public void Far_walls_and_already_joined_ends_are_left_alone()
    {
        var walls = new[] { new W(1, 0, 0, 5, 0, 0.15), new W(2, 5, 0, 5, 4, 0.15), new W(3, 20, 0, 20, 4, 0.15) };
        Assert.Empty(WallEndSnapper.Plan(walls));
    }

    [Fact]
    public void Joined_collinear_ends_are_not_pulled_onto_a_nearby_partition()
    {
        // facade split at y=39.68 (two joined pieces), a 3 cm cubicle divider stopping short of it at y=40.25
        var walls = new[]
        {
            new W(1, 12.8755, 33.35, 12.8755, 39.68, 0.25), new W(2, 12.8755, 39.68, 12.8755, 47.8, 0.25),
            new W(3, 11.11, 40.2534, 12.70, 40.2534, 0.03),   // 5 cm short of the face
        };
        var moves = WallEndSnapper.Plan(walls);
        Assert.DoesNotContain(moves, m => m.Id is 1 or 2);
        var m3 = Assert.Single(moves); Assert.Equal(3, m3.Id); Near(12.8755, m3.ToX); Assert.Equal("T", m3.Kind);
    }
}
