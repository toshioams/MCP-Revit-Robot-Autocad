using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Revit-independent geometry for a timber purlin roof on masonry gables:
/// wall plates (frechais) on the eave walls, intermediate purlins (terças) and ridge beam
/// (cumeeira) bearing on the gables and cantilevering over the lateral overhang, rafters
/// (caibros) over them, boarding (lambril) on the rafters, then membrane, battens and tiles
/// as one roof covering element. Gables are separate profile walls under the boarding with
/// a perimeter joint, pierced by the purlins. Rectangular plan, symmetric duopitch.
///
/// Local frame: u across the ridge (0 at the outer face of the first eave wall), v along
/// the ridge (0 at the outer face of the first gable wall). Output is mapped to document XY.
///
/// This lays out geometry only. It does not size members, check loads, connections,
/// anchorage, wind uplift or thrust. Sections are inputs for the engineer to confirm.
/// </summary>
public static class PurlinRoofPlanner
{
    public sealed record Section(double Width, double Height);

    public sealed record Input
    {
        public double X0 { get; init; }
        public double Y0 { get; init; }
        public double X1 { get; init; }
        public double Y1 { get; init; }
        /// <summary>"y": ridge parallel to Y (eave walls at X0/X1). "x": ridge parallel to X.</summary>
        public string RidgeAxis { get; init; } = "y";
        public double PitchDegrees { get; init; } = 45;
        public double EaveOverhang { get; init; } = 0.60;
        public double GableOverhang { get; init; } = 0.60;
        public double EaveWallThickness { get; init; } = 0.20;
        public double GableWallThickness { get; init; } = 0.20;
        public double MasonryTop { get; init; } = 2.80;
        public Section Plate { get; init; } = new(0.15, 0.08);
        public Section Purlin { get; init; } = new(0.10, 0.32);
        public Section Ridge { get; init; } = new(0.10, 0.32);
        public int IntermediatePurlinsPerSide { get; init; } = 1;
        public Section Rafter { get; init; } = new(0.06, 0.12);
        public double RafterMaxSpacing { get; init; } = 0.60;
        public double Boarding { get; init; } = 0.02;
        public double Membrane { get; init; } = 0;   // Revit membranes are zero-width layers
        public double Battens { get; init; } = 0.05;
        public double Tile { get; init; } = 0.10;
        public double JointGap { get; init; } = 0.015;
        public Section JointTrim { get; init; } = new(0.05, 0.02);
        public double PurlinPocketClearance { get; init; } = 0.01;
        public double BlockingThickness { get; init; } = 0.025;
        public bool Blocking { get; init; } = true;
        public Section Fascia { get; init; } = new(0.025, 0.20);
        public bool FasciaBoards { get; init; } = true;
        /// <summary>"center": plate on the eave wall axis. "outer": plate flush with the wall outer face, blocking in the outer face plane.</summary>
        public string PlateAlignment { get; init; } = "center";
        /// <summary>Fascia/verge board outer face set back from the roof edge (tile drip overhang).</summary>
        public double FasciaRecess { get; init; } = 0;
        /// <summary>Eave board depth = plumb depth of the rafter; verge board = rafter depth.</summary>
        public bool FasciaAutoHeight { get; init; } = false;
    }

    public readonly record struct P3(double X, double Y, double Z);

    /// <summary>Straight member. Start/End lie on the reference line named by ZJustification (top/bottom) at the section centre.</summary>
    /// <remarks>CutByCovering: the top edge rises into the boarding on purpose (square board under a
    /// sloped soffit); join it with the covering so the roof trims the chamfer.</remarks>
    public sealed record Member(string Role, string Side, P3 Start, P3 End, double Width, double Height, string ZJustification, bool CutByCovering = false);

    public sealed record EaveInfo(double FasciaOuterFaceOffset, double BoardingUndersideAtFasciaOuterFace, double RafterUndersideAtFasciaInnerFace);

    public sealed record Box(string Role, P3 Min, P3 Max);

    public sealed record Gable(string Name, double WallCentreV, IReadOnlyList<P3> Profile, IReadOnlyList<Box> PurlinOpenings);

    public sealed record Plan(
        IReadOnlyList<Member> Members,
        IReadOnlyList<Gable> Gables,
        double RoofFootprintX0, double RoofFootprintY0, double RoofFootprintX1, double RoofFootprintY1,
        double CoveringBaseElevationAtFootprint, double CoveringThickness,
        double BoardingUndersideAtRidge, double RafterUndersideAtRidge,
        IReadOnlyList<double> RafterStations, double MaxRafterSpacing, double MaxRafterSpanHorizontal,
        IReadOnlyList<string> Warnings, EaveInfo Eave);

    public static Plan Build(Input i)
    {
        Validate(i);
        var alongY = i.RidgeAxis.Equals("y", StringComparison.OrdinalIgnoreCase);
        double uLen = alongY ? i.X1 - i.X0 : i.Y1 - i.Y0, vLen = alongY ? i.Y1 - i.Y0 : i.X1 - i.X0;
        double tan = Math.Tan(i.PitchDegrees * Math.PI / 180), cos = Math.Cos(i.PitchDegrees * Math.PI / 180);
        double half = uLen / 2;
        var warnings = new List<string>();

        P3 Map(double u, double v, double z) => alongY ? new P3(i.X0 + u, i.Y0 + v, z) : new P3(i.X0 + v, i.Y0 + u, z);

        // Rafter underside touches the downslope top edge of every support (plate, purlins, ridge).
        double plateTop = i.MasonryTop + i.Plate.Height;
        var plateOuter = i.PlateAlignment.Equals("outer", StringComparison.OrdinalIgnoreCase);
        double plateU0 = plateOuter ? 0 : (i.EaveWallThickness - i.Plate.Width) / 2;   // outer edge of plate
        double Under(double u) => plateTop + (Math.Min(u, uLen - u) - plateU0) * tan;   // symmetric
        double rafterVertical = i.Rafter.Height / cos;
        double Boards(double u) => Under(u) + rafterVertical;

        var members = new List<Member>();
        double vStart = -i.GableOverhang, vEnd = vLen + i.GableOverhang;
        // Boards sit inside the roof edge by FasciaRecess; everything else stops behind them.
        double fw = i.FasciaBoards ? i.Fascia.Width : 0, recess = i.FasciaBoards ? i.FasciaRecess : 0;
        double uFasciaOuter = -i.EaveOverhang + recess, uFasciaInner = uFasciaOuter + fw;
        double vInnerStart = vStart + recess + fw, vInnerEnd = vEnd - recess - fw;

        // Supports along the ridge direction (lines of constant u), described by top centre.
        void Support(string role, string side, double uCentre, Section s, double top, double from, double to)
            => members.Add(new Member(role, side, Map(uCentre, from, top), Map(uCentre, to, top), s.Width, s.Height, "top"));

        double plateCentre = plateU0 + i.Plate.Width / 2;
        Support("wall_plate", "a", plateCentre, i.Plate, plateTop, vInnerStart, vInnerEnd);
        Support("wall_plate", "b", uLen - plateCentre, i.Plate, plateTop, vInnerStart, vInnerEnd);

        double bearingSpan = half - plateCentre;
        var purlinU = new List<double>();
        for (int k = 1; k <= i.IntermediatePurlinsPerSide; k++)
        {
            var u = plateCentre + bearingSpan * k / (i.IntermediatePurlinsPerSide + 1);
            purlinU.Add(u);
            var top = Under(u - i.Purlin.Width / 2);
            Support("purlin", "a", u, i.Purlin, top, vInnerStart, vInnerEnd);
            Support("purlin", "b", uLen - u, i.Purlin, top, vInnerStart, vInnerEnd);
        }
        double ridgeTop = Under(half - i.Ridge.Width / 2);
        Support("ridge_beam", "ridge", half, i.Ridge, ridgeTop, vInnerStart, vInnerEnd);

        // Rafter stations along v: edge rafters flush with the roof edge, a rafter just clear of
        // each face of both gables (outside the joint trim), then interior spacing <= max.
        double clear = i.JointTrim.Width + 0.001 + i.Rafter.Width / 2;
        var fixedStations = new List<double>
        {
            vInnerStart + i.Rafter.Width / 2,
            -clear,
            i.GableWallThickness + clear,
            vLen - i.GableWallThickness - clear,
            vLen + clear,
            vInnerEnd - i.Rafter.Width / 2,
        };
        var stations = new List<double>();
        for (int k = 0; k + 1 < fixedStations.Count; k++)
        {
            double a = fixedStations[k], b = fixedStations[k + 1];
            if (b - a < i.Rafter.Width + 1e-6) continue;
            var n = Math.Max(1, (int)Math.Ceiling((b - a) / i.RafterMaxSpacing - 1e-9));
            // Over a gable wall band keep only the two bracketing rafters.
            if (k == 1 || k == 3) n = 1;
            for (int j = 0; j < n; j++) stations.Add(a + (b - a) * j / n);
        }
        stations.Add(fixedStations[^1]);
        stations = stations.Distinct().OrderBy(s => s).ToList();
        var spacings = stations.Zip(stations.Skip(1), (a, b) => b - a).ToList();
        if (spacings.Any(s => s < i.Rafter.Width + 0.02))
            warnings.Add("gable_overhang_too_short_for_edge_rafter_layout");

        // Rafters: underside line from the eave tail to the ridge line. The square end cut keeps the
        // top corner on the eave board inner face (or the roof edge without boards); the plumb cut
        // itself is not modelled. Ridge ends meet on the ridge centre line.
        double tailU = uFasciaInner + i.Rafter.Height * Math.Sin(i.PitchDegrees * Math.PI / 180);
        foreach (var v in stations)
        {
            members.Add(new Member("rafter", "a", Map(tailU, v, Under(tailU)), Map(half, v, Under(half)), i.Rafter.Width, i.Rafter.Height, "bottom"));
            members.Add(new Member("rafter", "b", Map(uLen - tailU, v, Under(tailU)), Map(half, v, Under(half)), i.Rafter.Width, i.Rafter.Height, "bottom"));
        }

        // Blocking between rafters above each plate, inside the wall length (closes the eave void).
        if (i.Blocking)
        {
            var inWall = stations.Where(s => s > -clear - 1e-9 && s < vLen + clear + 1e-9).ToList();
            // "outer": board in the wall outer face plane, top at its inner edge so the covering
            // trims the chamfer; "center": on the plate axis, top just under the boarding.
            double blockU = plateOuter ? i.BlockingThickness / 2 : plateCentre;
            double blockTop = plateOuter ? Boards(i.BlockingThickness) : Boards(plateCentre - i.BlockingThickness / 2) - 0.002;
            double blockHeight = blockTop - plateTop;
            for (int k = 0; k + 1 < inWall.Count; k++)
            {
                double a = inWall[k] + i.Rafter.Width / 2, b = inWall[k + 1] - i.Rafter.Width / 2;
                if (b - a < 0.05) continue;
                members.Add(new Member("blocking", "a", Map(blockU, a, blockTop), Map(blockU, b, blockTop), i.BlockingThickness, blockHeight, "top", plateOuter));
                members.Add(new Member("blocking", "b", Map(uLen - blockU, a, blockTop), Map(uLen - blockU, b, blockTop), i.BlockingThickness, blockHeight, "top", plateOuter));
            }
        }

        // Fascia boards: eave boards along v under the boarding edge; verge boards along the slope.
        if (i.FasciaBoards)
        {
            // Eave board: top at its inner face under the boarding (covering trims the outer chamfer),
            // bottom at the rafter underside when auto; runs between the verge boards' outer faces.
            double uEave = uFasciaOuter + fw / 2;
            double eaveTop = Boards(uFasciaInner);
            double eaveHeight = i.FasciaAutoHeight ? rafterVertical : i.Fascia.Height;
            members.Add(new Member("fascia_eave", "a", Map(uEave, vStart + recess, eaveTop), Map(uEave, vEnd - recess, eaveTop), fw, eaveHeight, "top", true));
            members.Add(new Member("fascia_eave", "b", Map(uLen - uEave, vStart + recess, eaveTop), Map(uLen - uEave, vEnd - recess, eaveTop), fw, eaveHeight, "top", true));
            // Verge boards along the slope, top face in the boarding underside plane, from behind the eave boards.
            double vergeHeight = i.FasciaAutoHeight ? i.Rafter.Height : i.Fascia.Height;
            foreach (var (v, name) in new[] { (vStart + recess + fw / 2, "verge_start"), (vEnd - recess - fw / 2, "verge_end") })
            {
                members.Add(new Member("fascia_verge", name + "_a", Map(uFasciaInner, v, Boards(uFasciaInner)), Map(half, v, Boards(half)), fw, vergeHeight, "top"));
                members.Add(new Member("fascia_verge", name + "_b", Map(uLen - uFasciaInner, v, Boards(uFasciaInner)), Map(half, v, Boards(half)), fw, vergeHeight, "top"));
            }
        }

        // Gables: masonry from MasonryTop to boarding underside minus joint gap, between plates.
        var gables = new List<Gable>();
        double gU0 = plateU0 + i.Plate.Width + i.PurlinPocketClearance;
        double gU1 = uLen - gU0;
        foreach (var (name, vFace, trimV) in new[]
                 { ("start", 0.0, i.GableWallThickness + i.JointTrim.Width / 2), ("end", vLen - i.GableWallThickness, vLen - i.GableWallThickness - i.JointTrim.Width / 2) })
        {
            double vCentre = vFace + i.GableWallThickness / 2;
            double TopAt(double u) => Boards(u) - i.JointGap;
            var profile = new List<P3>
            {
                Map(gU0, vCentre, i.MasonryTop), Map(gU1, vCentre, i.MasonryTop),
                Map(gU1, vCentre, TopAt(gU1)), Map(half, vCentre, TopAt(half)), Map(gU0, vCentre, TopAt(gU0)),
            };
            var openings = new List<Box>();
            void Pocket(string role, double uc, Section s, double top)
            {
                double c = i.PurlinPocketClearance;
                openings.Add(new Box(role, Map(uc - s.Width / 2 - c, vFace, top - s.Height - c), Map(uc + s.Width / 2 + c, vFace + i.GableWallThickness, top + c)));
                if (top + c > TopAt(uc - s.Width / 2 - c) && role != "ridge_beam")
                    warnings.Add($"pocket_reaches_gable_top_{role}");
            }
            foreach (var u in purlinU)
            {
                var top = Under(u - i.Purlin.Width / 2);
                Pocket("purlin", u, i.Purlin, top); Pocket("purlin", uLen - u, i.Purlin, top);
            }
            Pocket("ridge_beam", half, i.Ridge, ridgeTop);
            gables.Add(new Gable(name, vCentre, profile, openings));

            // Perimeter joint trim (mata-junta) under the boarding along the inner gable face.
            double tz(double u) => Boards(u);
            members.Add(new Member("joint_trim", name + "_a", Map(gU0 - i.PurlinPocketClearance, trimV, tz(gU0 - i.PurlinPocketClearance)), Map(half, trimV, tz(half)), i.JointTrim.Width, i.JointTrim.Height, "top"));
            members.Add(new Member("joint_trim", name + "_b", Map(uLen - gU0 + i.PurlinPocketClearance, trimV, tz(gU0 - i.PurlinPocketClearance)), Map(half, trimV, tz(half)), i.JointTrim.Width, i.JointTrim.Height, "top"));
        }

        double maxRafterSpan = Math.Max(purlinU.DefaultIfEmpty(half).First() - plateCentre, half - purlinU.DefaultIfEmpty(plateCentre).Last());
        double coveringThickness = i.Boarding + i.Membrane + i.Battens + i.Tile;
        var (fx0, fy0) = alongY ? (i.X0 - i.EaveOverhang, i.Y0 - i.GableOverhang) : (i.X0 - i.GableOverhang, i.Y0 - i.EaveOverhang);
        var (fx1, fy1) = alongY ? (i.X1 + i.EaveOverhang, i.Y1 + i.GableOverhang) : (i.X1 + i.GableOverhang, i.Y1 + i.EaveOverhang);
        warnings.Add("layout_only_sections_not_designed");
        return new Plan(members, gables, fx0, fy0, fx1, fy1,
            Boards(-i.EaveOverhang), coveringThickness, Boards(half), Under(half),
            stations, spacings.Max(), maxRafterSpan, warnings,
            new EaveInfo(uFasciaOuter, Boards(uFasciaOuter), Under(uFasciaInner)));
    }

    static void Validate(Input i)
    {
        void Positive(double v, string n) { if (!(v > 0) || !double.IsFinite(v)) throw new ArgumentException($"{n} must be > 0."); }
        Positive(i.X1 - i.X0, "x span"); Positive(i.Y1 - i.Y0, "y span");
        if (!(i.PitchDegrees > 5 && i.PitchDegrees < 75)) throw new ArgumentException("pitchDegrees must be within 5..75.");
        foreach (var (v, n) in new[] { (i.EaveWallThickness, "eaveWallThickness"), (i.GableWallThickness, "gableWallThickness"),
                     (i.Plate.Width, "plate.width"), (i.Plate.Height, "plate.height"), (i.Purlin.Width, "purlin.width"), (i.Purlin.Height, "purlin.height"),
                     (i.Ridge.Width, "ridge.width"), (i.Ridge.Height, "ridge.height"), (i.Rafter.Width, "rafter.width"), (i.Rafter.Height, "rafter.height"),
                     (i.RafterMaxSpacing, "rafterMaxSpacing"), (i.Boarding, "boarding"), (i.Fascia.Width, "fascia.width"), (i.Fascia.Height, "fascia.height"),
                     (i.JointTrim.Width, "jointTrim.width"), (i.JointTrim.Height, "jointTrim.height"), (i.BlockingThickness, "blockingThickness") })
            Positive(v, n);
        foreach (var (v, n) in new[] { (i.EaveOverhang, "eaveOverhang"), (i.GableOverhang, "gableOverhang"), (i.Membrane, "membrane"),
                     (i.Battens, "battens"), (i.Tile, "tile"), (i.JointGap, "jointGap"), (i.PurlinPocketClearance, "purlinPocketClearance") })
            if (v < 0 || !double.IsFinite(v)) throw new ArgumentException($"{n} must be >= 0.");
        if (i.IntermediatePurlinsPerSide < 0 || i.IntermediatePurlinsPerSide > 4) throw new ArgumentException("intermediatePurlinsPerSide must be 0..4.");
        if (i.Plate.Width > i.EaveWallThickness) throw new ArgumentException("plate wider than the eave wall.");
        if (!i.PlateAlignment.Equals("center", StringComparison.OrdinalIgnoreCase) && !i.PlateAlignment.Equals("outer", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("plateAlignment must be center or outer.");
        if (i.FasciaRecess < 0 || !double.IsFinite(i.FasciaRecess) || i.FasciaRecess + i.Fascia.Width >= Math.Min(i.EaveOverhang, i.GableOverhang))
            throw new ArgumentException("fasciaRecess must be >= 0 and leave the board inside the overhang.");
        if (!i.RidgeAxis.Equals("x", StringComparison.OrdinalIgnoreCase) && !i.RidgeAxis.Equals("y", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ridgeAxis must be x or y.");
    }
}
