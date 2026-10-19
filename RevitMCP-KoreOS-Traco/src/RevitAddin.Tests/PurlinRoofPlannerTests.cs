using System;
using System.Linq;
using RevitMCPAddin.Commands;
using Xunit;
using static RevitMCPAddin.Commands.PurlinRoofPlanner;

namespace RevitMCPAddin.Tests;

/// <summary>Pilot cabin: 6 x 8 m, ridge along Y, 45 degrees, masonry top 2.80 m.</summary>
public class PurlinRoofPlannerTests
{
    static readonly Input Cabin = new() { X0 = 0, Y0 = 0, X1 = 6, Y1 = 8 };
    static void Near(double e, double a, double tol = 1e-6) => Assert.True(Math.Abs(e - a) < tol, $"expected {e}, got {a}");

    [Fact]
    public void Supports_follow_the_rafter_underside()
    {
        var plan = Build(Cabin);
        var plates = plan.Members.Where(m => m.Role == "wall_plate").ToList();
        Assert.Equal(2, plates.Count);
        Near(2.88, plates[0].Start.Z);
        Near(0.1, plates[0].Start.X); Near(5.9, plates[1].Start.X);
        Near(-0.575, plates[0].Start.Y); Near(8.575, plates[0].End.Y);

        var purlins = plan.Members.Where(m => m.Role == "purlin").ToList();
        Assert.Equal(2, purlins.Count);
        Near(1.55, purlins[0].Start.X); Near(4.45, purlins[1].Start.X);
        // underside = 2.88 + (u - 0.025); downslope edge u = 1.50
        Near(4.355, purlins[0].Start.Z); Near(4.355, purlins[1].Start.Z);

        var ridge = plan.Members.Single(m => m.Role == "ridge_beam");
        Near(3.0, ridge.Start.X); Near(5.805, ridge.Start.Z);
        Near(5.855, plan.RafterUndersideAtRidge);
    }

    [Fact]
    public void Covering_sits_on_rafters_and_starts_at_the_footprint()
    {
        var plan = Build(Cabin);
        var rafterVertical = 0.12 / Math.Cos(Math.PI / 4);
        Near(2.88 + (-0.6 - 0.025) + rafterVertical, plan.CoveringBaseElevationAtFootprint);
        Near(5.855 + rafterVertical, plan.BoardingUndersideAtRidge);
        Near(0.17, plan.CoveringThickness);
        Near(-0.6, plan.RoofFootprintX0); Near(8.6, plan.RoofFootprintY1);
    }

    [Fact]
    public void Rafters_clear_gable_bands_and_respect_spacing()
    {
        var plan = Build(Cabin);
        var s = plan.RafterStations;
        Assert.Equal(18, s.Count);
        Near(-0.545, s.First()); Near(8.545, s.Last());
        Assert.DoesNotContain(s, v => v + 0.03 > -0.05 && v - 0.03 < 0.25);   // gable band + trim, start
        Assert.DoesNotContain(s, v => v + 0.03 > 7.75 && v - 0.03 < 8.05);    // end
        Assert.True(plan.MaxRafterSpacing <= 0.60 + 1e-9);
        Assert.Equal(36, plan.Members.Count(m => m.Role == "rafter"));
    }

    [Fact]
    public void Rafter_tail_top_corner_stops_behind_eave_board()
    {
        var plan = Build(Cabin);
        var r = plan.Members.First(m => m.Role == "rafter" && m.Side == "a");
        var c = Math.Cos(Math.PI / 4); var sn = Math.Sin(Math.PI / 4);
        Near(-0.575, r.Start.X - 0.12 * sn);   // top corner on the eave board inner face
        Near(2.88 + (-0.575 - 0.025) + 0.12 / c, r.Start.Z + 0.12 * c);
        Near(3.0, r.End.X); Near(plan.RafterUndersideAtRidge, r.End.Z);
    }

    [Fact]
    public void Gable_profile_is_convex_under_boarding_with_joint_gap()
    {
        var plan = Build(Cabin);
        Assert.Equal(2, plan.Gables.Count);
        var g = plan.Gables[0];
        Near(0.1, g.WallCentreV);
        var uv = g.Profile.Select(p => (p.X, p.Z)).ToList();
        Assert.True(WallRoofProfileMath.IsStrictlyConvex(uv));
        Near(0.185, g.Profile[0].X); Near(2.80, g.Profile[0].Z);
        Near(plan.BoardingUndersideAtRidge - 0.015, g.Profile.Max(p => p.Z));
        Assert.All(g.Profile, p => Near(0.1, p.Y));
        Assert.Equal(3, g.PurlinOpenings.Count);
        foreach (var o in g.PurlinOpenings)
        {
            Near(0.0, o.Min.Y); Near(0.2, o.Max.Y);
            // Opening fully below the wall top at both edges.
            double TopAt(double x) => (Math.Min(x, 6 - x) - 0.025) + 2.88 + 0.12 / Math.Cos(Math.PI / 4) - 0.015;
            Assert.True(o.Max.Z < TopAt(o.Min.X) && o.Max.Z < TopAt(o.Max.X), $"{o.Role} pocket breaks the gable top");
        }
        Assert.Equal(7.9, plan.Gables[1].WallCentreV, 6);
    }

    [Fact]
    public void Joint_trims_run_along_inner_gable_faces_under_boarding()
    {
        var plan = Build(Cabin);
        var trims = plan.Members.Where(m => m.Role == "joint_trim").ToList();
        Assert.Equal(4, trims.Count);
        Near(0.225, trims[0].Start.Y); Near(7.775, trims[2].Start.Y);
        var rv = 0.12 / Math.Cos(Math.PI / 4);
        Near(2.88 + (0.175 - 0.025) + rv, trims[0].Start.Z);
        Near(plan.BoardingUndersideAtRidge, trims[0].End.Z);
    }

    [Fact]
    public void Blocking_sits_on_plates_between_rafters()
    {
        var plan = Build(Cabin);
        var blocks = plan.Members.Where(m => m.Role == "blocking").ToList();
        Assert.Equal(30, blocks.Count);
        Assert.All(blocks, b => Near(2.88, b.Start.Z - b.Height));
        Assert.All(blocks, b => Assert.True(b.End.Y - b.Start.Y > 0.05));
    }

    [Fact]
    public void Ridge_along_x_is_mapped()
    {
        var plan = Build(Cabin with { X1 = 8, Y1 = 6, RidgeAxis = "x" });
        var ridge = plan.Members.Single(m => m.Role == "ridge_beam");
        Near(3.0, ridge.Start.Y); Near(-0.575, ridge.Start.X); Near(8.575, ridge.End.X);   // behind the verge boards
        Near(0.1, plan.Gables[0].WallCentreV);
        Assert.All(plan.Gables[0].Profile, p => Near(0.1, p.X));
    }

    [Fact]
    public void Invalid_inputs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Build(Cabin with { PitchDegrees = 80 }));
        Assert.Throws<ArgumentException>(() => Build(Cabin with { Plate = new Section(0.3, 0.08) }));
        Assert.Throws<ArgumentException>(() => Build(Cabin with { RidgeAxis = "z" }));
    }

    static readonly Input Pilot = Cabin with { PlateAlignment = "outer", FasciaRecess = 0.05, FasciaAutoHeight = true };
    static readonly double Rv = 0.12 / Math.Cos(Math.PI / 4);

    [Fact]
    public void Outer_plate_and_blocking_sit_on_the_wall_outer_face()
    {
        var plan = Build(Pilot);
        var plate = plan.Members.First(m => m.Role == "wall_plate");
        Near(0.075, plate.Start.X); Near(2.88, plate.Start.Z);
        Near(-0.525, plate.Start.Y); Near(8.525, plate.End.Y);
        var block = plan.Members.First(m => m.Role == "blocking" && m.Side == "a");
        Near(0.0125, block.Start.X);
        Near(2.88 + 0.025 + Rv, block.Start.Z);           // inner edge touches the boarding
        Near(2.88, block.Start.Z - block.Height);          // sits on the plate
        Assert.True(block.CutByCovering);
        Near(0.16, plan.Gables[0].Profile[0].X);
        Near(2.88 - 0.6 + Rv, plan.CoveringBaseElevationAtFootprint);
    }

    [Fact]
    public void Recessed_eave_board_covers_the_rafter_plumb_depth()
    {
        var plan = Build(Pilot);
        var fascia = plan.Members.First(m => m.Role == "fascia_eave" && m.Side == "a");
        Near(-0.5375, fascia.Start.X);
        Near(-0.55, fascia.Start.Y); Near(8.55, fascia.End.Y);
        Near(2.88 - 0.525 + Rv, fascia.Start.Z);
        Near(Rv, fascia.Height);
        Assert.True(fascia.CutByCovering);
        var rafter = plan.Members.First(m => m.Role == "rafter" && m.Side == "a");
        Near(-0.525, rafter.Start.X - 0.12 * Math.Sin(Math.PI / 4));
        Near(2.88 - 0.525, fascia.Start.Z - fascia.Height);   // board bottom on the rafter underside line
        var verge = plan.Members.First(m => m.Role == "fascia_verge");
        Near(-0.5375, verge.Start.Y); Near(-0.525, verge.Start.X); Near(0.12, verge.Height);
        Near(-0.495, plan.RafterStations.First());   // edge rafter behind the verge board
        Near(-0.55, plan.Eave.FasciaOuterFaceOffset);
        Near(2.88 - 0.55 + Rv, plan.Eave.BoardingUndersideAtFasciaOuterFace);
    }

    [Fact]
    public void Recess_must_leave_board_inside_overhang()
        => Assert.Throws<ArgumentException>(() => Build(Pilot with { FasciaRecess = 0.6 }));
}
