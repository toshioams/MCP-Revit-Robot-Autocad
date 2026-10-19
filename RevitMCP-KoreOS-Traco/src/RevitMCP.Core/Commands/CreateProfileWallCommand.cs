using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

public sealed class CreateProfileWallCommand : IRevitCommand
{
    public string Name => "create_profile_wall";
    public bool IsReadOnly => false;
    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        if (p["profile"] is not JsonArray raw || raw.Count < 3 || raw.Count > 100
            || raw.Any(n => n is not JsonObject v || v["z"] is null))
            throw new RevitCommandException("invalid_profile", "Supply 3..100 vertices with explicit absolute x/y/z, without repeating the first vertex.");
        var points = P.XyzList(p,"profile",P.Units(p));
        if (points.Any(v => !double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z)))
            throw new RevitCommandException("invalid_profile","All coordinates must be finite.");
        var origin = points[0];
        XYZ? horizontal = null;
        foreach (var point in points.Skip(1))
        {
            var delta = new XYZ(point.X-origin.X,point.Y-origin.Y,0);
            if(delta.GetLength() > 1e-6) { horizontal=delta.Normalize(); break; }
        }
        if(horizontal is null)
            throw new RevitCommandException("invalid_profile","Profile has no horizontal span.");
        var normal = horizontal.CrossProduct(XYZ.BasisZ).Normalize();
        if(points.Any(v => Math.Abs((v-origin).DotProduct(normal)) > 1e-6))
            throw new RevitCommandException("invalid_profile","Vertices must lie in one vertical plane.");
        var uv = points.Select(v => new UV((v-origin).DotProduct(horizontal),v.Z-origin.Z)).ToList();
        double Cross(UV a,UV b,UV c) => (b.U-a.U)*(c.V-a.V)-(b.V-a.V)*(c.U-a.U);
        // Convex profiles only in this first version: triangles and gable polygons.
        // Every other vertex must lie strictly on the same side of each edge;
        // this rejects crossings, concavities, collinear corners and repeated points.
        int winding=0;
        for(int i=0;i<uv.Count;i++)
        {
            var next=(i+1)%uv.Count;
            if(points[i].DistanceTo(points[next]) <= doc.Application.ShortCurveTolerance)
                throw new RevitCommandException("invalid_profile","Profile contains a short or zero-length edge.");
            for(int j=0;j<uv.Count;j++)
            {
                if(j==i || j==next)continue;
                var cross=Cross(uv[i],uv[next],uv[j]);
                if(Math.Abs(cross)<1e-9 || (winding!=0 && Math.Sign(cross)!=winding))
                    throw new RevitCommandException("unsupported_profile","Use a simple strictly convex profile, without collinear corners or openings.");
                winding=Math.Sign(cross);
            }
        }
        var type=doc.GetElement(new ElementId(P.Long(p,"wallTypeId"))) as WallType
            ?? throw new RevitCommandException("invalid_parameter","wallTypeId must identify a wall type.");
        if(type.Kind!=WallKind.Basic)
            throw new RevitCommandException("unsupported_type","Only basic wall types are supported.");
        var level=CreateWallCommand.ResolveLevel(doc,P.Str(p,"levelName"));
        var curves=new List<Curve>();
        for(int i=0;i<points.Count;i++)curves.Add(Line.CreateBound(points[i],points[(i+1)%points.Count]));
        var wall=Wall.Create(doc,curves,type.Id,level.Id,P.BoolOr(p,"structural",false),normal);
        if(P.BoolOr(p,"flip",false))wall.Flip();
        doc.Regenerate();
        // 0.8.40 compared get_BoundingBox with the profile and failed live for every
        // profile, including a 0..3 m rectangle: the element box of a new profile wall
        // is not a reliable geometric extent. Verify the regenerated solid instead and
        // report both values so a future mismatch can be diagnosed.
        var (solidMin,solidMax)=SolidZRange(wall);
        double expectedMin=points.Min(v=>v.Z), expectedMax=points.Max(v=>v.Z), tolerance=1e-4;
        bool SolidOk() => !double.IsNaN(solidMin) && Math.Abs(solidMin-expectedMin)<=tolerance && Math.Abs(solidMax-expectedMax)<=tolerance;
        var diagnostics=new JsonObject { ["solidBeforeNormalization"]=$"{M(solidMin)}..{M(solidMax)}" };
        // 0.8.42 live: the new wall regenerated as an ordinary wall from the level to the next
        // level (0..2.9 m) instead of the 2.8..6.0 m profile. Pin base offset and unconnected
        // height to the profile extents, then re-read.
        if(!SolidOk())
        {
            TrySet(wall,BuiltInParameter.WALL_HEIGHT_TYPE,ElementId.InvalidElementId);
            TrySet(wall,BuiltInParameter.WALL_BASE_OFFSET,expectedMin-level.Elevation);
            TrySet(wall,BuiltInParameter.WALL_USER_HEIGHT_PARAM,expectedMax-expectedMin);
            doc.Regenerate();
            (solidMin,solidMax)=SolidZRange(wall);
            diagnostics["normalized"]=true;
        }
        var sketch=doc.GetElement(wall.SketchId) as Sketch;
        double sketchMin=double.NaN, sketchMax=double.NaN;
        if(sketch is not null)
            foreach(CurveArray loop in sketch.Profile) foreach(Curve c in loop) foreach(var v in new[]{c.GetEndPoint(0),c.GetEndPoint(1)})
            { sketchMin=double.IsNaN(sketchMin)?v.Z:Math.Min(sketchMin,v.Z); sketchMax=double.IsNaN(sketchMax)?v.Z:Math.Max(sketchMax,v.Z); }
        var box=wall.get_BoundingBox(null);
        diagnostics["solid"]=$"{M(solidMin)}..{M(solidMax)}"; diagnostics["boundingBox"]=$"{M(box?.Min.Z)}..{M(box?.Max.Z)}";
        diagnostics["sketch"]=sketch is null?"none":$"{M(sketchMin)}..{M(sketchMax)}";
        diagnostics["baseOffset"]=M(wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble());
        diagnostics["unconnectedHeight"]=M(wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble());
        diagnostics["topConstraint"]=wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId().Value;
        var sketchOk=sketch is not null && Math.Abs(sketchMin-expectedMin)<=tolerance && Math.Abs(sketchMax-expectedMax)<=tolerance;
        var verification=(P.StrOrNull(p,"verification")??"solid").ToLowerInvariant();
        if(!SolidOk() && !(verification=="sketch" && sketchOk))
            throw new RevitCommandException("verification_failed",
                $"Profile {M(expectedMin)}..{M(expectedMax)} m not reflected: {diagnostics.ToJsonString()}");
        return new JsonObject { ["id"]=wall.Id.Value,["uniqueId"]=wall.UniqueId,
            ["wallTypeId"]=type.Id.Value,["levelId"]=level.Id.Value,
            ["minElevationMeters"]=solidMin/P.MetersToFeet,["maxElevationMeters"]=solidMax/P.MetersToFeet,
            ["boundingBoxMinElevationMeters"]=box is null?null:box.Min.Z/P.MetersToFeet,
            ["boundingBoxMaxElevationMeters"]=box is null?null:box.Max.Z/P.MetersToFeet,
            ["widthMeters"]=wall.Width/P.MetersToFeet,["flipped"]=wall.Flipped,
            ["verifiedBy"]=SolidOk()?"solid":"sketch",["postCommitCheckRequired"]=!SolidOk(),["diagnostics"]=diagnostics,
            ["note"]="Independent native profile wall. No roof attachment, bearing design, gap inference or automatic update when the roof changes. Verify both wall faces against the roof." };
    }

    static void TrySet(Element e, BuiltInParameter bip, object value)
    {
        var prm=e.get_Parameter(bip);
        if(prm is null || prm.IsReadOnly) return;
        try { if(value is ElementId id) prm.Set(id); else prm.Set((double)value); } catch { /* reported by diagnostics */ }
    }

    static string M(double? feet) => feet is null || double.IsNaN(feet.Value) ? "n/a" : (feet.Value/P.MetersToFeet).ToString("0.####",System.Globalization.CultureInfo.InvariantCulture);

    internal static (double Min,double Max) SolidZRange(Element element)
    {
        double min=double.NaN, max=double.NaN;
        void Add(XYZ v) { if(double.IsNaN(min)||v.Z<min)min=v.Z; if(double.IsNaN(max)||v.Z>max)max=v.Z; }
        void Visit(GeometryElement g) {
            foreach(GeometryObject o in g) {
                if(o is GeometryInstance i) { Visit(i.GetInstanceGeometry()); continue; }
                if(o is not Solid s || s.Volume<=0) continue;
                foreach(Edge edge in s.Edges) foreach(var v in edge.Tessellate()) Add(v);
            }
        }
        using var options=new Options { DetailLevel=ViewDetailLevel.Fine };
        var geometry=element.get_Geometry(options);
        if(geometry is not null) Visit(geometry);
        return (min,max);
    }
}
