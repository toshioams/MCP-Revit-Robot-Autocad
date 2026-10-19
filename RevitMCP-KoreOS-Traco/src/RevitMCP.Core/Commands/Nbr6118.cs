using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Minimum detailing rules of ABNT NBR 6118:2023 and the bar series of NBR 7480, used to
/// WARN and to compute lap lengths the user did not give. Never sizes reinforcement.
/// Units: mm and MPa.
/// </summary>
public static class Nbr6118
{
    /// <summary>NBR 7480 nominal diameters (mm).</summary>
    public static readonly double[] Ca50 = { 6.3, 8.0, 10.0, 12.5, 16.0, 20.0, 22.0, 25.0, 32.0, 40.0 };
    public static readonly double[] Ca60 = { 2.4, 3.4, 3.8, 4.2, 4.6, 5.0, 5.5, 6.0, 6.4, 7.0, 8.0, 9.5, 10.0 };

    /// <summary>Steel mass per metre (kg/m) from the nominal diameter, density 7850 kg/m3 (NBR 7480).</summary>
    public static double MassPerMetre(double diameterMm) => Math.PI * Math.Pow(diameterMm / 1000.0, 2) / 4.0 * 7850.0;

    /// <summary>"CA-50", "CA-60", "CA-25" found in a type or material name, else null.</summary>
    public static string? GradeFromName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var m = Regex.Match(name, @"CA\s*-?\s*(25|50|60)", RegexOptions.IgnoreCase);
        return m.Success ? "CA-" + m.Groups[1].Value : null;
    }

    public static double Fyk(string? grade) => grade switch { "CA-60" => 600, "CA-25" => 250, _ => 500 };

    /// <summary>Bond coefficient eta1: 2.25 ribbed (CA-50), 1.4 indented (CA-60), 1.0 smooth (CA-25).</summary>
    public static double Eta1(string? grade) => grade switch { "CA-60" => 1.4, "CA-25" => 1.0, _ => 2.25 };

    /// <summary>Design bond strength fbd (9.3.2.1).</summary>
    public static double Fbd(double fckMpa, string? grade, bool goodBond, double diameterMm)
    {
        var fctd = 0.21 * Math.Pow(fckMpa, 2.0 / 3.0) / 1.4;
        var eta2 = goodBond ? 1.0 : 0.7;
        var eta3 = diameterMm < 32 ? 1.0 : (132 - diameterMm) / 100.0;
        return Eta1(grade) * eta2 * eta3 * fctd;
    }

    /// <summary>Basic anchorage length lb (9.4.2.4), not less than 25 diameters.</summary>
    public static double BasicAnchorageMm(double diameterMm, double fckMpa, string? grade, bool goodBond)
    {
        var fyd = Fyk(grade) / 1.15;
        var lb = diameterMm / 4.0 * fyd / Fbd(fckMpa, grade, goodBond, diameterMm);
        return Math.Max(lb, 25 * diameterMm);
    }

    /// <summary>alpha0t of table 9.4 from the fraction of bars spliced in the same section.</summary>
    public static double Alpha0t(double fractionSpliced) => fractionSpliced switch
    {
        <= 0.20 => 1.2,
        <= 0.25 => 1.4,
        <= 0.34 => 1.6,
        <= 0.50 => 1.8,
        _ => 2.0,
    };

    public sealed record LapResult(double LapMm, double LbMm, double LbNecMm, double Alpha0t, double FbdMpa, double MinimumMm);

    /// <summary>
    /// Tension lap length l0t (9.5.2.2.1): alpha0t * lb,nec, with lb,nec = alpha * lb * As,calc/As,ef
    /// (straight bars alpha = 1; ratio 1 when the user gives no As,calc) and the minimum
    /// l0t,min = max(0.3 alpha0t lb, 15 phi, 200 mm). Rounded up to <paramref name="roundingMm"/>.
    /// </summary>
    public static LapResult TensionLap(double diameterMm, double fckMpa, string? grade, bool goodBond,
        double fractionSpliced, double asRatio = 1.0, double roundingMm = 50)
    {
        var lb = BasicAnchorageMm(diameterMm, fckMpa, grade, goodBond);
        var lbMin = Math.Max(Math.Max(0.3 * lb, 10 * diameterMm), 100);
        var lbNec = Math.Max(lb * asRatio, lbMin);
        var a0t = Alpha0t(fractionSpliced);
        var min = Math.Max(Math.Max(0.3 * a0t * lb, 15 * diameterMm), 200);
        var lap = Math.Max(a0t * lbNec, min);
        if (roundingMm > 0) lap = Math.Ceiling(lap / roundingMm - 1e-9) * roundingMm;
        return new LapResult(lap, lb, lbNec, a0t, Fbd(fckMpa, grade, goodBond, diameterMm), min);
    }

    /// <summary>Minimum internal bend diameter (tables 9.1 hooks/bends and 9.2 stirrups).</summary>
    public static double MinBendDiameterMm(double diameterMm, string? grade, bool stirrup)
    {
        if (stirrup) return diameterMm <= 10 ? 3 * diameterMm : diameterMm < 20 ? 4.5 * diameterMm : 5 * diameterMm;
        return grade switch
        {
            "CA-60" => 6 * diameterMm,
            "CA-25" => (diameterMm < 20 ? 4 : 5) * diameterMm,
            _ => (diameterMm < 20 ? 5 : 8) * diameterMm,
        };
    }

    /// <summary>
    /// Nominal cover (table 7.2, delta c = 10 mm) for slab | beam | column | soil (elements in
    /// contact with soil) and aggressiveness class I..IV. Rigorous execution control: minus 5 mm.
    /// </summary>
    public static double NominalCoverMm(string element, int caa, bool rigorousControl = false)
    {
        if (caa < 1 || caa > 4) throw new ArgumentOutOfRangeException(nameof(caa), "CAA must be 1..4.");
        int[] row = element.ToLowerInvariant() switch
        {
            "slab" or "laje" => new[] { 20, 25, 35, 45 },
            "beam" or "viga" or "column" or "pilar" or "wall" or "parede" => new[] { 25, 30, 40, 50 },
            "soil" or "solo" or "foundation" or "fundacao" => new[] { 30, 30, 40, 50 },
            _ => throw new ArgumentException("element: slab | beam | column | wall | soil.", nameof(element)),
        };
        return row[caa - 1] - (rigorousControl ? 5 : 0);
    }

    /// <summary>Every cover a template should offer for CAA I..IV, with and without rigorous control.</summary>
    public static IReadOnlyCollection<double> AllNominalCoversMm()
    {
        var set = new SortedSet<double>();
        foreach (var e in new[] { "slab", "beam", "soil" })
            for (var c = 1; c <= 4; c++) { set.Add(NominalCoverMm(e, c)); set.Add(NominalCoverMm(e, c, true)); }
        return set;
    }
}
