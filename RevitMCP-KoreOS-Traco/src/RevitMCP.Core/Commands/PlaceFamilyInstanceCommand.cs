using System;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Place a FamilyInstance at a point.
///
/// Params:
///   - location:       { x, y, z? }
///   - familyName:     string, optional
///   - familyTypeName: string, optional
///   - category:       BuiltInCategory name, optional (helps narrow the search)
///   - levelName:      string, optional
///   - hostId:         long, optional — element id of host wall/face; when
///                     present uses the hosted overload so Revit auto-cuts
///                     the opening (required for doors/windows)
///   - flipFacing:     bool, optional — flip the door/window facing side
///   - flipHand:       bool, optional — flip the door/window hinge side
///   - structural:     bool, default false
///   - units:          "meters"|"feet"
///
/// When neither familyName nor familyTypeName is supplied (or the query still
/// returns multiple symbols), the command returns a candidate list instead of
/// picking arbitrarily.  Specify both fields for unambiguous placement.
/// </summary>
public sealed class PlaceFamilyInstanceCommand : IRevitCommand
{
    public string Name => "place_family_instance";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);

        var familyName = P.StrOrNull(p, "familyName");
        var typeName = P.StrOrNull(p, "familyTypeName");
        var catName = P.StrOrNull(p, "category");

        var query = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>();

        if (!string.IsNullOrWhiteSpace(catName)
            && Enum.TryParse<BuiltInCategory>(catName, true, out var bic))
            query = query.Where(s => s.Category?.BuiltInCategory == bic);

        if (!string.IsNullOrWhiteSpace(familyName))
            query = query.Where(s => s.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(typeName))
            query = query.Where(s => s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

        // Cap at 11 to distinguish "many" from "exactly 10".
        var matches = query.Take(11).ToList();

        if (matches.Count == 0)
            throw new RevitCommandException("not_found",
                $"No FamilySymbol found (family='{familyName ?? "*"}', " +
                $"type='{typeName ?? "*"}', category='{catName ?? "*"}').");

        // When the query is still ambiguous (no explicit type selected), return
        // candidates so the caller can make an informed choice.
        bool noExplicitFamily = string.IsNullOrWhiteSpace(familyName);
        bool noExplicitType   = string.IsNullOrWhiteSpace(typeName);

        if (matches.Count > 1 && noExplicitFamily && noExplicitType)
        {
            var shown = matches.Take(10).ToList();
            return new JsonObject
            {
                ["placed"] = false,
                ["action"] = "select_type",
                ["candidatesFound"] = matches.Count > 10 ? "10+" : matches.Count.ToString(),
                ["candidates"] = new JsonArray(shown.Select(s => (JsonNode?)new JsonObject
                {
                    ["familyName"] = s.FamilyName,
                    ["typeName"] = s.Name,
                    ["category"] = s.Category?.Name,
                    ["id"] = s.Id.Value,
                }).ToArray()),
                ["hint"] = "Multiple family types match. Specify familyName and familyTypeName to place an instance.",
            };
        }

        var symbol = matches[0];
        bool usedFirstMatch = matches.Count > 1; // partial filter → first wins, but warn

        var location = P.Xyz(p, "location", units);
        var level = CreateWallCommand.ResolveLevel(doc, P.StrOrNull(p, "levelName"));
        var structural = P.BoolOr(p, "structural", false);
        var flipFacing = P.BoolOr(p, "flipFacing", false);
        var flipHand   = P.BoolOr(p, "flipHand",   false);

        bool hasHost = p.ContainsKey("hostId") && p["hostId"] is not null;
        var hostSide = P.StrOrNull(p, "hostSide");
        if (hostSide is not null && (!hasHost || (hostSide != "interior" && hostSide != "exterior" && hostSide != "top")))
            throw new RevitCommandException("invalid_parameter", "hostSide requires hostId and must be interior, exterior (wall faces) or top (floor top face).");
        if (hostSide is null && p["referenceDirection"] is not null)
            throw new RevitCommandException("invalid_parameter", "referenceDirection requires hostSide.");

        if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

        var stype = StructuralType.NonStructural;
        if (structural)
        {
            var bc = symbol.Category?.BuiltInCategory;
            stype = bc switch
            {
                BuiltInCategory.OST_StructuralColumns => StructuralType.Column,
                BuiltInCategory.OST_StructuralFraming => StructuralType.Beam,
                BuiltInCategory.OST_StructuralFoundation => StructuralType.Footing,
                _ => StructuralType.NonStructural,
            };
        }

        var explicitZ = p["location"] is JsonObject locationObject && locationObject["z"] is not null;
        var relativeToFloor = p["floorId"] is not null || p["heightAboveFloor"] is not null;
        double? floorElevation = null;
        double? heightAboveFloorFeet = null;
        if (relativeToFloor)
        {
            if (explicitZ || p["floorId"] is null || p["heightAboveFloor"] is null)
                throw new RevitCommandException("invalid_parameter", "Provide floorId and heightAboveFloor together; omit location.z.");
            heightAboveFloorFeet = P.Dbl(p, "heightAboveFloor") * (units == "feet" ? 1 : P.MetersToFeet);
            floorElevation = FloorSurfaceElevation.At(doc, P.Long(p, "floorId"), location);
            location = new XYZ(location.X, location.Y, floorElevation.Value + heightAboveFloorFeet.Value);
            explicitZ = true;
        }
        var pt = new XYZ(location.X, location.Y, explicitZ ? location.Z : level.Elevation);

        FamilyInstance instance;
        long? returnedHostId = null;
        string? hostWarning = null;
        XYZ? projectedPoint = null;
        double? topFaceZ = null;

        if (hasHost)
        {
            var rawHostId = P.Long(p, "hostId");
            var hostElem = doc.GetElement(new ElementId(rawHostId))
                ?? throw new RevitCommandException("not_found",
                    $"Host element id {rawHostId} not found in document.");

            if (hostSide == "top")
            {
                // Work-plane-based fixtures (basins, drains) on a floor's top face, optionally raised
                // above it through the host offset (e.g. an inset basin at countertop height).
                if (hostElem is not Floor floor || symbol.Family.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased)
                    throw new RevitCommandException("unsupported_host", "hostSide 'top' requires a Floor host and a WorkPlaneBased family.");
                if (!explicitZ || flipFacing || flipHand || structural)
                    throw new RevitCommandException("invalid_parameter", "Top-face placement requires location.z or floorId plus heightAboveFloor, and does not accept flipFacing, flipHand or structural=true.");
                var direction = p["referenceDirection"] is null ? XYZ.BasisX : P.Xyz(p, "referenceDirection", "feet");
                if (direction.GetLength() < 1e-9 || System.Math.Abs(direction.Z) > 1e-6)
                    throw new RevitCommandException("invalid_parameter", "referenceDirection must be a nonzero horizontal vector.");
                direction = direction.Normalize();
                var candidates = HostObjectUtils.GetTopFaces(floor)
                    .Select(reference => new { Reference = reference, Face = floor.GetGeometryObjectFromReference(reference) as PlanarFace })
                    .Where(x => x.Face is not null && System.Math.Abs(x.Face!.FaceNormal.Z) > 0.999)
                    .Select(x => new { x.Reference, Face = x.Face!, Projection = x.Face!.Project(pt) })
                    .Where(x => x.Projection is not null && x.Face.IsInside(x.Projection.UVPoint))
                    .OrderBy(x => System.Math.Abs(pt.Z - x.Projection!.XYZPoint.Z)).ToList();
                if (candidates.Count == 0)
                    throw new RevitCommandException("face_not_found", "No top face of the floor lies under the insertion point.");
                var candidate = candidates[0];
                projectedPoint = candidate.Projection!.XYZPoint;
                instance = doc.Create.NewFamilyInstance(candidate.Reference, projectedPoint, direction, symbol);
                var raise = pt.Z - projectedPoint.Z;
                if (System.Math.Abs(raise) > 1e-6)
                {
                    var offset = instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                    if (offset is null || offset.IsReadOnly)
                        throw new RevitCommandException("placement_mismatch", "The family exposes no writable host offset to raise it above the face.");
                    offset.Set(raise);
                }
                topFaceZ = projectedPoint.Z;
                returnedHostId = floor.Id.Value;
            }
            else if (hostSide is not null)
            {
                if (hostElem is not Wall wall || symbol.Family.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased)
                    throw new RevitCommandException("unsupported_host", "Face placement requires a wall and a WorkPlaneBased family.");
                if (!explicitZ || flipFacing || flipHand || structural)
                    throw new RevitCommandException("invalid_parameter", "Face placement requires location.z or floorId plus heightAboveFloor, and does not accept flipFacing, flipHand or structural=true.");
                var direction = P.Xyz(p, "referenceDirection", "feet"); // dimensionless vector
                if (direction.GetLength() < 1e-9)
                    throw new RevitCommandException("invalid_parameter", "referenceDirection must be nonzero.");
                direction = direction.Normalize();
                var refs = HostObjectUtils.GetSideFaces(wall, hostSide == "interior" ? ShellLayerType.Interior : ShellLayerType.Exterior);
                var candidates = refs.Select(reference => new { Reference = reference, Face = wall.GetGeometryObjectFromReference(reference) as PlanarFace })
                    .Where(x => x.Face is not null)
                    .Select(x => new { x.Reference, Face = x.Face!, Projection = x.Face!.Project(pt) })
                    .Where(x => x.Projection is not null && x.Face.IsInside(x.Projection.UVPoint))
                    .OrderBy(x => x.Projection!.Distance).ToList();
                if (candidates.Count == 0)
                    throw new RevitCommandException("face_not_found", "No planar side face contains the projected insertion point.");
                var candidate = candidates[0];
                if (candidate.Projection!.Distance > 0.05 * P.MetersToFeet)
                    throw new RevitCommandException("point_off_face", "Insertion point must be within 50 mm of the selected face.");
                if (System.Math.Abs(direction.DotProduct(candidate.Face.FaceNormal)) > 1e-6)
                    throw new RevitCommandException("invalid_parameter", "referenceDirection must be tangent to the selected wall face.");
                projectedPoint = candidate.Projection.XYZPoint;
                if (relativeToFloor && projectedPoint.DistanceTo(pt) > 1e-6)
                    throw new RevitCommandException("point_off_face", "Floor-relative placement requires the computed insertion point on the wall face; projection would change the floor reference position or height.");
                instance = doc.Create.NewFamilyInstance(candidate.Reference, projectedPoint, direction, symbol);
                returnedHostId = wall.Id.Value;
            }
            else if (hostElem is Wall)
            {
                // Hosted overload: Revit auto-cuts the opening in the wall.
                instance = doc.Create.NewFamilyInstance(pt, symbol, hostElem, level, stype);

                // Phase must match host or Revit raises an infill warning and the wall cut fails.
                var hostPhaseId = hostElem.get_Parameter(BuiltInParameter.PHASE_CREATED)?.AsElementId();
                if (hostPhaseId != null && hostPhaseId != ElementId.InvalidElementId)
                    instance.get_Parameter(BuiltInParameter.PHASE_CREATED)?.Set(hostPhaseId);

                if (flipFacing) instance.flipFacing();
                if (flipHand)   instance.flipHand();

                returnedHostId = hostElem.Id.Value;
            }
            else
            {
                // hostId supplied but not a wall: fall back to non-hosted instead of
                // letting the hosted overload throw. Warn so the caller knows the
                // instance is free-standing (Host = -1), not silently mis-hosted.
                instance = doc.Create.NewFamilyInstance(pt, symbol, level, stype);
                hostWarning = $"hostId {rawHostId} is a {hostElem.GetType().Name}, not a Wall — " +
                              "placed non-hosted (no opening cut). Only wall hosting is supported.";
            }
        }
        else
        {
            instance = doc.Create.NewFamilyInstance(pt, symbol, level, stype);
            if (symbol.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased)
            {
                // Revit stores the point's Z as the offset from the level, which lifted level-based
                // families (countertops, shower boxes) by the level elevation again.
                doc.Regenerate();
                var offset = instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                if (offset is not null && !offset.IsReadOnly) offset.Set(pt.Z - level.Elevation);
            }
        }

        doc.Regenerate();
        var actualPoint = (instance.Location as LocationPoint)?.Point;
        if (topFaceZ is not null)
        {
            // Face-hosted instances may report the point on the face or raised by the host offset.
            var ok = actualPoint is not null && instance.Host?.Id.Value == returnedHostId
                && System.Math.Abs(actualPoint.X - pt.X) < 1e-6 && System.Math.Abs(actualPoint.Y - pt.Y) < 1e-6
                && (System.Math.Abs(actualPoint.Z - pt.Z) < 1e-4 || System.Math.Abs(actualPoint.Z - topFaceZ.Value) < 1e-4);
            if (!ok)
                throw new RevitCommandException("placement_mismatch", "Revit did not retain the requested floor face host or insertion point.");
            projectedPoint = null;
            explicitZ = false;
        }
        if (projectedPoint is not null && (actualPoint is null || actualPoint.DistanceTo(projectedPoint) > 1e-6
            || instance.Host?.Id.Value != returnedHostId))
            throw new RevitCommandException("placement_mismatch", "Revit did not retain the requested face host or insertion point.");
        if (explicitZ && (actualPoint is null || System.Math.Abs(actualPoint.Z - (projectedPoint ?? pt).Z) > 1e-6))
            throw new RevitCommandException("placement_mismatch",
                "Revit did not retain the requested insertion elevation. Check the family's placement type and hosting constraints.");

        var result = new JsonObject
        {
            ["placed"] = true,
            ["id"] = instance.Id.Value,
            ["familyName"] = symbol.FamilyName,
            ["familyTypeName"] = symbol.Name,
            ["familyTypeId"] = symbol.Id.Value,
            ["levelName"] = level.Name,
            ["placementType"] = symbol.Family.FamilyPlacementType.ToString(),
            ["actualHostId"] = instance.Host?.Id.Value,
            ["floorReference"] = !relativeToFloor ? null : new JsonObject
            {
                ["floorId"] = P.Long(p, "floorId"),
                ["surfaceElevationMeters"] = floorElevation!.Value / P.MetersToFeet,
                ["insertionHeightAboveFloorMeters"] = heightAboveFloorFeet!.Value / P.MetersToFeet,
                ["reference"] = "family_insertion_point",
            },
            ["hostSide"] = hostSide,
            ["projectedLocationFeet"] = projectedPoint is null ? null : new JsonObject
            {
                ["x"] = projectedPoint.X, ["y"] = projectedPoint.Y, ["z"] = projectedPoint.Z,
            },
            ["actualLocationFeet"] = actualPoint is null ? null : new JsonObject
            {
                ["x"] = actualPoint.X, ["y"] = actualPoint.Y, ["z"] = actualPoint.Z,
            },
        };

        if (returnedHostId.HasValue)
            result["hostId"] = returnedHostId.Value;

        if (hostWarning != null)
            result["hostWarning"] = hostWarning;

        if (usedFirstMatch)
            result["warning"] = $"Multiple types matched — used first result '{symbol.FamilyName} : {symbol.Name}'. " +
                                 "Provide both familyName and familyTypeName for deterministic placement.";

        return result;
    }
}
