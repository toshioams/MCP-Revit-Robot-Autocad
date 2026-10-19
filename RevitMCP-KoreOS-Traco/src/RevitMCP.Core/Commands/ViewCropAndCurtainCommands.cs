using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Crop a plan view to a model rectangle (meters), or clear the crop. Needed to check details in
/// get_view_image: the whole Ford site exported at 1600 px left a 60 cm door invisible (11/09/2026).
/// Refuses views whose crop is driven by a scope box.
/// Sections and elevations: viewRectMeters {minU,minV,maxU,maxV} in view coordinates (right/up, the
/// same absolute values get_annotation_extents reports), keeping the far clip depth — callouts and
/// dimension chains beside a section need room the original crop did not leave.
/// </summary>
public sealed class SetViewCropCommand : IRevitCommand
{
    public string Name => "set_view_crop";
    public bool IsReadOnly => false;
    public string RiskLevel => "medium";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var view = doc.GetElement(new ElementId(P.Long(p, "viewId"))) as View
            ?? throw new RevitCommandException("invalid_parameter", "viewId must identify a view.");
        if (view is ViewSection && p["viewRectMeters"] is JsonObject vr)
            return CropSection(doc, view, vr, p);
        if (view is not ViewPlan)
            throw new RevitCommandException("unsupported_view", "Plan views take bboxMeters; sections and elevations take viewRectMeters {minU,minV,maxU,maxV}; 3D views use set_section_box.");
        var scope = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
        if (scope is not null && scope.AsElementId() != ElementId.InvalidElementId)
            throw new RevitCommandException("scope_box_controlled", "The view crop is driven by a scope box; move the scope box instead.");
        var before = Rect(view);
        if (P.BoolOr(p, "clear", false))
        {
            view.CropBoxActive = false;
            return new JsonObject { ["viewId"] = view.Id.Value, ["cropActive"] = false, ["previous"] = before };
        }
        var b = p["bboxMeters"] as JsonObject ?? throw new RevitCommandException("invalid_parameter", "Pass bboxMeters {minX,minY,maxX,maxY} or clear:true.");
        double x0 = P.Dbl(b, "minX"), y0 = P.Dbl(b, "minY"), x1 = P.Dbl(b, "maxX"), y1 = P.Dbl(b, "maxY");
        if (x1 - x0 < 0.1 || y1 - y0 < 0.1)
            throw new RevitCommandException("invalid_parameter", "bboxMeters must be at least 10 cm wide and tall.");

        var crop = view.CropBox;
        var inv = crop.Transform.Inverse;
        var corners = new[] { new XYZ(x0, y0, 0), new XYZ(x1, y0, 0), new XYZ(x1, y1, 0), new XYZ(x0, y1, 0) }
            .Select(q => inv.OfPoint(new XYZ(q.X * P.MetersToFeet, q.Y * P.MetersToFeet, 0))).ToList();
        var box = new BoundingBoxXYZ
        {
            Transform = crop.Transform,
            Min = new XYZ(corners.Min(c => c.X), corners.Min(c => c.Y), crop.Min.Z),
            Max = new XYZ(corners.Max(c => c.X), corners.Max(c => c.Y), crop.Max.Z),
        };
        view.CropBoxActive = true;
        view.CropBox = box;
        if (p["cropVisible"] is not null) view.CropBoxVisible = P.BoolOr(p, "cropVisible", false);
        doc.Regenerate();
        var after = Rect(view);
        return new JsonObject
        {
            ["viewId"] = view.Id.Value, ["viewName"] = view.Name, ["cropActive"] = view.CropBoxActive,
            ["previous"] = before, ["cropMeters"] = after,
            ["note"] = "Rotated views (true north / view rotation) crop to the model-aligned rectangle's bounds.",
        };
    }

    static JsonObject CropSection(Document doc, View view, JsonObject vr, JsonObject p)
    {
        double u0 = P.Dbl(vr, "minU"), v0 = P.Dbl(vr, "minV"), u1 = P.Dbl(vr, "maxU"), v1 = P.Dbl(vr, "maxV");
        if (u1 - u0 < 0.1 || v1 - v0 < 0.1)
            throw new RevitCommandException("invalid_parameter", "viewRectMeters must be at least 10 cm wide and tall.");
        var crop = view.CropBox;
        var t = crop.Transform;
        // Local crop coordinates are measured from the transform origin along its basis (right, up).
        double ou = t.Origin.DotProduct(t.BasisX), ov = t.Origin.DotProduct(t.BasisY);
        var before = new JsonObject { ["minU"] = Math.Round((crop.Min.X + ou) / P.MetersToFeet, 4), ["minV"] = Math.Round((crop.Min.Y + ov) / P.MetersToFeet, 4),
            ["maxU"] = Math.Round((crop.Max.X + ou) / P.MetersToFeet, 4), ["maxV"] = Math.Round((crop.Max.Y + ov) / P.MetersToFeet, 4) };
        var box = new BoundingBoxXYZ
        {
            Transform = t,
            Min = new XYZ(u0 * P.MetersToFeet - ou, v0 * P.MetersToFeet - ov, crop.Min.Z),
            Max = new XYZ(u1 * P.MetersToFeet - ou, v1 * P.MetersToFeet - ov, crop.Max.Z),
        };
        view.CropBoxActive = true;
        view.CropBox = box;
        if (p["cropVisible"] is not null) view.CropBoxVisible = P.BoolOr(p, "cropVisible", false);
        doc.Regenerate();
        var c = view.CropBox;
        var after = new JsonObject { ["minU"] = Math.Round((c.Min.X + ou) / P.MetersToFeet, 4), ["minV"] = Math.Round((c.Min.Y + ov) / P.MetersToFeet, 4),
            ["maxU"] = Math.Round((c.Max.X + ou) / P.MetersToFeet, 4), ["maxV"] = Math.Round((c.Max.Y + ov) / P.MetersToFeet, 4) };
        if (Math.Abs(after["minU"]!.GetValue<double>() - u0) > 0.01 || Math.Abs(after["maxV"]!.GetValue<double>() - v1) > 0.01)
            throw new RevitCommandException("verification_failed", "Revit did not retain the requested section crop.");
        return new JsonObject { ["viewId"] = view.Id.Value, ["viewName"] = view.Name, ["cropActive"] = true, ["previous"] = before, ["viewRectMeters"] = after };
    }

    static JsonObject Rect(View v)
    {
        var c = v.CropBox; var t = c.Transform;
        var pts = new[] { t.OfPoint(c.Min), t.OfPoint(c.Max), t.OfPoint(new XYZ(c.Min.X, c.Max.Y, c.Min.Z)), t.OfPoint(new XYZ(c.Max.X, c.Min.Y, c.Min.Z)) };
        return new JsonObject
        {
            ["active"] = v.CropBoxActive,
            ["minX"] = Math.Round(pts.Min(q => q.X) / P.MetersToFeet, 4), ["minY"] = Math.Round(pts.Min(q => q.Y) / P.MetersToFeet, 4),
            ["maxX"] = Math.Round(pts.Max(q => q.X) / P.MetersToFeet, 4), ["maxY"] = Math.Round(pts.Max(q => q.Y) / P.MetersToFeet, 4),
        };
    }
}

/// <summary>
/// Edit a curtain wall: list its panels, add vertical/horizontal grid lines through points, and change
/// the panel that contains a point to another type (a curtain door panel, a solid panel, glass).
/// Needed for the showroom entrance: the double door sits inside the glass skin, and a hosted door
/// family cannot go into a curtain wall. Panels pinned by a type-driven grid are unpinned first.
/// </summary>
public sealed class EditCurtainWallCommand : IRevitCommand
{
    public string Name => "edit_curtain_wall";
    public bool IsReadOnly => false;
    public string RiskLevel => "medium";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);
        var wall = doc.GetElement(new ElementId(P.Long(p, "wallId"))) as Wall
            ?? throw new RevitCommandException("invalid_parameter", "wallId must identify a wall.");
        var grid = wall.CurtainGrid ?? throw new RevitCommandException("unsupported_type", "The wall is not a curtain wall.");
        var result = new JsonObject { ["wallId"] = wall.Id.Value, ["wallType"] = wall.WallType.Name };

        // removeGridLines first: a type-driven grid already has lines inside a wider door opening (live
        // 11/09: entrance bay 34.85-39.62 had lines at 36.44 and 38.03, the 3.27 m door became 1.59 m).
        var removed = new JsonArray();
        if (p["removeGridLines"] is JsonArray removeLines)
        {
            foreach (var node in removeLines.OfType<JsonObject>())
            {
                var orientation = (P.StrOrNull(node, "orientation") ?? "").ToLowerInvariant();
                if (orientation is not ("vertical" or "horizontal"))
                    throw new RevitCommandException("invalid_parameter", "removeGridLines[].orientation: vertical | horizontal.");
                var point = P.Xyz(node, "point", units);
                var tol = P.DblOr(node, "toleranceMeters", 0.05) * P.MetersToFeet;
                var hit = grid.GetUGridLineIds().Concat(grid.GetVGridLineIds()).Select(id => doc.GetElement(id) as CurtainGridLine)
                    .Where(gl => gl is not null && IsVertical(gl) == (orientation == "vertical"))
                    .Select(gl => (gl: gl!, d: Horizontal(gl!.FullCurve, point, orientation == "vertical")))
                    .Where(x => x.d <= tol).OrderBy(x => x.d).Select(x => x.gl).FirstOrDefault()
                    ?? throw new RevitCommandException("not_found", $"No {orientation} grid line within tolerance of the point.");
                if (hit.Pinned) hit.Pinned = false;
                var c = hit.FullCurve;
                removed.Add(new JsonObject { ["id"] = hit.Id.Value, ["orientation"] = orientation, ["start"] = M(c.GetEndPoint(0)), ["end"] = M(c.GetEndPoint(1)) });
                try { doc.Delete(hit.Id); }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    throw new RevitCommandException("type_driven_grid_line",
                        "This grid line comes from the curtain wall type's layout and cannot be deleted. Duplicate the type, set its layout to none (set_parameter SPACING_LAYOUT_VERT / SPACING_LAYOUT_HORIZ = 0), change this wall to it and add the lines you need.");
                }
            }
            doc.Regenerate();
        }
        result["gridLinesRemoved"] = removed;

        var lines = new JsonArray();
        if (p["addGridLines"] is JsonArray addLines)
        {
            foreach (var node in addLines.OfType<JsonObject>())
            {
                var orientation = (P.StrOrNull(node, "orientation") ?? "").ToLowerInvariant();
                if (orientation is not ("vertical" or "horizontal"))
                    throw new RevitCommandException("invalid_parameter", "addGridLines[].orientation: vertical | horizontal.");
                var point = P.Xyz(node, "point", units);
                bool wantVertical = orientation == "vertical";
                // U/V naming depends on the host; add, check the real direction and swap if needed.
                CurtainGridLine? gl = TryAdd(grid, !wantVertical, point);
                if (gl is not null && IsVertical(gl) != wantVertical) { doc.Delete(gl.Id); gl = null; }
                gl ??= TryAdd(grid, wantVertical, point);
                if (gl is null || IsVertical(gl) != wantVertical)
                    throw new RevitCommandException("command_failed", $"Could not add a {orientation} grid line through ({point.X / P.MetersToFeet:0.###}, {point.Y / P.MetersToFeet:0.###}, {point.Z / P.MetersToFeet:0.###}) m.");
                var c = gl.FullCurve;
                lines.Add(new JsonObject
                {
                    ["id"] = gl.Id.Value, ["orientation"] = orientation,
                    ["start"] = M(c.GetEndPoint(0)), ["end"] = M(c.GetEndPoint(1)),
                });
            }
            doc.Regenerate();
        }
        result["gridLinesAdded"] = lines;

        // removeSegments: merge two panels across one segment of a grid line (a double door spanning two columns
        // below the door head while the fixed lights above keep the line). Live 14/09: storefront doors.
        var segmentsRemoved = new JsonArray();
        if (p["removeSegments"] is JsonArray removeSegments)
        {
            foreach (var node in removeSegments.OfType<JsonObject>())
            {
                var orientation = (P.StrOrNull(node, "orientation") ?? "").ToLowerInvariant();
                if (orientation is not ("vertical" or "horizontal"))
                    throw new RevitCommandException("invalid_parameter", "removeSegments[].orientation: vertical | horizontal.");
                var at = P.Xyz(node, "point", units);
                var tol = P.DblOr(node, "toleranceMeters", 0.05) * P.MetersToFeet;
                var gridLine = grid.GetUGridLineIds().Concat(grid.GetVGridLineIds()).Select(id => doc.GetElement(id) as CurtainGridLine)
                    .Where(gl => gl is not null && IsVertical(gl) == (orientation == "vertical"))
                    .Select(gl => (gl: gl!, d: Horizontal(gl!.FullCurve, at, orientation == "vertical")))
                    .Where(x => x.d <= tol).OrderBy(x => x.d).Select(x => x.gl).FirstOrDefault()
                    ?? throw new RevitCommandException("not_found", $"No {orientation} grid line within tolerance of the point.");
                // the segment whose projection contains the point (vertical: by height; horizontal: along the wall)
                var segment = gridLine.ExistingSegmentCurves.Cast<Curve>()
                    .OrderBy(c => c.Distance(orientation == "vertical" ? new XYZ(c.GetEndPoint(0).X, c.GetEndPoint(0).Y, at.Z) : new XYZ(at.X, at.Y, c.GetEndPoint(0).Z)))
                    .FirstOrDefault()
                    ?? throw new RevitCommandException("not_found", "The grid line has no existing segments.");
                if (gridLine.Pinned) gridLine.Pinned = false;
                gridLine.RemoveSegment(segment);
                segmentsRemoved.Add(new JsonObject { ["gridLineId"] = gridLine.Id.Value, ["orientation"] = orientation, ["start"] = M(segment.GetEndPoint(0)), ["end"] = M(segment.GetEndPoint(1)) });
            }
            doc.Regenerate();
        }
        result["segmentsRemoved"] = segmentsRemoved;

        var changed = new JsonArray();
        if (p["setPanels"] is JsonArray setPanels)
        {
            foreach (var node in setPanels.OfType<JsonObject>())
            {
                var point = P.Xyz(node, "point", units);
                var type = doc.GetElement(new ElementId(P.Long(node, "typeId"))) as ElementType
                    ?? throw new RevitCommandException("invalid_parameter", "setPanels[].typeId must identify a type.");
                var panel = PanelAt(doc, grid, point)
                    ?? throw new RevitCommandException("not_found", $"No panel contains ({point.X / P.MetersToFeet:0.###}, {point.Y / P.MetersToFeet:0.###}, {point.Z / P.MetersToFeet:0.###}) m.");
                var oldType = doc.GetElement(panel.GetTypeId())?.Name;
                if (panel.Pinned) panel.Pinned = false;
                panel.ChangeTypeId(type.Id);
                doc.Regenerate();
                var now = PanelAt(doc, grid, point);
                if (now is null || now.GetTypeId() != type.Id)
                    throw new RevitCommandException("verification_failed", $"Panel at point did not take type '{type.Name}'.");
                changed.Add(new JsonObject
                {
                    ["panelId"] = now.Id.Value, ["category"] = now.Category?.Name, ["oldType"] = oldType, ["newType"] = type.Name,
                    ["bbox"] = Box(now),
                });
            }
        }
        result["panelsChanged"] = changed;

        if (P.BoolOr(p, "listPanels", false) || (lines.Count == 0 && changed.Count == 0 && removed.Count == 0))
            result["panels"] = new JsonArray(grid.GetPanelIds().Select(id => doc.GetElement(id)).Where(e => e is not null)
                .Select(e => (JsonNode)new JsonObject
                {
                    ["id"] = e!.Id.Value, ["type"] = doc.GetElement(e.GetTypeId())?.Name, ["category"] = e.Category?.Name,
                    ["pinned"] = e.Pinned, ["bbox"] = Box(e),
                }).ToArray());
        result["gridLineCounts"] = new JsonObject { ["u"] = grid.GetUGridLineIds().Count, ["v"] = grid.GetVGridLineIds().Count };
        return result;
    }

    static CurtainGridLine? TryAdd(CurtainGrid grid, bool isU, XYZ point)
    {
        try { return grid.AddGridLine(isU, point, false); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { return null; }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException) { return null; }
    }

    // vertical lines: plan distance from the point to the line; horizontal lines: height difference
    static double Horizontal(Curve c, XYZ q, bool vertical)
    {
        var a = c.GetEndPoint(0);
        if (!vertical) return Math.Abs(a.Z - q.Z);
        return Math.Sqrt((a.X - q.X) * (a.X - q.X) + (a.Y - q.Y) * (a.Y - q.Y));
    }

    static bool IsVertical(CurtainGridLine gl)
    {
        var c = gl.FullCurve;
        var d = (c.GetEndPoint(1) - c.GetEndPoint(0)).Normalize();
        return Math.Abs(d.Z) > 0.9;
    }

    static Element? PanelAt(Document doc, CurtainGrid grid, XYZ point)
    {
        const double tol = 0.05 * P.MetersToFeet;
        return grid.GetPanelIds().Select(id => doc.GetElement(id)).Where(e => e is not null)
            .Select(e => (e: e!, b: e!.get_BoundingBox(null)))
            .Where(x => x.b is not null
                && point.X >= x.b.Min.X - tol && point.X <= x.b.Max.X + tol
                && point.Y >= x.b.Min.Y - tol && point.Y <= x.b.Max.Y + tol
                && point.Z >= x.b.Min.Z - tol && point.Z <= x.b.Max.Z + tol)
            .OrderBy(x => (x.b.Max - x.b.Min).GetLength())
            .Select(x => x.e).FirstOrDefault();
    }

    static JsonObject M(XYZ q) => new() { ["x"] = Math.Round(q.X / P.MetersToFeet, 4), ["y"] = Math.Round(q.Y / P.MetersToFeet, 4), ["z"] = Math.Round(q.Z / P.MetersToFeet, 4) };
    static JsonObject? Box(Element e) { var b = e.get_BoundingBox(null); return b is null ? null : new JsonObject { ["min"] = M(b.Min), ["max"] = M(b.Max) }; }
}
