using System;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
namespace RevitMCPAddin.Commands;
public sealed class InspectPlanarFacesCommand : IRevitCommand
{
    public string Name => "inspect_planar_faces";
    public bool IsReadOnly => true;
    public JsonNode? Execute(CommandContext ctx)
    {
        var p=ctx.Parameters;
        var e=ctx.RequireDoc().GetElement(new ElementId(P.Long(p,"id")))
            ?? throw new RevitCommandException("not_found","Element not found.");
        var probe=p["probe"] is null ? null : P.Xyz(p,"probe",P.Units(p));
        int offset=Math.Max(0,P.IntOr(p,"offset",0)), limit=Math.Clamp(P.IntOr(p,"limit",100),1,500), total=0, skipped=0;
        var faces=new JsonArray();
        JsonObject Point(XYZ v) => new() { ["x"]=v.X/P.MetersToFeet,["y"]=v.Y/P.MetersToFeet,["z"]=v.Z/P.MetersToFeet };
        void Visit(GeometryElement g) {
            foreach (GeometryObject o in g) {
                if(o is GeometryInstance i) { Visit(i.GetInstanceGeometry()); continue; }
                if(o is not Solid s) continue;
                foreach(Face raw in s.Faces) {
                    if(raw is not PlanarFace f) { skipped++; continue; }
                    int index=total++;
                    if(index<offset || faces.Count>=limit) continue;
                    var n=f.FaceNormal;
                    JsonObject? hit=null;
                    if(probe is not null && Math.Abs(n.Z)>1e-8) {
                        var z=f.Origin.Z-(n.X*(probe.X-f.Origin.X)+n.Y*(probe.Y-f.Origin.Y))/n.Z;
                        var projection=f.Project(new XYZ(probe.X,probe.Y,z));
                        if(projection is not null && f.IsInside(projection.UVPoint)) hit=Point(projection.XYZPoint);
                    }
                    var loops = new JsonArray();
                    if (P.BoolOr(p,"includeEdgeLoops",false)) {
                        foreach (var edgeLoop in f.GetEdgesAsCurveLoops()) {
                            var edges = new JsonArray();
                            foreach (var curve in edgeLoop) {
                                var points = new JsonArray();
                                foreach(var vertex in curve.Tessellate()) points.Add(Point(vertex));
                                edges.Add(new JsonObject { ["kind"]=curve.GetType().Name,["pointsMeters"]=points });
                            }
                            loops.Add(edges);
                        }
                    }
                    faces.Add(new JsonObject { ["index"]=index,["originMeters"]=Point(f.Origin),["edgeLoops"]=loops,
                        ["normal"]=new JsonObject{["x"]=n.X,["y"]=n.Y,["z"]=n.Z},
                        ["areaSquareMeters"]=f.Area/(P.MetersToFeet*P.MetersToFeet),
                        ["materialId"]=f.MaterialElementId.Value,["upward"]=n.Z>0.999,["verticalProbeHitMeters"]=hit });
                }
            }
        }
        using var options=new Options { DetailLevel=ViewDetailLevel.Fine,IncludeNonVisibleObjects=false };
        var geometry=e.get_Geometry(options);
        if(geometry is not null) Visit(geometry);
        return new JsonObject { ["id"]=e.Id.Value,["faces"]=faces,["total"]=total,["offset"]=offset,
            ["hasMore"]=offset+faces.Count<total,["nonPlanarFacesSkipped"]=skipped,
            ["note"]="Document coordinates in meters. Transient face indices, not hosting references. Probe returns intersected faces, not a semantic support decision." };
    }
}
