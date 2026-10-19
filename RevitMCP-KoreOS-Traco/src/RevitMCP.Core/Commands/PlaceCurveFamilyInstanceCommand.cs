using System;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Place a curve-based (line) family instance, e.g. ceiling shadow-gap trims (tabicas),
/// sweeps modelled as generic families. place_family_instance only handles point placement.
/// mode "face": hosted on a planar face of hostId (bottom/top/side by normal) — the line is
/// projected onto that face. mode "level": unhosted on a level at the line's elevation.
/// </summary>
public sealed class PlaceCurveFamilyInstanceCommand : IRevitCommand
{
    public string Name => "place_curve_family_instance";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);
        var symbol = doc.GetElement(new ElementId(P.Long(p, "typeId"))) as FamilySymbol
            ?? throw new RevitCommandException("invalid_parameter", "typeId must identify a family type.");
        var placement = symbol.Family.FamilyPlacementType;
        if (placement is not (FamilyPlacementType.CurveBased or FamilyPlacementType.CurveDrivenStructural or FamilyPlacementType.CurveBasedDetail))
            throw new RevitCommandException("unsupported_type", $"Family placement is {placement}; this command places line-based families.");
        if (placement == FamilyPlacementType.CurveBasedDetail)
            throw new RevitCommandException("unsupported_type", "Detail (view-specific) line families are not supported here.");
        var start = P.Xyz(p, "start", units); var end = P.Xyz(p, "end", units);
        if (start.DistanceTo(end) <= doc.Application.ShortCurveTolerance)
            throw new RevitCommandException("invalid_parameter", "Line is too short.");
        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

        var mode = (P.StrOrNull(p, "mode") ?? "face").ToLowerInvariant();
        FamilyInstance instance;
        string hostInfo;
        Line placed;
        if (mode == "face")
        {
            var host = doc.GetElement(new ElementId(P.Long(p, "hostId")))
                ?? throw new RevitCommandException("not_found", "hostId not found.");
            var side = (P.StrOrNull(p, "faceSide") ?? "bottom").ToLowerInvariant();
            var face = PickFace(host, side, (start + end) / 2)
                ?? throw new RevitCommandException("not_found", $"No planar {side} face on host {host.Id.Value} near the line.");
            var a = face.Project(start)?.XYZPoint ?? throw new RevitCommandException("invalid_parameter", "start does not project onto the host face.");
            var b = face.Project(end)?.XYZPoint ?? throw new RevitCommandException("invalid_parameter", "end does not project onto the host face.");
            placed = Line.CreateBound(a, b);
            instance = doc.Create.NewFamilyInstance(face.Reference, placed, symbol);
            hostInfo = $"{host.Id.Value}:{side} face normal ({face.FaceNormal.X:0.###},{face.FaceNormal.Y:0.###},{face.FaceNormal.Z:0.###})";
        }
        else if (mode == "level")
        {
            var level = CreateWallCommand.ResolveLevel(doc, P.StrOrNull(p, "levelName"));
            placed = Line.CreateBound(start, end);
            instance = doc.Create.NewFamilyInstance(placed, symbol, level, StructuralType.NonStructural);
            hostInfo = $"level {level.Name}";
        }
        else throw new RevitCommandException("invalid_parameter", "mode: face|level.");

        var comments = P.StrOrNull(p, "comments");
        if (comments is not null) instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(comments);
        doc.Regenerate();
        if ((instance.Location as LocationCurve)?.Curve is not Curve actual)
            throw new RevitCommandException("verification_failed", "Instance has no location curve.");
        double d0 = Math.Min(actual.GetEndPoint(0).DistanceTo(placed.GetEndPoint(0)), actual.GetEndPoint(0).DistanceTo(placed.GetEndPoint(1)));
        double d1 = Math.Min(actual.GetEndPoint(1).DistanceTo(placed.GetEndPoint(0)), actual.GetEndPoint(1).DistanceTo(placed.GetEndPoint(1)));
        if (d0 > 1e-3 || d1 > 1e-3)
            throw new RevitCommandException("verification_failed", $"Location curve is {Math.Max(d0, d1) / P.MetersToFeet:0.####} m from the requested line.");
        // Bounding boxes of these families were inflated live (2 cm tabica -> 22 cm); use the solid.
        var solid = SolidExtents.Points(instance);
        JsonObject Pt(XYZ v) => new() { ["x"] = v.X / P.MetersToFeet, ["y"] = v.Y / P.MetersToFeet, ["z"] = v.Z / P.MetersToFeet };
        return new JsonObject
        {
            ["id"] = instance.Id.Value, ["uniqueId"] = instance.UniqueId, ["familyName"] = symbol.FamilyName, ["typeName"] = symbol.Name,
            ["mode"] = mode, ["host"] = hostInfo, ["start"] = Pt(actual.GetEndPoint(0)), ["end"] = Pt(actual.GetEndPoint(1)),
            ["lengthMeters"] = actual.Length / P.MetersToFeet,
            ["solidExtents"] = SolidExtents.Json(solid),
            ["profileSide"] = SolidExtents.SideOfLine(solid, actual.GetEndPoint(0), actual.GetEndPoint(1)),
            ["note"] = "profileSide is where the mass lies seen from above along start->end (tabica tipo 2: right). Swap start/end to flip.",
        };
    }

    static PlanarFace? PickFace(Element host, string side, XYZ near)
    {
        var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
        var faces = (host.get_Geometry(options) ?? throw new RevitCommandException("unsupported_host", "Host has no geometry."))
            .OfType<Solid>().Where(s => s.Volume > 0).SelectMany(s => s.Faces.OfType<PlanarFace>())
            .Where(f => f.Reference is not null && side switch
            {
                "bottom" => f.FaceNormal.Z < -0.999,
                "top" => f.FaceNormal.Z > 0.999,
                _ => false,
            }).ToList();
        return faces.OrderBy(f => f.Project(near)?.Distance ?? double.MaxValue).FirstOrDefault();
    }
}
