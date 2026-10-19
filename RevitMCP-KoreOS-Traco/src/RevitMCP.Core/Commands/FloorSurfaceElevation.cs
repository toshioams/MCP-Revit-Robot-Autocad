using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

internal static class FloorSurfaceElevation
{
    // Intersect actual top faces, not level elevation or the bounding-box top.
    internal static double At(Document doc, long floorId, XYZ point)
    {
        var floor = doc.GetElement(new ElementId(floorId)) as Floor
            ?? throw new RevitCommandException("invalid_floor", "floorId must identify a Floor in the active document.");
        var box = floor.get_BoundingBox(null)
            ?? throw new RevitCommandException("floor_surface_not_found", "Floor has no bounding box.");
        var ray = Line.CreateBound(new XYZ(point.X, point.Y, box.Min.Z - 1),
            new XYZ(point.X, point.Y, box.Max.Z + 1));
        var hits = new List<double>();
        foreach (var reference in HostObjectUtils.GetTopFaces(floor))
        {
            if (floor.GetGeometryObjectFromReference(reference) is not Face face) continue;
            face.Intersect(ray, out IntersectionResultArray intersections);
            if (intersections is null) continue;
            foreach (IntersectionResult hit in intersections)
            {
                var z = hit.XYZPoint.Z;
                if (!hits.Exists(existing => Math.Abs(existing - z) < 1e-6)) hits.Add(z);
            }
        }
        if (hits.Count == 0)
            throw new RevitCommandException("floor_surface_not_found", "No floor top surface at the requested XY (outside floor or inside opening).");
        if (hits.Count > 1)
            throw new RevitCommandException("ambiguous_floor_surface", "Multiple floor top elevations at the requested XY.");
        return hits[0];
    }
}
