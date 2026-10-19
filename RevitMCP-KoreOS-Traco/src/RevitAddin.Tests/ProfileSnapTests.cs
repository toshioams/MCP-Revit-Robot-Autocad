using System.Collections.Generic;
using RevitMCPAddin.Commands;
using Xunit;

namespace RevitMCPAddin.Tests;

public class ProfileSnapTests
{
    [Fact]
    public void Nearly_vertical_edge_becomes_vertical_and_real_slopes_stay()
    {
        var pts = new List<(double X, double Y)> { (13.05, -0.99), (44.2, -4.8), (52.775, 29.214), (13.0, 41.19) };
        // last edge 13.0 -> 13.05 over 42 m is 0.07 degrees off vertical
        var n = ProfileSnap.SnapNearAxis(pts);
        Assert.Equal(1, n);
        Assert.Equal(13.0, pts[0].X);      // closing edge moved the first vertex onto x = 13.0
        Assert.Equal((44.2, -4.8), pts[1]); // 7 degree edge untouched
    }

    [Fact]
    public void Axis_aligned_rectangle_is_unchanged()
    {
        var pts = new List<(double X, double Y)> { (0, 0), (4, 0), (4, 3), (0, 3) };
        Assert.Equal(0, ProfileSnap.SnapNearAxis(pts));
    }
}
