using System;
using System.Linq;
using RevitMCPAddin.Commands;
using Xunit;
using static RevitMCPAddin.Commands.ProfileShapes;

namespace RevitMCPAddin.Tests;

public class ProfileShapesTests
{
    static void Near(double e, double a) => Assert.True(Math.Abs(e - a) < 1e-9, $"expected {e}, got {a}");

    [Fact]
    public void Ridge_cap_underside_follows_both_45_degree_planes()
    {
        var loop = RidgeCap(45, 0.20, 0.01);
        Assert.Equal(6, loop.Count);
        Near(0, loop[1].U); Near(0, loop[1].V);
        // wing tips lie on v = -|u| (45 degrees)
        Near(-Math.Abs(loop[0].U), loop[0].V); Near(-Math.Abs(loop[2].U), loop[2].V);
        Near(0.20 * Math.Cos(Math.PI / 4), loop[2].U);
        // shell thickness measured perpendicular to the wing
        var d = Math.Sqrt(Math.Pow(loop[3].U - loop[2].U, 2) + Math.Pow(loop[3].V - loop[2].V, 2));
        Near(0.01, d);
        Near(0.01 / Math.Cos(Math.PI / 4), loop[4].V);
        ValidateLoop(loop);
        Assert.True(Math.Abs(Area(loop)) > 0);
    }

    [Fact]
    public void Ridge_cap_top_is_parallel_to_underside()
    {
        var loop = RidgeCap(30, 0.25, 0.012);
        double slopeUnder = (loop[2].V - loop[1].V) / (loop[2].U - loop[1].U);
        double slopeTop = (loop[3].V - loop[4].V) / (loop[3].U - loop[4].U);
        Near(slopeUnder, slopeTop);
        Near(-Math.Tan(Math.PI / 6), slopeUnder);
    }

    [Fact]
    public void Invalid_ridge_caps_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => RidgeCap(85, 0.2, 0.01));
        Assert.Throws<ArgumentException>(() => RidgeCap(45, 0.01, 0.02));
    }

    [Fact]
    public void Self_intersecting_and_degenerate_loops_are_rejected()
    {
        var bowtie = new[] { new UV(0, 0), new UV(1, 1), new UV(1, 0), new UV(0, 1) };
        Assert.Throws<ArgumentException>(() => ValidateLoop(bowtie));
        var line = new[] { new UV(0, 0), new UV(1, 0), new UV(2, 0) };
        Assert.Throws<ArgumentException>(() => ValidateLoop(line));
        var tiny = new[] { new UV(0, 0), new UV(0.0001, 0), new UV(0, 1) };
        Assert.Throws<ArgumentException>(() => ValidateLoop(tiny));
        ValidateLoop(new[] { new UV(0, 0), new UV(0.3, 0), new UV(0.3, 0.2), new UV(0, 0.2) });
    }
}
