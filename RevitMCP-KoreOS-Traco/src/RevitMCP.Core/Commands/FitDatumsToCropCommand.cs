using System;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Trim the levels and grids visible in a view to its crop region (view-specific 2D extents), so a
/// section or elevation placed on a sheet is not stretched by datum lines that run far beyond the drawing.
///
/// Params:
///   - viewId:       long, optional (defaults to active view)
///   - insetMeters:  double, optional — distance kept inside the crop at each end (default 0.3)
/// </summary>
public sealed class FitDatumsToCropCommand : IRevitCommand
{
    public string Name => "fit_datums_to_crop";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var view = SetViewDetailLevelCommand.ResolveView(doc, ctx, p);
        if (view.IsTemplate || !view.CropBoxActive)
            throw new RevitCommandException("invalid_parameter", "The view must be a non-template view with an active crop region.");
        var inset = P.DblOr(p, "insetMeters", 0.3) * P.MetersToFeet;
        var box = view.CropBox;
        var corners = new[] { box.Min.X, box.Max.X }.SelectMany(x => new[] { box.Min.Y, box.Max.Y }
            .SelectMany(y => new[] { box.Min.Z, box.Max.Z }.Select(z => box.Transform.OfPoint(new XYZ(x, y, z))))).ToList();

        var results = new JsonArray();
        var datums = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType()
            .Where(e => e is Level || e is Grid).Cast<DatumPlane>().ToList();
        foreach (var datum in datums)
        {
            datum.SetDatumExtentType(DatumEnds.End0, view, DatumExtentType.ViewSpecific);
            datum.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);
            var curves = datum.GetCurvesInView(DatumExtentType.ViewSpecific, view);
            if (curves.Count != 1 || curves[0] is not Line line)
            {
                results.Add(new JsonObject { ["id"] = datum.Id.Value, ["name"] = datum.Name, ["skipped"] = "not a single straight line in this view" });
                continue;
            }
            var origin = line.GetEndPoint(0);
            var dir = line.Direction;
            var ts = corners.Select(c => (c - origin).DotProduct(dir)).ToList();
            double t0 = ts.Min() + inset, t1 = ts.Max() - inset;
            if (t1 - t0 < 1e-3)
            {
                results.Add(new JsonObject { ["id"] = datum.Id.Value, ["name"] = datum.Name, ["skipped"] = "crop too narrow along the datum" });
                continue;
            }
            datum.SetCurveInView(DatumExtentType.ViewSpecific, view, Line.CreateBound(origin + dir * t0, origin + dir * t1));
            results.Add(new JsonObject { ["id"] = datum.Id.Value, ["name"] = datum.Name, ["lengthMeters"] = Math.Round((t1 - t0) / P.MetersToFeet, 3) });
        }
        return new JsonObject { ["viewId"] = view.Id.Value, ["viewName"] = view.Name, ["datums"] = results };
    }
}
