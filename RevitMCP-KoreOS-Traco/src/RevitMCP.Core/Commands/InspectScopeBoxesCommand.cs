using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Read-only: scope boxes, their real extents in their own frame (rotation aware),
/// datums and views bound to them, and model elements that leave them. Added after
/// the pilot showed plans/levels cropped by a scope box that no longer enclosed the
/// building and furniture aligned to the box edge instead of the wall.
/// </summary>
public sealed class InspectScopeBoxesCommand : IRevitCommand
{
    public string Name => "inspect_scope_boxes";
    public bool IsReadOnly => true;

    static readonly BuiltInCategory[] DefaultCategories =
    {
        BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs,
        BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
        BuiltInCategory.OST_Furniture, BuiltInCategory.OST_Casework, BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_Railings,
    };

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var maxListed = Math.Clamp(P.IntOr(p, "maxOutsideListed", 50), 0, 500);
        var cats = DefaultCategories;
        if (p["categories"] is JsonArray raw)
            cats = raw.Select(n => Enum.TryParse<BuiltInCategory>(n?.GetValue<string>(), true, out var c) ? c
                : throw new RevitCommandException("invalid_parameter", $"Unknown BuiltInCategory '{n}'.")).ToArray();

        var datumIds = new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => e is DatumPlane).ToList();
        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();
        var elements = new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .WherePasses(new ElementMulticategoryFilter(cats)).ToElements();

        var result = new JsonArray();
        foreach (var box in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType())
        {
            var points = new List<XYZ>();
            XYZ? u = null;
            var geometry = box.get_Geometry(new Options());
            if (geometry is not null)
                foreach (var o in geometry)
                    if (o is Line l)
                    {
                        points.Add(l.GetEndPoint(0)); points.Add(l.GetEndPoint(1));
                        var d = l.Direction;
                        if (u is null && Math.Abs(d.Z) < 1e-9 && l.Length > 1e-6) u = new XYZ(d.X, d.Y, 0).Normalize();
                    }
            u ??= XYZ.BasisX;
            var v = XYZ.BasisZ.CrossProduct(u);
            if (points.Count == 0)
            {
                var bb = box.get_BoundingBox(null);
                if (bb is not null) { points.Add(bb.Min); points.Add(bb.Max); }
            }
            (double U, double V, double Z) Frame(XYZ q) => (q.DotProduct(u), q.DotProduct(v), q.Z);
            var fp = points.Select(Frame).ToList();
            double u0 = fp.Min(q => q.U), u1 = fp.Max(q => q.U), v0 = fp.Min(q => q.V), v1 = fp.Max(q => q.V),
                z0 = fp.Min(q => q.Z), z1 = fp.Max(q => q.Z);
            const double tol = 1e-4;

            var outside = new JsonArray();
            int outsideCount = 0;
            double needU0 = u0, needU1 = u1, needV0 = v0, needV1 = v1, needZ0 = z0, needZ1 = z1;
            foreach (var e in elements)
            {
                var bb = e.get_BoundingBox(null);
                if (bb is null) continue;
                var corners = new[] { 0, 1 }.SelectMany(i => new[] { 0, 1 }.SelectMany(j => new[] { 0, 1 }.Select(k =>
                    Frame(new XYZ(i == 0 ? bb.Min.X : bb.Max.X, j == 0 ? bb.Min.Y : bb.Max.Y, k == 0 ? bb.Min.Z : bb.Max.Z))))).ToList();
                double eu0 = corners.Min(c => c.U), eu1 = corners.Max(c => c.U), ev0 = corners.Min(c => c.V),
                    ev1 = corners.Max(c => c.V), ez0 = corners.Min(c => c.Z), ez1 = corners.Max(c => c.Z);
                var sides = new List<string>();
                if (eu0 < u0 - tol) sides.Add("-u"); if (eu1 > u1 + tol) sides.Add("+u");
                if (ev0 < v0 - tol) sides.Add("-v"); if (ev1 > v1 + tol) sides.Add("+v");
                if (ez0 < z0 - tol) sides.Add("below"); if (ez1 > z1 + tol) sides.Add("above");
                if (sides.Count == 0) continue;
                outsideCount++;
                needU0 = Math.Min(needU0, eu0); needU1 = Math.Max(needU1, eu1); needV0 = Math.Min(needV0, ev0);
                needV1 = Math.Max(needV1, ev1); needZ0 = Math.Min(needZ0, ez0); needZ1 = Math.Max(needZ1, ez1);
                if (outside.Count < maxListed)
                    outside.Add(new JsonObject { ["id"] = e.Id.Value, ["category"] = e.Category?.Name, ["name"] = e.Name,
                        ["sides"] = new JsonArray(sides.Select(s => (JsonNode)s).ToArray()) });
            }

            bool BoundTo(Element e, BuiltInParameter bip) => e.get_Parameter(bip)?.AsElementId() == box.Id;
            var datums = datumIds.Where(d => BoundTo(d, BuiltInParameter.DATUM_VOLUME_OF_INTEREST)).ToList();
            var boundViews = views.Where(w => BoundTo(w, BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)).ToList();
            double M(double f) => f / P.MetersToFeet;
            result.Add(new JsonObject
            {
                ["id"] = box.Id.Value, ["uniqueId"] = box.UniqueId, ["name"] = box.Name,
                ["frameUDirection"] = new JsonObject { ["x"] = u.X, ["y"] = u.Y },
                ["rotationDegrees"] = Math.Atan2(u.Y, u.X) * 180 / Math.PI,
                ["extentsMetersInBoxFrame"] = new JsonObject { ["u0"] = M(u0), ["u1"] = M(u1), ["v0"] = M(v0), ["v1"] = M(v1), ["z0"] = M(z0), ["z1"] = M(z1) },
                ["heightMeters"] = M(z1 - z0),
                ["boundDatums"] = new JsonArray(datums.Select(d => (JsonNode)new JsonObject { ["id"] = d.Id.Value, ["name"] = d.Name, ["class"] = d.GetType().Name }).ToArray()),
                ["boundViewCount"] = boundViews.Count,
                ["boundViews"] = new JsonArray(boundViews.Take(100).Select(w => (JsonNode)new JsonObject { ["id"] = w.Id.Value, ["name"] = w.Name, ["viewType"] = w.ViewType.ToString() }).ToArray()),
                ["modelElementsOutside"] = outsideCount,
                ["outsideListed"] = outside,
                ["requiredExtentsMetersInBoxFrame"] = outsideCount == 0 ? null : new JsonObject
                    { ["u0"] = M(needU0), ["u1"] = M(needU1), ["v0"] = M(needV0), ["v1"] = M(needV1), ["z0"] = M(needZ0), ["z1"] = M(needZ1) },
            });
        }
        return new JsonObject
        {
            ["scopeBoxes"] = result,
            ["categoriesChecked"] = new JsonArray(cats.Select(c => (JsonNode)c.ToString()).ToArray()),
            ["note"] = "Element bounding boxes are conservative (rotated families enlarge them). Revit API has no public call to resize "
                + "scope box plan extents: move/rotate the box or change its Height parameter, otherwise ask the user to drag its grips. "
                + "Never use scope box or level extents as construction references for walls or families.",
        };
    }
}
