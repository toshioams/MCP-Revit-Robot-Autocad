using System;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Structural framing member between two 3D points (sloped allowed). create_beam flattens
/// both ends to the level elevation, which made rafters impossible. The location line is
/// the given points; zJustification/yJustification say which face/edge of the section lies
/// on it. Ends are not auto-joined by default so Revit does not trim or extend members.
/// </summary>
public sealed class CreateFramingMemberCommand : IRevitCommand
{
    public string Name => "create_framing_member";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);
        var start = P.Xyz(p, "start", units);
        var end = P.Xyz(p, "end", units);
        if (p["start"]?["z"] is null || p["end"]?["z"] is null)
            throw new RevitCommandException("invalid_parameter", "start and end need explicit absolute z.");
        if (start.DistanceTo(end) <= doc.Application.ShortCurveTolerance)
            throw new RevitCommandException("invalid_parameter", "Member is too short.");
        var symbol = doc.GetElement(new ElementId(P.Long(p, "typeId"))) as FamilySymbol
            ?? throw new RevitCommandException("invalid_parameter", "typeId must identify a family type.");
        if (symbol.Category?.BuiltInCategory != BuiltInCategory.OST_StructuralFraming)
            throw new RevitCommandException("invalid_parameter", "typeId must be a Structural Framing type.");
        var level = CreateWallCommand.ResolveLevel(doc, P.StrOrNull(p, "levelName"));
        var z = (P.StrOrNull(p, "zJustification") ?? "top").ToLowerInvariant() switch
        {
            "top" => ZJustification.Top, "center" => ZJustification.Center,
            "bottom" => ZJustification.Bottom, "origin" => ZJustification.Origin,
            _ => throw new RevitCommandException("invalid_parameter", "zJustification: top|center|bottom|origin."),
        };
        var y = (P.StrOrNull(p, "yJustification") ?? "center").ToLowerInvariant() switch
        {
            "left" => YJustification.Left, "center" => YJustification.Center,
            "right" => YJustification.Right, "origin" => YJustification.Origin,
            _ => throw new RevitCommandException("invalid_parameter", "yJustification: left|center|right|origin."),
        };
        var rotation = P.DblOr(p, "crossSectionRotationDegrees", 0);
        if (!double.IsFinite(rotation)) throw new RevitCommandException("invalid_parameter", "Rotation must be finite.");

        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
        var member = doc.Create.NewFamilyInstance(Line.CreateBound(start, end), symbol, level, StructuralType.Beam);
        if (!P.BoolOr(p, "allowJoins", false))
        {
            StructuralFramingUtils.DisallowJoinAtEnd(member, 0);
            StructuralFramingUtils.DisallowJoinAtEnd(member, 1);
        }
        SetInt(member, BuiltInParameter.Z_JUSTIFICATION, (int)z);
        SetInt(member, BuiltInParameter.Y_JUSTIFICATION, (int)y);
        SetDouble(member, BuiltInParameter.Z_OFFSET_VALUE, 0);
        SetDouble(member, BuiltInParameter.Y_OFFSET_VALUE, 0);
        SetDouble(member, BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE, rotation * Math.PI / 180);
        if (!P.BoolOr(p, "structural", true))
            SetInt(member, BuiltInParameter.INSTANCE_STRUCT_USAGE_PARAM, (int)StructuralInstanceUsage.Other);
        var comments = P.StrOrNull(p, "comments");
        if (comments is not null) member.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(comments);
        doc.Regenerate();

        if ((member.Location as LocationCurve)?.Curve is not Line actual)
            throw new RevitCommandException("verification_failed", "Member has no straight location line.");
        const double tol = 1e-4;
        double d0 = actual.GetEndPoint(0).DistanceTo(start), d1 = actual.GetEndPoint(1).DistanceTo(end);
        if (d0 > tol || d1 > tol)
            throw new RevitCommandException("verification_failed",
                $"Location line moved: start {d0 / P.MetersToFeet:0.####} m, end {d1 / P.MetersToFeet:0.####} m from the request.");
        var zBack = member.get_Parameter(BuiltInParameter.Z_JUSTIFICATION)?.AsInteger();
        if (zBack != (int)z) throw new RevitCommandException("verification_failed", "zJustification was not retained.");
        var (zMin, zMax) = CreateProfileWallCommand.SolidZRange(member);
        JsonObject Pt(XYZ v) => new() { ["x"] = v.X / P.MetersToFeet, ["y"] = v.Y / P.MetersToFeet, ["z"] = v.Z / P.MetersToFeet };
        return new JsonObject
        {
            ["id"] = member.Id.Value, ["uniqueId"] = member.UniqueId,
            ["typeId"] = symbol.Id.Value, ["typeName"] = symbol.Name, ["familyName"] = symbol.FamilyName,
            ["levelName"] = level.Name,
            ["start"] = Pt(actual.GetEndPoint(0)), ["end"] = Pt(actual.GetEndPoint(1)),
            ["lengthMeters"] = actual.Length / P.MetersToFeet,
            ["slopeDegrees"] = Math.Asin(Math.Abs(actual.Direction.Z)) * 180 / Math.PI,
            ["zJustification"] = z.ToString(), ["yJustification"] = y.ToString(),
            ["solidMinElevationMeters"] = double.IsNaN(zMin) ? null : zMin / P.MetersToFeet,
            ["solidMaxElevationMeters"] = double.IsNaN(zMax) ? null : zMax / P.MetersToFeet,
            ["joinsAllowed"] = P.BoolOr(p, "allowJoins", false),
            ["note"] = "Geometry placement only; not a structural design or connection check. Verify faces against supports and boarding.",
        };
    }

    static void SetInt(Element e, BuiltInParameter bip, int value)
    {
        var prm = e.get_Parameter(bip);
        if (prm is null || prm.IsReadOnly) throw new RevitCommandException("unsupported_type", $"Parameter {bip} is not writable on this member.");
        prm.Set(value);
    }

    static void SetDouble(Element e, BuiltInParameter bip, double value)
    {
        var prm = e.get_Parameter(bip);
        if (prm is not null && !prm.IsReadOnly) prm.Set(value);
    }
}
