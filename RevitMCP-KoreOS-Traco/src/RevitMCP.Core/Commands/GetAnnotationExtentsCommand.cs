using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Report the 2D extents (view right/up, metres) of the annotations in a view and every pair that
/// overlaps, so a documented plan, section or facade can be checked for dimensions over texts, tags
/// over tags, and annotations outside the crop before it goes on a sheet.
///
/// Dimensions: the text box of each segment (text position ± half the value string, from the
/// dimension type's text size and the view scale) plus the dimension line itself — Revit's own
/// bounding box of a dimension is unreliable. Texts, tags, room tags and spot dimensions: their view
/// bounding box; text notes use the text box without leaders.
///
/// Params:
///   - viewId:       long, optional (defaults to active view)
///   - modelIds:     long[], optional — model elements whose view boxes are also tested against the
///                   annotations (e.g. a stair a text must not cover)
///   - minOverlapM2: number, optional — ignore overlaps smaller than this (default 0.0004 m² model)
/// </summary>
public sealed class GetAnnotationExtentsCommand : IRevitCommand
{
    public string Name => "get_annotation_extents";
    public bool IsReadOnly => true;

    private static readonly BuiltInCategory[] Categories =
    {
        BuiltInCategory.OST_Dimensions, BuiltInCategory.OST_TextNotes, BuiltInCategory.OST_SpotElevations,
        BuiltInCategory.OST_SpotSlopes, BuiltInCategory.OST_RoomTags, BuiltInCategory.OST_DoorTags,
        BuiltInCategory.OST_WindowTags, BuiltInCategory.OST_MultiCategoryTags, BuiltInCategory.OST_GenericAnnotation,
    };

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var view = SetViewDetailLevelCommand.ResolveView(doc, ctx, p);
        var minOverlap = P.DblOr(p, "minOverlapM2", 0.0004);

        var items = new List<(Element e, string kind, List<double[]> boxes)>();
        var filter = new ElementMulticategoryFilter(Categories);
        foreach (var e in new FilteredElementCollector(doc, view.Id).WherePasses(filter).WhereElementIsNotElementType())
        {
            // Text notes: the text box only — a leader's bounding box covers everything it crosses.
            var boxes = e is Dimension d ? DimensionBoxes(doc, d, view)
                : e is TextNote tn && ViewExtents.TextBox(tn, view) is double[] tb ? new List<double[]> { tb }
                : ViewExtents.Box(e, view) is double[] b ? new List<double[]> { b } : new();
            if (boxes.Count > 0) items.Add((e, e.Category?.Name ?? "", boxes));
        }
        if (p["modelIds"] is JsonArray ids)
            foreach (var n in ids)
            {
                var e = doc.GetElement(new ElementId(P.LongFrom(n, "modelIds[]")))
                    ?? throw new RevitCommandException("not_found", "modelIds must identify existing elements.");
                if (ViewExtents.Box(e, view) is double[] b) items.Add((e, "model:" + (e.Category?.Name ?? ""), new List<double[]> { b }));
            }

        double[]? crop = null;
        if (view.CropBoxActive)
        {
            var cb = view.CropBox;
            crop = ViewExtents.Of2D(new[] { cb.Min, cb.Max }.Select(q => cb.Transform.OfPoint(q))
                .Select(c => (c.DotProduct(view.RightDirection), c.DotProduct(view.UpDirection))));
        }

        var annotations = new JsonArray();
        foreach (var (e, kind, boxes) in items)
        {
            var o = new JsonObject
            {
                ["id"] = e.Id.Value,
                ["category"] = kind,
                ["boxes"] = new JsonArray(boxes.Select(b => (JsonNode?)new JsonArray(b.Select(d => (JsonNode?)d).ToArray())).ToArray()),
            };
            if (e is TextNote tn) o["text"] = tn.Text.Trim();
            if (crop is not null && !kind.StartsWith("model:") && boxes.Any(b => b[0] < crop[0] || b[1] < crop[1] || b[2] > crop[2] || b[3] > crop[3]))
                o["outsideCrop"] = true;
            annotations.Add(o);
        }

        var overlaps = new JsonArray();
        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
            {
                if (items[i].kind.StartsWith("model:") && items[j].kind.StartsWith("model:")) continue;
                var area = items[i].boxes.SelectMany(a => items[j].boxes.Select(b => Overlap(a, b))).DefaultIfEmpty(0).Max();
                if (area >= minOverlap)
                    overlaps.Add(new JsonObject { ["a"] = items[i].e.Id.Value, ["aCategory"] = items[i].kind,
                        ["b"] = items[j].e.Id.Value, ["bCategory"] = items[j].kind, ["areaM2"] = Math.Round(area, 4) });
            }

        return new JsonObject
        {
            ["viewId"] = view.Id.Value, ["viewName"] = view.Name, ["scale"] = view.Scale,
            ["crop"] = crop is null ? null : new JsonArray(crop.Select(d => (JsonNode?)d).ToArray()),
            ["count"] = annotations.Count, ["overlapCount"] = overlaps.Count,
            ["overlaps"] = overlaps, ["annotations"] = annotations,
        };
    }

    internal static double Overlap(double[] a, double[] b)
    {
        var w = Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]);
        var h = Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]);
        return w > 0 && h > 0 ? w * h : 0;
    }

    private static List<double[]> DimensionBoxes(Document doc, Dimension dim, View view)
    {
        var boxes = new List<double[]>();
        var type = doc.GetElement(dim.GetTypeId());
        var textSize = type?.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 2.5 / 304.8;
        var h = textSize * view.Scale;
        if (dim.Curve is not Line line) return boxes;
        var dir = line.Direction;
        var perp = view.ViewDirection.CrossProduct(dir).Normalize();
        (double, double) Uv(XYZ q) => (q.DotProduct(view.RightDirection), q.DotProduct(view.UpDirection));

        void Add(XYZ origin, double? value, XYZ textPos, string? valueString)
        {
            var len = value ?? 0;
            var chars = Math.Max(3, (valueString ?? "").Length);
            var half = chars * 0.8 * h / 2;
            // Text box, along the dimension line.
            boxes.Add(ViewExtents.Of2D(new[] { textPos + dir * half + perp * h / 2, textPos - dir * half - perp * h / 2,
                textPos + dir * half - perp * h / 2, textPos - dir * half + perp * h / 2 }.Select(Uv)));
            // Dimension line, as a thin strip.
            boxes.Add(ViewExtents.Of2D(new[] { origin + dir * len / 2 + perp * h * 0.15, origin - dir * len / 2 - perp * h * 0.15 }.Select(Uv)));
        }

        if (dim.Segments is { Size: > 0 } segs)
            foreach (DimensionSegment s in segs) Add(s.Origin, s.Value, s.TextPosition, s.ValueString);
        else
            Add(dim.Origin, dim.Value, dim.TextPosition, dim.ValueString);
        return boxes;
    }
}
