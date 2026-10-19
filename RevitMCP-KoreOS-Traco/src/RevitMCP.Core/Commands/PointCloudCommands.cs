using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.PointClouds;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Link a ReCap point cloud (.rcp project or .rcs scan) into the project. Reuses a loaded type with the
/// same path. placement "origin" keeps the cloud coordinates; "center" moves the cloud so its plan centre
/// sits on the internal origin and its lowest point on the level (plus elevationOffset). Raw scans
/// (.e57/.las/.pts) must first become a ReCap project: revit_recipe_convert_scans (decap.exe).
/// </summary>
public sealed class LinkPointCloudCommand : IRevitCommand
{
    public string Name => "link_point_cloud";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var path = P.StrOrNull(p, "path");
        if (string.IsNullOrWhiteSpace(path)) throw new RevitCommandException("invalid_parameter", "path is required.");
        path = Path.GetFullPath(path!);
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (ext is not ("rcp" or "rcs"))
            throw new RevitCommandException("invalid_parameter", "Only ReCap .rcp/.rcs; convert raw scans first (revit_recipe_convert_scans).");
        if (!File.Exists(path)) throw new RevitCommandException("not_found", $"File not found: {path}");
        if (doc.IsFamilyDocument) throw new RevitCommandException("unsupported_document", "Link into a project document.");

        var type = new FilteredElementCollector(doc).OfClass(typeof(PointCloudType)).Cast<PointCloudType>()
            .FirstOrDefault(t => string.Equals(SafePath(t), path, StringComparison.OrdinalIgnoreCase));
        var reused = type is not null;
        type ??= PointCloudType.Create(doc, ext, path);
        var inst = PointCloudInstance.Create(doc, type.Id, Transform.Identity);
        doc.Regenerate();

        var placement = (P.StrOrNull(p, "placement") ?? "origin").ToLowerInvariant();
        var box = inst.get_BoundingBox(null);
        if (placement == "center")
        {
            if (box is null) throw new RevitCommandException("command_failed", "Point cloud has no extents (file not loaded?).");
            var level = CreateWallCommand.ResolveLevel(doc, P.StrOrNull(p, "levelName"));
            var targetZ = level.Elevation + P.DblOr(p, "elevationOffset", 0) * P.MetersToFeet;
            var move = new XYZ(-(box.Min.X + box.Max.X) / 2, -(box.Min.Y + box.Max.Y) / 2, targetZ - box.Min.Z);
            ElementTransformUtils.MoveElement(doc, inst.Id, move);
            doc.Regenerate();
            box = inst.get_BoundingBox(null);
        }
        else if (placement != "origin")
            throw new RevitCommandException("invalid_parameter", "placement: origin | center.");

        var result = PointCloudInfo.Describe(doc, inst, includeScans: false);
        result["reusedType"] = reused;
        result["placement"] = placement;
        result["note"] = "Check extentsMeters (units) and scans (duplicated scans show as sameOriginGroups in inspect_point_cloud).";
        return result;
    }

    internal static string? SafePath(PointCloudType t) { try { return ModelPathUtils.ConvertModelPathToUserVisiblePath(t.GetPath()); } catch { return null; } }
}

/// <summary>
/// Read-only: point cloud instances; with instanceId the scans (name, origin) and regions, plus scans that
/// share an origin within toleranceMeters or a name up to a copy suffix — the usual shape of a cloud whose
/// scans were imported or registered twice. inspect before sampling a duplicated cloud.
/// </summary>
public sealed class InspectPointCloudCommand : IRevitCommand
{
    public string Name => "inspect_point_cloud";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        if (p["instanceId"] is null)
        {
            var all = new FilteredElementCollector(doc).OfClass(typeof(PointCloudInstance)).Cast<PointCloudInstance>().ToList();
            return new JsonObject
            {
                ["count"] = all.Count,
                ["supportedEngines"] = new JsonArray(PointCloudEngineRegistry.GetSupportedEngines().Select(e => (JsonNode)e).ToArray()),
                ["instances"] = new JsonArray(all.Select(i => (JsonNode)PointCloudInfo.Describe(doc, i, includeScans: false)).ToArray()),
            };
        }
        var inst = doc.GetElement(new ElementId(P.Long(p, "instanceId"))) as PointCloudInstance
            ?? throw new RevitCommandException("invalid_parameter", "instanceId must identify a point cloud instance.");
        var r = PointCloudInfo.Describe(doc, inst, includeScans: true, tolerance: P.DblOr(p, "toleranceMeters", 0.05));
        return r;
    }
}

internal static class PointCloudInfo
{
    public static JsonObject Describe(Document doc, PointCloudInstance inst, bool includeScans, double tolerance = 0.05)
    {
        var type = doc.GetElement(inst.GetTypeId()) as PointCloudType;
        var box = inst.get_BoundingBox(null);
        var tf = inst.GetTransform();
        var o = new JsonObject
        {
            ["instanceId"] = inst.Id.Value, ["typeId"] = type?.Id.Value, ["name"] = type?.Name,
            ["path"] = type is null ? null : LinkPointCloudCommand.SafePath(type), ["engine"] = type?.EngineIdentifier,
            ["foundStatus"] = type?.FoundStatus.ToString(), ["hasColor"] = SafeBool(() => inst.HasColor()),
            ["supportsOverrides"] = inst.SupportsOverrides, ["pinned"] = inst.Pinned,
            ["transformOriginMeters"] = M(tf.Origin),
            ["rotationDegrees"] = Math.Round(Math.Atan2(tf.BasisX.Y, tf.BasisX.X) * 180 / Math.PI, 4),
            ["extentsMeters"] = box is null ? null : new JsonObject
            {
                ["min"] = M(box.Min), ["max"] = M(box.Max),
                ["size"] = new JsonArray(R((box.Max.X - box.Min.X) / P.MetersToFeet), R((box.Max.Y - box.Min.Y) / P.MetersToFeet), R((box.Max.Z - box.Min.Z) / P.MetersToFeet)),
            },
        };
        IList<string> scans = SafeList(() => inst.GetScans());
        IList<string> regions = SafeList(() => inst.GetRegions());
        o["scanCount"] = scans.Count; o["regionCount"] = regions.Count;
        if (!includeScans) return o;

        var withOrigin = scans.Select(s => (name: s, origin: SafeXyz(() => inst.GetScanOrigin(s)))).ToList();
        o["scans"] = new JsonArray(withOrigin.Select(s => (JsonNode)new JsonObject { ["name"] = s.name, ["originMeters"] = s.origin is null ? null : M(s.origin) }).ToArray());
        o["regions"] = new JsonArray(regions.Select(r => (JsonNode)r).ToArray());

        // duplicates: same scanner position, or same name once copy suffixes are stripped
        var tol = tolerance * P.MetersToFeet;
        var groups = new List<List<string>>();
        foreach (var s in withOrigin.Where(s => s.origin is not null))
        {
            var g = groups.FirstOrDefault(gr => withOrigin.First(x => x.name == gr[0]).origin!.DistanceTo(s.origin!) <= tol);
            if (g is null) groups.Add(new List<string> { s.name }); else g.Add(s.name);
        }
        o["sameOriginGroups"] = new JsonArray(groups.Where(g => g.Count > 1).Select(g => (JsonNode)new JsonArray(g.Select(n => (JsonNode)n).ToArray())).ToArray());
        o["sameNameGroups"] = new JsonArray(scans.GroupBy(BaseName).Where(g => g.Count() > 1)
            .Select(g => (JsonNode)new JsonArray(g.Select(n => (JsonNode)n).ToArray())).ToArray());
        return o;
    }

    /// <summary>"Scan_012 (1)", "Scan_012_copy", "Scan_012-2" -> "scan_012". Pure, unit tested.</summary>
    public static string BaseName(string name)
    {
        var s = name.Trim().ToLowerInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(\s*\(\d+\)|[\s_-]*(copy|copia|cópia|dup|duplicate)(\s*\d+)?|[\s_-]+\d{1,2})$", "");
        return s;
    }

    static bool? SafeBool(Func<bool> f) { try { return f(); } catch { return null; } }
    static IList<string> SafeList(Func<IList<string>> f) { try { return f() ?? new List<string>(); } catch { return new List<string>(); } }
    static XYZ? SafeXyz(Func<XYZ> f) { try { return f(); } catch { return null; } }
    static double R(double v) => Math.Round(v, 4);
    public static JsonArray M(XYZ q) => new(R(q.X / P.MetersToFeet), R(q.Y / P.MetersToFeet), R(q.Z / P.MetersToFeet));
}

/// <summary>
/// View-specific point cloud display: visibility and colour mode of the whole instance, of named scans or of
/// regions. Hide the duplicated scans in a working view, or colour by elevation to read floors and ceilings.
/// </summary>
public sealed class SetPointCloudDisplayCommand : IRevitCommand
{
    public string Name => "set_point_cloud_display";
    public bool IsReadOnly => false;
    public string RiskLevel => "low";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var view = doc.GetElement(new ElementId(P.Long(p, "viewId"))) as View
            ?? throw new RevitCommandException("invalid_parameter", "viewId must identify a view.");
        var inst = doc.GetElement(new ElementId(P.Long(p, "instanceId"))) as PointCloudInstance
            ?? throw new RevitCommandException("invalid_parameter", "instanceId must identify a point cloud instance.");
        if (!inst.SupportsOverrides) throw new RevitCommandException("unsupported_type", "This point cloud does not support overrides.");
        var overrides = view.GetPointCloudOverrides();

        PointCloudOverrideSettings Build()
        {
            var s = new PointCloudOverrideSettings();
            if (p["visible"] is not null) s.Visible = P.BoolOr(p, "visible", true);
            var mode = (P.StrOrNull(p, "colorMode") ?? "noOverride").ToLowerInvariant() switch
            {
                "nooverride" => PointCloudColorMode.NoOverride, "elevation" => PointCloudColorMode.Elevation,
                "intensity" => PointCloudColorMode.Intensity, "normals" => PointCloudColorMode.Normals, "fixedcolor" => PointCloudColorMode.FixedColor,
                _ => throw new RevitCommandException("invalid_parameter", "colorMode: noOverride | elevation | intensity | normals | fixedColor."),
            };
            s.ColorMode = mode;
            if (mode == PointCloudColorMode.FixedColor && p["color"] is JsonObject c)
            {
                var color = new Color((byte)P.IntOr(c, "r", 255), (byte)P.IntOr(c, "g", 0), (byte)P.IntOr(c, "b", 0));
                s.SetModeOverride(mode, new PointCloudColorSettings(color, color)); // Color1/Color2 are read-only
            }
            return s;
        }

        var changed = new JsonArray();
        var scans = (p["scans"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList();
        var regions = (p["regions"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList();
        if (scans is null && regions is null)
        {
            overrides.SetPointCloudScanOverrideSettings(inst.Id, Build());
            changed.Add("(whole instance)");
        }
        foreach (var scan in scans ?? new List<string>())
        {
            if (!inst.ContainsScan(scan)) throw new RevitCommandException("not_found", $"Scan '{scan}' is not in the instance.");
            overrides.SetPointCloudScanOverrideSettings(inst.Id, Build(), scan, doc);
            changed.Add("scan:" + scan);
        }
        var known = inst.GetRegions();
        foreach (var region in regions ?? new List<string>())
        {
            if (!known.Contains(region)) throw new RevitCommandException("not_found", $"Region '{region}' is not in the instance.");
            overrides.SetPointCloudRegionOverrideSettings(inst.Id, Build(), region, doc);
            changed.Add("region:" + region);
        }
        return new JsonObject { ["viewId"] = view.Id.Value, ["instanceId"] = inst.Id.Value, ["applied"] = changed,
            ["visible"] = p["visible"]?.DeepClone(), ["colorMode"] = P.StrOrNull(p, "colorMode") ?? "noOverride" };
    }
}

/// <summary>
/// Read points inside a model box (meters). Summary: count, extents, a z histogram (floors and ceilings are
/// the spikes). outFile writes "x y z" meters (mm precision) for the Node planners; inline returns at most
/// inlineLimit points. averageDistance controls density (Revit returns fewer points for larger values).
/// A duplicated cloud reads every copy: dedupe/shift analysis happens in the planner.
/// </summary>
public sealed class SamplePointCloudCommand : IRevitCommand
{
    public string Name => "sample_point_cloud";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var inst = doc.GetElement(new ElementId(P.Long(p, "instanceId"))) as PointCloudInstance
            ?? throw new RevitCommandException("invalid_parameter", "instanceId must identify a point cloud instance.");
        var cloudBox = inst.get_BoundingBox(null) ?? throw new RevitCommandException("command_failed", "Point cloud has no extents (file not found?).");
        XYZ min, max;
        if (p["boxMeters"] is JsonObject b)
        {
            min = new XYZ(P.DblOr(b, "minX", cloudBox.Min.X / P.MetersToFeet), P.DblOr(b, "minY", cloudBox.Min.Y / P.MetersToFeet), P.DblOr(b, "minZ", cloudBox.Min.Z / P.MetersToFeet)) * P.MetersToFeet;
            max = new XYZ(P.DblOr(b, "maxX", cloudBox.Max.X / P.MetersToFeet), P.DblOr(b, "maxY", cloudBox.Max.Y / P.MetersToFeet), P.DblOr(b, "maxZ", cloudBox.Max.Z / P.MetersToFeet)) * P.MetersToFeet;
        }
        else { min = cloudBox.Min; max = cloudBox.Max; }
        if (max.X <= min.X || max.Y <= min.Y || max.Z <= min.Z) throw new RevitCommandException("invalid_parameter", "boxMeters must have max > min on every axis.");

        var avg = Math.Max(0.001, P.DblOr(p, "averageDistanceMeters", 0.01)) * P.MetersToFeet;
        var maxPoints = Math.Clamp(P.IntOr(p, "maxPoints", 500_000), 1, 20_000_000);
        // Revit reads at most PerCall points per GetPoints (live: 1,000,000 was refused): larger requests are
        // split into a grid of plan tiles, each read with its own limit.
        const int PerCall = 999_000;
        var tileCount = (int)Math.Ceiling(maxPoints / (double)PerCall);
        var cols = (int)Math.Ceiling(Math.Sqrt(tileCount));
        var rows = (int)Math.Ceiling(tileCount / (double)cols);
        var perTile = Math.Min(PerCall, (int)Math.Ceiling(maxPoints / (double)(cols * rows)));
        // Verified live on a cloud linked with an offset: the filter planes are in model coordinates, the
        // points come back in the cloud's own coordinates and the instance transform maps them to the model.
        var tf = inst.GetTransform();
        var identity = tf.IsIdentity;
        static Plane CloudPlane(XYZ normal, XYZ origin) => Plane.CreateByNormalAndOrigin(normal, origin);
        IEnumerable<PointCollection> Collections()
        {
            var dx = (max.X - min.X) / cols; var dy = (max.Y - min.Y) / rows;
            for (var i = 0; i < cols; i++)
            for (var j = 0; j < rows; j++)
            {
                var tMin = new XYZ(min.X + i * dx, min.Y + j * dy, min.Z);
                var tMax = new XYZ(i == cols - 1 ? max.X : min.X + (i + 1) * dx, j == rows - 1 ? max.Y : min.Y + (j + 1) * dy, max.Z);
                var planes = new List<Plane>
                {
                    CloudPlane(XYZ.BasisX, tMin), CloudPlane(-XYZ.BasisX, tMax),
                    CloudPlane(XYZ.BasisY, tMin), CloudPlane(-XYZ.BasisY, tMax),
                    CloudPlane(XYZ.BasisZ, tMin), CloudPlane(-XYZ.BasisZ, tMax),
                };
                yield return inst.GetPoints(PointCloudFilterFactory.CreateMultiPlaneFilter(planes), avg, perTile);
            }
        }

        var binM = P.DblOr(p, "zBinMeters", 0.02);
        var zMin = min.Z / P.MetersToFeet; var zMax = max.Z / P.MetersToFeet;
        var bins = new int[Math.Max(1, (int)Math.Ceiling((zMax - zMin) / binM))];
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        var inlineLimit = Math.Clamp(P.IntOr(p, "inlineLimit", 0), 0, 20_000);
        var inline = new JsonArray();
        var outFile = P.StrOrNull(p, "outFile");
        StreamWriter? writer = null;
        if (!string.IsNullOrWhiteSpace(outFile))
        {
            outFile = Path.GetFullPath(outFile!);
            Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
            writer = new StreamWriter(outFile, false, new UTF8Encoding(false), 1 << 20);
        }
        var inv = CultureInfo.InvariantCulture;
        var withColor = P.BoolOr(p, "includeColor", false);
        int count = 0, tilesAtLimit = 0;
        try
        {
            foreach (var collection in Collections())
            {
            if (collection.Count >= perTile) tilesAtLimit++;
            foreach (CloudPoint cp in collection)
            {
                var q = new XYZ(cp.X, cp.Y, cp.Z);
                if (!identity) q = tf.OfPoint(q);
                double x = q.X / P.MetersToFeet, y = q.Y / P.MetersToFeet, z = q.Z / P.MetersToFeet;
                count++;
                if (x < x0) x0 = x; if (y < y0) y0 = y; if (z < z0) z0 = z;
                if (x > x1) x1 = x; if (y > y1) y1 = y; if (z > z1) z1 = z;
                var bi = (int)((z - zMin) / binM);
                if (bi >= 0 && bi < bins.Length) bins[bi]++;
                if (writer is not null)
                {
                    writer.Write(x.ToString("0.###", inv)); writer.Write(' ');
                    writer.Write(y.ToString("0.###", inv)); writer.Write(' ');
                    writer.Write(z.ToString("0.###", inv));
                    if (withColor) { writer.Write(' '); writer.Write(cp.Color.ToString(inv)); }
                    writer.Write('\n');
                }
                if (inline.Count < inlineLimit) inline.Add(new JsonArray(Math.Round(x, 3), Math.Round(y, 3), Math.Round(z, 3)));
            }
            }
        }
        finally { writer?.Dispose(); }

        return new JsonObject
        {
            ["instanceId"] = inst.Id.Value, ["count"] = count, ["maxPoints"] = maxPoints, ["hitLimit"] = tilesAtLimit > 0,
            ["tiles"] = cols * rows, ["tilesAtLimit"] = tilesAtLimit,
            ["averageDistanceMeters"] = Math.Round(avg / P.MetersToFeet, 4), ["transformApplied"] = !identity,
            ["boxMeters"] = new JsonObject { ["min"] = PointCloudInfo.M(min), ["max"] = PointCloudInfo.M(max) },
            ["extentsMeters"] = count == 0 ? null : new JsonArray(Math.Round(x0, 3), Math.Round(y0, 3), Math.Round(z0, 3), Math.Round(x1, 3), Math.Round(y1, 3), Math.Round(z1, 3)),
            ["zHistogram"] = new JsonObject { ["fromMeters"] = Math.Round(zMin, 4), ["binMeters"] = binM, ["counts"] = new JsonArray(bins.Select(v => (JsonNode)v).ToArray()) },
            ["outFile"] = outFile, ["format"] = writer is null ? null : (withColor ? "x y z color(argb int), meters" : "x y z, meters"),
            ["points"] = inlineLimit > 0 ? inline : null,
            ["note"] = count >= maxPoints ? "Hit maxPoints: raise it or the averageDistance, or shrink the box; results beyond the limit are not consistent between calls." : null,
        };
    }
}
