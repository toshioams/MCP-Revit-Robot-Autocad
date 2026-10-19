using System;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Create a TextNote in a view, optionally with a type, alignment, rotation and leaders
/// (material callouts in sections, facades and roof plans).
///
/// Params:
///   - text:     string, required
///   - location: { x, y, z? }, required — text origin (projected onto the view plane)
///   - viewId:   long, optional (defaults to active view)
///   - width:    number, optional — wrap width ON PAPER in the input units (Revit's TextNote width is
///               paper space): 0.06 m = 60 mm, whatever the view scale. Default 0.06 m. A model-sized
///               width (0.5 "m" = 500 mm of paper) produced notes that never appeared in the view.
///   - typeId | typeName: TextNoteType, optional (default: first TextNoteType)
///   - align:    "left"|"center"|"right", optional (default left)
///   - rotationDegrees: number, optional — counter-clockwise in the view plane
///   - leaders:  [{ end: {x,y,z?}, elbow?: {x,y,z?} }], optional — each leader points at end;
///               the attachment side follows the end point (left of the text → left leader)
///   - units:    "meters"|"feet"
/// Returns: { id, viewId, text, typeId, leaders, extents? } — extents is the regenerated view
/// bounding box in view coordinates (right/up, metres) for overlap checks.
/// </summary>
public sealed class CreateTextNoteCommand : IRevitCommand
{
    public string Name => "create_text_note";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);
        var scale = units == "feet" ? 1.0 : P.MetersToFeet;

        var text = P.Str(p, "text");

        var viewId = p["viewId"] is not null
            ? new ElementId(P.Long(p, "viewId"))
            : doc.ActiveView?.Id
            ?? throw new RevitCommandException("not_found", "No active view.");
        var view = doc.GetElement(viewId) as View
            ?? throw new RevitCommandException("not_found", $"View {viewId.Value} not found.");
        var location = ViewPlane.Project(view, P.Xyz(p, "location", units));

        var width = (p["width"] is null ? 0.06 * P.MetersToFeet : P.Dbl(p, "width") * scale);

        var types = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
        TextNoteType? type;
        if (P.LongOrNull(p, "typeId") is long typeId)
            type = types.FirstOrDefault(t => t.Id.Value == typeId)
                ?? throw new RevitCommandException("not_found", $"typeId {typeId} is not a TextNoteType.");
        else if (P.StrOrNull(p, "typeName") is string typeName)
            type = types.FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new RevitCommandException("not_found",
                    $"TextNoteType '{typeName}' not found. Available: {string.Join(", ", types.Select(t => t.Name).Take(40))}");
        else
            type = types.FirstOrDefault()
                ?? throw new RevitCommandException("not_found", "No TextNoteType in document.");

        double minW = TextElement.GetMinimumAllowedWidth(doc, type.Id), maxW = TextElement.GetMaximumAllowedWidth(doc, type.Id);
        if (width < minW || width > maxW)
            throw new RevitCommandException("invalid_parameter",
                $"width is paper space: {width * 304.8:0.#} mm is outside {minW * 304.8:0.#}..{maxW * 304.8:0.#} mm for this text type.");
        var options = new TextNoteOptions(type.Id)
        {
            HorizontalAlignment = (P.StrOrNull(p, "align") ?? "left").ToLowerInvariant() switch
            {
                "left" => HorizontalTextAlignment.Left,
                "center" or "centre" => HorizontalTextAlignment.Center,
                "right" => HorizontalTextAlignment.Right,
                var a => throw new RevitCommandException("invalid_parameter", $"align must be left, center or right, got '{a}'."),
            },
            Rotation = P.DblOr(p, "rotationDegrees", 0) * Math.PI / 180,
        };

        var note = TextNote.Create(doc, viewId, location, width, text, options);

        var leaders = new JsonArray();
        if (p["leaders"] is JsonArray leaderArr)
        {
            foreach (var node in leaderArr)
            {
                var lo = node as JsonObject
                    ?? throw new RevitCommandException("invalid_parameter", "Each leader must be an object with 'end'.");
                var end = ViewPlane.Project(view, P.Xyz(lo, "end", units));
                var left = (end - location).DotProduct(view.RightDirection) < 0;
                var leader = note.AddLeader(left ? TextNoteLeaderTypes.TNLT_STRAIGHT_L : TextNoteLeaderTypes.TNLT_STRAIGHT_R);
                leader.End = end;
                if (lo["elbow"] is not null)
                    leader.Elbow = ViewPlane.Project(view, P.Xyz(lo, "elbow", units));
                leaders.Add(left ? "left" : "right");
            }
        }
        else if (p["leaders"] is not null)
            throw new RevitCommandException("invalid_parameter", "leaders must be an array.");

        doc.Regenerate();
        var extents = ViewExtents.Of(note, view);
        if (extents is null)
            throw new RevitCommandException("verification_failed", "The text note has no extents in the view (not visible); check width and view.");
        return new JsonObject
        {
            ["id"] = note.Id.Value,
            ["viewId"] = viewId.Value,
            ["text"] = text,
            ["typeId"] = type.Id.Value,
            ["leaders"] = leaders,
            ["extents"] = extents,
            ["textBox"] = ViewExtents.TextBox(note, view) is double[] tb ? new JsonArray(tb.Select(d => (JsonNode?)d).ToArray()) : null,
        };
    }
}

/// <summary>Bounding box of an element in a view's 2D coordinates (right/up, metres).</summary>
internal static class ViewExtents
{
    public static double[]? Box(Element e, View view)
    {
        var bb = e.get_BoundingBox(view);
        if (bb is null) return null;
        var corners = new[] { bb.Min.X, bb.Max.X }.SelectMany(x => new[] { bb.Min.Y, bb.Max.Y }
            .SelectMany(y => new[] { bb.Min.Z, bb.Max.Z }.Select(z => bb.Transform.OfPoint(new XYZ(x, y, z))))).ToList();
        return Of2D(corners.Select(c => (c.DotProduct(view.RightDirection), c.DotProduct(view.UpDirection))));
    }

    public static double[] Of2D(System.Collections.Generic.IEnumerable<(double u, double v)> pts)
    {
        var l = pts.ToList();
        return new[] { l.Min(q => q.u), l.Min(q => q.v), l.Max(q => q.u), l.Max(q => q.v) }
            .Select(d => Math.Round(d * P.FeetToMeters, 3)).ToArray();
    }

    /// <summary>Box of the text itself (without leaders): coord, paper width/height times the view scale.</summary>
    public static double[]? TextBox(TextNote note, View view)
    {
        double w = note.Width * view.Scale, h = note.Height * view.Scale;
        if (w <= 0 || h <= 0) return null;
        var right = note.BaseDirection; var up = note.UpDirection; var c = note.Coord;
        double a = note.HorizontalAlignment switch { HorizontalTextAlignment.Center => -w / 2, HorizontalTextAlignment.Right => -w, _ => 0 };
        var pts = new[] { c + right * a, c + right * (a + w), c + right * a - up * h, c + right * (a + w) - up * h };
        return Of2D(pts.Select(q => (q.DotProduct(view.RightDirection), q.DotProduct(view.UpDirection))));
    }

    public static JsonNode? Of(Element e, View view) =>
        Box(e, view) is double[] b ? new JsonArray(b.Select(d => (JsonNode?)d).ToArray()) : null;
}
