using System;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

public sealed class CreateGableRoofCommand : IRevitCommand
{
    public string Name => "create_gable_roof";
    public bool IsReadOnly => false;
    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);
        var factor = units == "feet" ? 1.0 : P.MetersToFeet;
        var min = P.Xyz(p, "min", units);
        var max = P.Xyz(p, "max", units);
        // slopedEdges (xMin|xMax|yMin|yMax) replaces the ridge rule: one edge = a lean-to (live 14/09: stair roof
        // against a facade), two opposite edges = a gable, four = a hip. The roof rises away from each sloped edge.
        var slopedEdges = p["slopedEdges"] is JsonArray se ? se.Select(n => n!.GetValue<string>()).ToHashSet() : null;
        var axis = slopedEdges is null ? P.Str(p, "ridgeAxis") : "x";
        var angle = P.Dbl(p, "slopeDegrees");
        if (slopedEdges is not null && (slopedEdges.Count == 0 || slopedEdges.Any(e => e is not ("xMin" or "xMax" or "yMin" or "yMax"))))
            throw new RevitCommandException("invalid_parameter", "slopedEdges items must be xMin, xMax, yMin or yMax.");
        if (!double.IsFinite(angle) || (axis != "x" && axis != "y") || angle <= 0 || angle >= 80
            || max.X - min.X < 0.1 || max.Y - min.Y < 0.1)
            throw new RevitCommandException("invalid_parameter", "Require ridgeAxis x/y (or slopedEdges), slopeDegrees >0 and <80, and positive rectangular spans.");
        var level = CreateWallCommand.ResolveLevel(doc, P.Str(p, "levelName"));
        var roofType = doc.GetElement(new ElementId(P.Long(p, "roofTypeId"))) as RoofType
            ?? throw new RevitCommandException("not_found", "roofTypeId must identify a RoofType.");
        var offset = P.Dbl(p, "baseOffset") * factor;
        if (!double.IsFinite(offset))
            throw new RevitCommandException("invalid_parameter", "baseOffset must be finite.");
        if (roofType.FamilyName != "Basic Roof" && roofType.GetCompoundStructure() is null)
            throw new RevitCommandException("unsupported_type", "Use a basic compound roof type, not sloped glazing.");
        var points = new[] { new XYZ(min.X,min.Y,level.Elevation), new XYZ(max.X,min.Y,level.Elevation),
            new XYZ(max.X,max.Y,level.Elevation), new XYZ(min.X,max.Y,level.Elevation) };
        var curves = new CurveArray();
        for (int i=0;i<4;i++) curves.Append(Line.CreateBound(points[i], points[(i+1)%4]));
        var mapping = new ModelCurveArray();
        FootPrintRoof roof;
        try { roof = doc.Create.NewFootPrintRoof(curves, level, roofType, out mapping); }
        catch (Autodesk.Revit.Exceptions.ArgumentNullException ex) {
            throw new RevitCommandException("roof_creation_failed", "NewFootPrintRoof rejected an argument: " + ex.Message);
        }
        if (mapping is null || mapping.Size != 4)
            throw new RevitCommandException("roof_creation_failed", "Expected four native roof boundary curves.");
        var height = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM);
        if (height is null || height.IsReadOnly || !height.Set(offset))
            throw new RevitCommandException("command_failed", "Cannot set roof base offset.");
        bool Sloped(ModelCurve curve)
        {
            var a = curve.GeometryCurve.GetEndPoint(0); var b = curve.GeometryCurve.GetEndPoint(1); var delta = b - a;
            if (slopedEdges is null)
                return axis == "y" ? Math.Abs(delta.Y) > Math.Abs(delta.X) : Math.Abs(delta.X) > Math.Abs(delta.Y);
            var tol = 1e-3;
            var edge = Math.Abs(delta.X) < tol ? (Math.Abs(a.X - min.X) < tol ? "xMin" : "xMax")
                                               : (Math.Abs(a.Y - min.Y) < tol ? "yMin" : "yMax");
            return slopedEdges.Contains(edge);
        }
        foreach (ModelCurve curve in mapping)
        {
            bool sloped = Sloped(curve);
            roof.set_DefinesSlope(curve, sloped);
            if (sloped) roof.set_SlopeAngle(curve, Math.Tan(angle * Math.PI / 180));
        }
        doc.Regenerate();
        foreach (ModelCurve curve in mapping)
        {
            var expected = Sloped(curve);
            if (roof.get_DefinesSlope(curve) != expected || (expected && Math.Abs(roof.get_SlopeAngle(curve) - Math.Tan(angle * Math.PI / 180)) > 1e-8))
                throw new RevitCommandException("verification_failed", "Roof slopes did not retain requested values.");
        }
        var bb = roof.get_BoundingBox(null);
        return new JsonObject { ["id"] = roof.Id.Value, ["roofTypeName"] = roofType.Name,
            ["ridgeAxis"] = axis, ["slopeDegrees"] = angle, ["baseOffsetMeters"] = height.AsDouble()/P.MetersToFeet,
            ["minElevationMeters"] = bb?.Min.Z/P.MetersToFeet, ["maxElevationMeters"] = bb?.Max.Z/P.MetersToFeet };
    }
}
