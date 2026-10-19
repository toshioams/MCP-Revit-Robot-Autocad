using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Create a Ceiling from a closed polygonal profile.
///
/// Params:
///   - profile:        [{x,y,z?}, ...], >=3 points
///   - levelName:      string, optional
///   - ceilingTypeName: string, optional
///   - units:          "meters"|"feet"
/// </summary>
public sealed class CreateCeilingCommand : IRevitCommand
{
    public string Name => "create_ceiling";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);

        double? requestedOffset = p["heightOffset"] is null ? null
            : P.Dbl(p, "heightOffset") * (units == "feet" ? 1.0 : P.MetersToFeet);
        var pts = P.XyzList(p, "profile", units);
        if (pts.Count < 3)
            throw new RevitCommandException("invalid_parameter", "Ceiling profile must have at least 3 points.");

        var gap = p["perimeterGap"] is null ? 0 : P.Dbl(p, "perimeterGap") * (units == "feet" ? 1 : P.MetersToFeet);
        if (!double.IsFinite(gap) || gap < 0)
            throw new RevitCommandException("invalid_parameter", "perimeterGap must be finite and nonnegative.");
        if (gap > 0)
        {
            // Deliberately bounded to a simple axis-aligned rectangle; no guessed polygon offsets.
            var minX=pts.Min(v=>v.X); var maxX=pts.Max(v=>v.X);
            var minY=pts.Min(v=>v.Y); var maxY=pts.Max(v=>v.Y);
            var corners=pts.Select(v=>(v.X,v.Y)).Distinct().Count();
            for (var i=0;i<pts.Count;i++)
            {
                var next=pts[(i+1)%pts.Count];
                if (System.Math.Abs(next.X-pts[i].X)>1e-7 && System.Math.Abs(next.Y-pts[i].Y)>1e-7)
                    throw new RevitCommandException("unsupported_profile", "Rectangle corners must follow perimeter order.");
            }
            if (pts.Count!=4 || corners!=4 || pts.Any(v=>
                (System.Math.Abs(v.X-minX)>1e-7 && System.Math.Abs(v.X-maxX)>1e-7) ||
                (System.Math.Abs(v.Y-minY)>1e-7 && System.Math.Abs(v.Y-maxY)>1e-7))
                || 2*gap>=maxX-minX || 2*gap>=maxY-minY)
                throw new RevitCommandException("unsupported_profile", "perimeterGap requires a nondegenerate axis-aligned rectangle.");
            for (var i=0;i<pts.Count;i++)
                pts[i]=new XYZ(pts[i].X+(System.Math.Abs(pts[i].X-minX)<1e-7?gap:-gap),
                    pts[i].Y+(System.Math.Abs(pts[i].Y-minY)<1e-7?gap:-gap),pts[i].Z);
        }

        var level = CreateWallCommand.ResolveLevel(doc, P.StrOrNull(p, "levelName"));
        var ceilingType = ResolveCeilingType(doc, P.StrOrNull(p, "ceilingTypeName"));

        var loop = new CurveLoop();
        for (var i = 0; i < pts.Count; i++)
        {
            var a = new XYZ(pts[i].X, pts[i].Y, level.Elevation);
            var bRaw = pts[(i + 1) % pts.Count];
            var b = new XYZ(bRaw.X, bRaw.Y, level.Elevation);
            if (a.DistanceTo(b) < 1e-6)
                throw new RevitCommandException("invalid_parameter", $"Profile segment {i} has zero length.");
            loop.Append(Line.CreateBound(a, b));
        }

        var ceiling = Ceiling.Create(doc, new List<CurveLoop> { loop }, ceilingType.Id, level.Id);
        var offset = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
        if (requestedOffset.HasValue)
        {
            if (offset is null || offset.IsReadOnly || !offset.Set(requestedOffset.Value))
                throw new RevitCommandException("height_offset_failed", "Cannot set the ceiling height offset.");
            doc.Regenerate();
            if (System.Math.Abs(offset.AsDouble() - requestedOffset.Value) > 1e-6)
                throw new RevitCommandException("height_offset_failed", "Revit did not retain the requested ceiling height offset.");
        }

        return new JsonObject
        {
            ["id"] = ceiling.Id.Value,
            ["perimeterGapMeters"] = gap / P.MetersToFeet,
            ["perimeterGapNote"] = "Geometric setback only; no perimeter trim or movement-joint system is created.",
            ["levelName"] = level.Name,
            ["ceilingTypeName"] = ceilingType.Name,
            ["heightOffsetFeet"] = offset?.AsDouble(),
            ["heightOffsetMeters"] = offset is null ? null : offset.AsDouble() / P.MetersToFeet,
        };
    }

    private static CeilingType ResolveCeilingType(Document doc, string? name)
    {
        var query = new FilteredElementCollector(doc)
            .OfClass(typeof(CeilingType))
            .Cast<CeilingType>();
        if (string.IsNullOrWhiteSpace(name))
            return query.FirstOrDefault()
                ?? throw new RevitCommandException("not_found", "No CeilingType in the document.");
        return query.FirstOrDefault(t => t.Name == name)
            ?? throw new RevitCommandException("not_found", $"CeilingType '{name}' not found.");
    }
}
