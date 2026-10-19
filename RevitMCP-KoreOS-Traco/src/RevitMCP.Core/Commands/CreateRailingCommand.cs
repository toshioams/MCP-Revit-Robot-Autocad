using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Create a railing along an open polyline (meters): balcony guards, the handrail of an entrance stair or a ramp.
///
/// Params:
///   - path: [{x,y}, ...] at least 2 points, in order (the path is not closed)
///   - levelName: string, required
///   - railingTypeName | railingTypeId: optional, default the first railing type
///   - baseOffset: meters above the level (default 0)
///   - hostId: optional floor/stairs/ramp element: the railing follows its top (sloped ramps and stairs)
///   - flip: bool, swap the side of the path the railing sits on
/// </summary>
public sealed class CreateRailingCommand : IRevitCommand
{
    public string Name => "create_railing";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var level = CreateWallCommand.ResolveLevel(doc, P.Str(p, "levelName"));
        var pts = P.XyzList(p, "path", "meters");
        if (pts.Count < 2)
            throw new RevitCommandException("invalid_parameter", "path needs at least 2 points.");

        var types = new FilteredElementCollector(doc).OfClass(typeof(RailingType)).Cast<RailingType>().ToList();
        RailingType type;
        if (p["railingTypeId"] is not null)
            type = doc.GetElement(new ElementId(P.Long(p, "railingTypeId"))) as RailingType
                ?? throw new RevitCommandException("invalid_parameter", "railingTypeId must identify a RailingType.");
        else
        {
            var name = P.StrOrNull(p, "railingTypeName");
            type = (string.IsNullOrWhiteSpace(name) ? types.FirstOrDefault() : types.FirstOrDefault(t => t.Name == name))
                ?? throw new RevitCommandException("not_found", $"Railing type '{name}' not found. Available: {string.Join(", ", types.Select(t => t.Name))}.");
        }

        var loop = new CurveLoop();
        for (var i = 1; i < pts.Count; i++)
        {
            var a = new XYZ(pts[i - 1].X, pts[i - 1].Y, level.Elevation);
            var b = new XYZ(pts[i].X, pts[i].Y, level.Elevation);
            if (a.DistanceTo(b) < 1e-3)
                throw new RevitCommandException("invalid_parameter", $"path segment {i - 1} has zero length.");
            loop.Append(Line.CreateBound(a, b));
        }

        var railing = Railing.Create(doc, loop, type.Id, level.Id);
        var offset = P.DblOr(p, "baseOffset", 0) * P.MetersToFeet;
        if (System.Math.Abs(offset) > 1e-9)
        {
            var off = railing.get_Parameter(BuiltInParameter.STAIRS_RAILING_HEIGHT_OFFSET)
                ?? throw new RevitCommandException("command_failed", "Railing has no base offset parameter.");
            off.Set(offset);
        }
        if (p["hostId"] is not null)
        {
            var hostId = new ElementId(P.Long(p, "hostId"));
            try { railing.HostId = hostId; }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                throw new RevitCommandException("invalid_parameter", $"hostId {hostId.Value} cannot host this railing: {ex.Message}");
            }
        }
        if (P.BoolOr(p, "flip", false)) railing.Flip();
        doc.Regenerate();
        var box = railing.get_BoundingBox(null);
        return new JsonObject
        {
            ["id"] = railing.Id.Value,
            ["railingTypeName"] = type.Name,
            ["levelName"] = level.Name,
            ["segments"] = pts.Count - 1,
            ["hostId"] = railing.HostId?.Value,
            ["bottomElevationMeters"] = box is null ? null : System.Math.Round(box.Min.Z / P.MetersToFeet, 3),
            ["topElevationMeters"] = box is null ? null : System.Math.Round(box.Max.Z / P.MetersToFeet, 3),
        };
    }
}
