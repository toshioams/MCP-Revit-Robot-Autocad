using System;
using System.Collections.Generic;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Pure: make nearly horizontal/vertical profile edges exactly axis-aligned. Revit refuses a sketch
/// line "slightly off axis" with a warning, and the failure capture rolls the floor back (live 11/09:
/// yard paving edge from x 12.986 to 13.05 over 42 m). Only edges within maxAngleDeg are touched;
/// the edge's end vertex takes the start vertex's coordinate.
/// </summary>
public static class ProfileSnap
{
    public static int SnapNearAxis(IList<(double X, double Y)> pts, double maxAngleDeg = 0.5)
    {
        var tan = Math.Tan(maxAngleDeg * Math.PI / 180);
        int changed = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            int j = (i + 1) % pts.Count;
            var (ax, ay) = pts[i]; var (bx, by) = pts[j];
            double dx = Math.Abs(bx - ax), dy = Math.Abs(by - ay);
            if (dx > 1e-9 && dy > 1e-12 && dy / dx <= tan) { pts[j] = (bx, ay); changed++; }
            else if (dy > 1e-9 && dx > 1e-12 && dx / dy <= tan) { pts[j] = (ax, by); changed++; }
        }
        return changed;
    }
}
