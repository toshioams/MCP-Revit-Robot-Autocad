using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Create a DirectShape from simple solids (meters): the small facade items a survey needs and no family covers
/// (AC enclosures, meter boxes, downpipes, lightning rod masts, gate headers).
///
/// Params:
///   - name: string (DirectShape name, also written to Comments)
///   - category: BuiltInCategory name, default OST_GenericModel
///   - materialName: optional material for every solid
///   - solids: [ {type:"box", min:{x,y,z}, max:{x,y,z}}
///             | {type:"cylinder", center:{x,y}, radius, zMin, zMax}
///             | {type:"extrusion", profile:[{x,y},...], zMin, zMax} ]
/// </summary>
public sealed class CreateDirectShapeCommand : IRevitCommand
{
    public string Name => "create_direct_shape";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var catName = P.StrOrNull(p, "category") ?? "OST_GenericModel";
        if (!Enum.TryParse<BuiltInCategory>(catName, out var bic))
            throw new RevitCommandException("invalid_parameter", $"Unknown BuiltInCategory '{catName}'.");
        if (!DirectShape.IsValidCategoryId(new ElementId(bic), doc))
            throw new RevitCommandException("invalid_parameter", $"{catName} cannot hold DirectShapes.");
        if (p["solids"] is not JsonArray solids || solids.Count == 0)
            throw new RevitCommandException("invalid_parameter", "solids must be a non-empty array.");

        var materialId = ElementId.InvalidElementId;
        var materialName = P.StrOrNull(p, "materialName");
        if (!string.IsNullOrWhiteSpace(materialName))
            materialId = new FilteredElementCollector(doc).OfClass(typeof(Material)).FirstOrDefault(m => m.Name == materialName)?.Id
                ?? throw new RevitCommandException("not_found", $"Material '{materialName}' not found.");
        var options = new SolidOptions(materialId, ElementId.InvalidElementId);
        var ft = P.MetersToFeet;

        var geometry = new List<GeometryObject>();
        for (var i = 0; i < solids.Count; i++)
        {
            if (solids[i] is not JsonObject s)
                throw new RevitCommandException("invalid_parameter", $"solids[{i}] must be an object.");
            var kind = (P.StrOrNull(s, "type") ?? "").ToLowerInvariant();
            CurveLoop loop;
            double z0, z1;
            switch (kind)
            {
                case "panel":
                {
                    // One thin solid per planar convex polygon, including sloped railing infill.
                    var pts = P.XyzList(s, "profile", "meters");
                    var thickness = P.Dbl(s, "thicknessMeters") * ft;
                    if (pts.Count < 3 || pts.Count > 32 || !double.IsFinite(thickness) || thickness <= 0 || thickness > .1 * ft)
                        throw new RevitCommandException("invalid_parameter", "panel needs 3..32 vertices and thicknessMeters in (0, 0.1].");
                    var cross = (pts[1]-pts[0]).CrossProduct(pts[2]-pts[0]);
                    if (cross.GetLength() < 1e-8)
                        throw new RevitCommandException("invalid_parameter", "First three panel vertices must not be collinear.");
                    var normal = cross.Normalize();
                    if (pts.Any(v => Math.Abs((v-pts[0]).DotProduct(normal)) > 1e-6))
                        throw new RevitCommandException("invalid_parameter", "Panel vertices must be coplanar.");
                    loop = new CurveLoop();
                    for (int k=0; k<pts.Count; k++) {
                        var a=pts[k]; var b=pts[(k+1)%pts.Count];
                        if (a.DistanceTo(b) < ctx.App.Application.ShortCurveTolerance)
                            throw new RevitCommandException("invalid_parameter", "Panel edge is shorter than Revit tolerance.");
                        // Every other vertex must lie on the same side of each edge: rejects concavity and self-intersection.
                        if (pts.Any(v => (b-a).CrossProduct(v-a).DotProduct(normal) < -1e-7))
                            throw new RevitCommandException("invalid_parameter", "Panel polygon must be convex and ordered without crossings.");
                        loop.Append(Line.CreateBound(a-normal*thickness/2,b-normal*thickness/2));
                    }
                    geometry.Add(GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop>{loop},normal,thickness,options));
                    continue;
                }
                case "box":
                {
                    var a = P.Xyz(s, "min", "meters"); var b = P.Xyz(s, "max", "meters");
                    if (b.X <= a.X || b.Y <= a.Y || b.Z <= a.Z)
                        throw new RevitCommandException("invalid_parameter", $"solids[{i}]: max must exceed min on every axis.");
                    loop = Polygon(new[] { (a.X, a.Y), (b.X, a.Y), (b.X, b.Y), (a.X, b.Y) }, a.Z);
                    z0 = a.Z; z1 = b.Z;
                    break;
                }
                case "cylinder":
                {
                    var c = P.Xyz(s, "center", "meters"); var r = P.Dbl(s, "radius") * ft;
                    z0 = P.Dbl(s, "zMin") * ft; z1 = P.Dbl(s, "zMax") * ft;
                    if (r <= 0) throw new RevitCommandException("invalid_parameter", $"solids[{i}].radius must be positive.");
                    var o = new XYZ(c.X, c.Y, z0);
                    loop = new CurveLoop();
                    loop.Append(Arc.Create(o, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                    loop.Append(Arc.Create(o, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                    break;
                }
                case "extrusion":
                {
                    var pts = P.XyzList(s, "profile", "meters");
                    if (pts.Count < 3) throw new RevitCommandException("invalid_parameter", $"solids[{i}].profile needs at least 3 points.");
                    z0 = P.Dbl(s, "zMin") * ft; z1 = P.Dbl(s, "zMax") * ft;
                    loop = Polygon(pts.Select(v => (v.X, v.Y)).ToArray(), z0);
                    break;
                }
                default:
                    throw new RevitCommandException("invalid_parameter", $"solids[{i}].type must be box, cylinder, extrusion or panel.");
            }
            if (z1 <= z0) throw new RevitCommandException("invalid_parameter", $"solids[{i}]: zMax must exceed zMin.");
            geometry.Add(GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, z1 - z0, options));
        }

        var shape = DirectShape.CreateElement(doc, new ElementId(bic));
        shape.SetShape(geometry);
        var name = P.StrOrNull(p, "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            shape.SetName(name);
            shape.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(name);
        }
        doc.Regenerate(); // live 14/09: the bounding box was null before regeneration
        var box = shape.get_BoundingBox(null);
        return new JsonObject
        {
            ["id"] = shape.Id.Value, ["name"] = name, ["category"] = catName, ["solids"] = geometry.Count,
            ["min"] = box is null ? null : new JsonArray(Math.Round(box.Min.X / ft, 3), Math.Round(box.Min.Y / ft, 3), Math.Round(box.Min.Z / ft, 3)),
            ["max"] = box is null ? null : new JsonArray(Math.Round(box.Max.X / ft, 3), Math.Round(box.Max.Y / ft, 3), Math.Round(box.Max.Z / ft, 3)),
        };
    }

    static CurveLoop Polygon(IReadOnlyList<(double X, double Y)> pts, double z)
    {
        var loop = new CurveLoop();
        for (var k = 0; k < pts.Count; k++)
        {
            var a = new XYZ(pts[k].X, pts[k].Y, z); var b = new XYZ(pts[(k + 1) % pts.Count].X, pts[(k + 1) % pts.Count].Y, z);
            if (a.DistanceTo(b) < 1e-4) throw new RevitCommandException("invalid_parameter", "Profile has a zero-length edge.");
            loop.Append(Line.CreateBound(a, b));
        }
        return loop;
    }
}
