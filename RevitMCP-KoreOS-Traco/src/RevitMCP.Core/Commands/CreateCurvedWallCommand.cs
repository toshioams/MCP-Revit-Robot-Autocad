using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Creates a curved wall on a circle: an arc (startAngleDeg..endAngleDeg) or, for 360 degrees, two
/// half-circle walls (Revit walls cannot be closed curves). The radius refers to the centerline,
/// interior or exterior face; the result is verified on the centerline after creation.
///
/// Params: center {x,y}, radius, radiusReference centerline|interior|exterior, startAngleDeg (0),
/// endAngleDeg (360), height (3), baseOffset (0), levelName, wallTypeName | wallTypeId,
/// structural (true), units.
/// </summary>
public sealed class CreateCurvedWallCommand : IRevitCommand
{
    public string Name => "create_curved_wall";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);
        var toFeet = units == "feet" ? 1.0 : P.MetersToFeet;

        var center = P.Xyz(p, "center", units);
        var radius = P.Dbl(p, "radius") * toFeet;
        var height = P.DblOr(p, "height", 3.0) * toFeet;
        var baseOffset = P.DblOr(p, "baseOffset", 0) * toFeet;
        var a0 = P.DblOr(p, "startAngleDeg", 0) * Math.PI / 180;
        var a1 = P.DblOr(p, "endAngleDeg", 360) * Math.PI / 180;
        if (radius <= 0 || height <= 0) throw new RevitCommandException("invalid_parameter", "radius and height must be positive.");
        if (a1 <= a0 || a1 - a0 > 2 * Math.PI + 1e-9) throw new RevitCommandException("invalid_parameter", "endAngleDeg must be greater than startAngleDeg, at most 360 degrees apart.");

        var level = CreateWallCommand.ResolveLevel(doc, P.StrOrNull(p, "levelName"));
        var wallType = p["wallTypeId"] is not null
            ? doc.GetElement(new ElementId(P.Long(p, "wallTypeId"))) as WallType
                ?? throw new RevitCommandException("invalid_parameter", "wallTypeId must identify a WallType.")
            : CreateWallCommand.ResolveWallType(doc, P.StrOrNull(p, "wallTypeName"));
        var structural = P.BoolOr(p, "structural", true);
        var halfWidth = wallType.Width / 2;
        var centerline = (P.StrOrNull(p, "radiusReference") ?? "centerline").ToLowerInvariant() switch
        {
            "centerline" => radius,
            "interior" => radius + halfWidth,
            "exterior" => radius - halfWidth,
            _ => throw new RevitCommandException("invalid_parameter", "radiusReference: centerline | interior | exterior."),
        };
        if (centerline - halfWidth <= 0) throw new RevitCommandException("invalid_parameter", "Radius smaller than half the wall thickness.");

        var full = a1 - a0 >= 2 * Math.PI - 1e-9;
        var pieces = full ? new[] { (a0, a0 + Math.PI), (a0 + Math.PI, a0 + 2 * Math.PI) } : new[] { (a0, a1) };
        var c = new XYZ(center.X, center.Y, level.Elevation);

        var walls = new List<Wall>();
        foreach (var (s, e) in pieces)
        {
            var wall = Create(doc, c, centerline, s, e, wallType, level, height, baseOffset, structural);
            // The template's default location line may not be the centerline: switch it, measure where the
            // centerline landed and, if it moved, rebuild with the radius corrected by the same offset.
            var drift = CenterlineRadius(doc, wall) - centerline;
            if (Math.Abs(drift) > 1e-4)
            {
                doc.Delete(wall.Id);
                wall = Create(doc, c, centerline - drift, s, e, wallType, level, height, baseOffset, structural);
                if (Math.Abs(CenterlineRadius(doc, wall) - centerline) > 1e-3)
                    throw new RevitCommandException("verification_failed", "Wall centerline radius does not match after creation.");
            }
            walls.Add(wall);
        }
        if (walls.Count == 2)
        {
            WallUtils.AllowWallJoinAtEnd(walls[0], 0); WallUtils.AllowWallJoinAtEnd(walls[0], 1);
            WallUtils.AllowWallJoinAtEnd(walls[1], 0); WallUtils.AllowWallJoinAtEnd(walls[1], 1);
            doc.Regenerate();
        }

        var f = 1 / toFeet;
        return new JsonObject
        {
            ["wallIds"] = new JsonArray(walls.Select(w => (JsonNode)w.Id.Value).ToArray()),
            ["wallTypeName"] = wallType.Name,
            ["levelName"] = level.Name,
            ["structural"] = structural,
            ["centerlineRadius"] = Math.Round(centerline * f, 4),
            ["interiorDiameter"] = Math.Round(2 * (centerline - halfWidth) * f, 4),
            ["exteriorDiameter"] = Math.Round(2 * (centerline + halfWidth) * f, 4),
            ["thickness"] = Math.Round(wallType.Width * f, 4),
            ["height"] = Math.Round(height * f, 4),
            ["sweepDeg"] = Math.Round((a1 - a0) * 180 / Math.PI, 3),
            ["units"] = units,
            ["note"] = full ? "Circle made of two half walls (Revit walls cannot be closed)." : null,
        };
    }

    static Wall Create(Document doc, XYZ c, double r, double s, double e, WallType type, Level level, double height, double offset, bool structural)
    {
        var arc = Arc.Create(c, r, s, e, XYZ.BasisX, XYZ.BasisY);
        var wall = Wall.Create(doc, arc, type.Id, level.Id, height, offset, false, structural);
        var keyRef = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
        if (keyRef is not null && !keyRef.IsReadOnly && keyRef.AsInteger() != (int)WallLocationLine.WallCenterline)
            keyRef.Set((int)WallLocationLine.WallCenterline);
        doc.Regenerate();
        return wall;
    }

    static double CenterlineRadius(Document doc, Wall wall) =>
        wall.Location is LocationCurve { Curve: Arc a } ? a.Radius
            : throw new RevitCommandException("command_failed", "Curved wall lost its arc location.");
}

/// <summary>
/// UI action: create a project from a template (.rte), save it to saveAsPath and, by default,
/// open it in a window. The template file itself is never modified.
/// </summary>
public sealed class NewProjectFromTemplateCommand : IRevitCommand
{
    public string Name => "new_project_from_template";
    public bool IsReadOnly => false;
    public ExecutionKind Execution => ExecutionKind.UiAction;
    public string RiskLevel => "low";

    public JsonNode? Execute(CommandContext ctx)
    {
        var p = ctx.Parameters;
        var template = System.IO.Path.GetFullPath(P.Str(p, "templatePath"));
        var target = System.IO.Path.GetFullPath(P.Str(p, "saveAsPath"));
        var overwrite = P.BoolOr(p, "overwrite", false);
        var activate = P.BoolOr(p, "activate", true);
        if (!template.EndsWith(".rte", StringComparison.OrdinalIgnoreCase))
            throw new RevitCommandException("invalid_parameter", "templatePath must be a .rte file.");
        if (!System.IO.File.Exists(template)) throw new RevitCommandException("not_found", $"Template not found: {template}");
        if (!target.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            throw new RevitCommandException("invalid_parameter", "saveAsPath must end with .rvt.");
        if (System.IO.File.Exists(target) && !overwrite)
            throw new RevitCommandException("name_collision", $"{target} exists. Pass overwrite:true to replace it.");
        if (ctx.DryRun)
            return new JsonObject { ["dryRun"] = true, ["templatePath"] = template, ["saveAsPath"] = target };

        var created = ctx.App.Application.NewProjectDocument(template);
        created.SaveAs(target, new SaveAsOptions { OverwriteExistingFile = overwrite });
        var title = created.Title;
        if (activate) ctx.App.OpenAndActivateDocument(target);

        return new JsonObject
        {
            ["title"] = title,
            ["pathName"] = target,
            ["templatePath"] = template,
            ["activated"] = activate,
            ["note"] = activate ? null : "Open without a window: target it with documentTitle.",
        };
    }
}
