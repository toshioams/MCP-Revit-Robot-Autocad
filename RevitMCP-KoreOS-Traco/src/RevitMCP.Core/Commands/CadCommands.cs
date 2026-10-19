using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Link (default) or import a DWG/DXF into a view. Asked for on 11/09/2026 to model a project
/// that only exists as a DWG. Link keeps the file external and removable; import embeds it.
/// Plan views get a view-specific underlay unless thisViewOnly:false. Reports the instance,
/// layers and extents in meters so inspect_cad can follow.
/// </summary>
public sealed class ImportCadCommand : IRevitCommand
{
    public string Name => "import_cad";
    public bool IsReadOnly => false;
    public string RiskLevel => "low";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var path = P.StrOrNull(p, "path");
        if (string.IsNullOrWhiteSpace(path)) throw new RevitCommandException("invalid_parameter", "path is required.");
        path = Path.GetFullPath(path!);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".dwg" or ".dxf")) throw new RevitCommandException("invalid_parameter", "Only .dwg and .dxf are supported.");
        if (!File.Exists(path)) throw new RevitCommandException("not_found", $"File not found: {path}");
        if (doc.IsFamilyDocument) throw new RevitCommandException("unsupported_document", "Import into a project document.");

        var mode = (P.StrOrNull(p, "mode") ?? "link").ToLowerInvariant();
        if (mode is not ("link" or "import")) throw new RevitCommandException("invalid_parameter", "mode: link | import.");

        View view;
        if (p["viewId"] is not null)
            view = doc.GetElement(new ElementId(P.Long(p, "viewId"))) as View
                ?? throw new RevitCommandException("invalid_parameter", "viewId must identify a view.");
        else
            view = ctx.App.ActiveUIDocument?.Document is { } active && DocumentResolver.Same(active, doc)
                ? active.ActiveView : throw new RevitCommandException("invalid_parameter", "Pass viewId (target document is not active).");
        if (view.IsTemplate || view is ViewSheet or ViewSchedule)
            throw new RevitCommandException("invalid_parameter", $"View '{view.Name}' cannot receive CAD.");

        var options = new DWGImportOptions
        {
            Unit = ParseUnit(P.StrOrNull(p, "unit")),
            Placement = ParsePlacement(P.StrOrNull(p, "placement")),
            ColorMode = ParseColor(P.StrOrNull(p, "colorMode")),
            ThisViewOnly = P.BoolOr(p, "thisViewOnly", view is ViewPlan),
            VisibleLayersOnly = P.BoolOr(p, "visibleLayersOnly", true),
            OrientToView = true,
            AutoCorrectAlmostVHLines = P.BoolOr(p, "autoCorrectAlmostVHLines", false),
        };
        if (p["customScale"] is not null) { options.Unit = ImportUnit.Custom; options.CustomScale = P.Dbl(p, "customScale"); }

        var ok = mode == "link"
            ? doc.Link(path, options, view, out ElementId id)
            : doc.Import(path, options, view, out id);
        if (!ok || id == ElementId.InvalidElementId || doc.GetElement(id) is not ImportInstance inst)
            throw new RevitCommandException("command_failed", $"Revit did not {mode} the file.");
        doc.Regenerate();

        var summary = CadGeometry.Collect(doc, inst, null, null);
        var result = new JsonObject
        {
            ["importInstanceId"] = inst.Id.Value, ["uniqueId"] = inst.UniqueId,
            ["typeId"] = inst.GetTypeId().Value, ["mode"] = mode, ["isLinked"] = inst.IsLinked,
            ["viewId"] = view.Id.Value, ["viewName"] = view.Name, ["viewSpecific"] = inst.ViewSpecific,
            ["unit"] = options.Unit.ToString(), ["placement"] = options.Placement.ToString(),
            ["levelElevationMeters"] = (view as ViewPlan)?.GenLevel?.Elevation / P.MetersToFeet,
        };
        foreach (var kv in summary.ToSummaryJson()) result[kv.Key] = kv.Value?.DeepClone();
        result["note"] = "Check extentsMeters against the drawing units (a 10 m house reading 10000 m means unit mm was needed). Texts/dimensions are not readable through the API.";
        return result;
    }

    static ImportUnit ParseUnit(string? s) => (s ?? "auto").ToLowerInvariant() switch
    {
        "auto" or "default" => ImportUnit.Default, "meter" or "m" => ImportUnit.Meter, "centimeter" or "cm" => ImportUnit.Centimeter,
        "millimeter" or "mm" => ImportUnit.Millimeter, "decimeter" or "dm" => ImportUnit.Decimeter,
        "foot" or "ft" => ImportUnit.Foot, "inch" or "in" => ImportUnit.Inch,
        _ => throw new RevitCommandException("invalid_parameter", "unit: auto | m | cm | mm | dm | ft | in."),
    };

    static ImportPlacement ParsePlacement(string? s) => (s ?? "origin").ToLowerInvariant() switch
    {
        "origin" => ImportPlacement.Origin, "center" or "centered" => ImportPlacement.Centered, "shared" => ImportPlacement.Shared,
        _ => throw new RevitCommandException("invalid_parameter", "placement: origin | center | shared."),
    };

    static ImportColorMode ParseColor(string? s) => (s ?? "blackAndWhite").ToLowerInvariant() switch
    {
        "blackandwhite" => ImportColorMode.BlackAndWhite, "preserve" or "preserved" => ImportColorMode.Preserved,
        "invert" or "invertcolors" => ImportColorMode.Inverted,
        _ => throw new RevitCommandException("invalid_parameter", "colorMode: blackAndWhite | preserve | invert."),
    };
}

/// <summary>
/// Read the geometry of a linked/imported CAD instance. Without importInstanceId: list CAD instances.
/// mode "summary": layers (counts, lengths, extents) and blocks. mode "curves": plan coordinates in
/// meters (x,y; z reported as range), filtered by layers and a bounding box, paginated.
/// Feeds revit_recipe_plan_walls_from_cad. DWG texts and dimensions are not exposed by the API.
/// </summary>
public sealed class InspectCadCommand : IRevitCommand
{
    public string Name => "inspect_cad";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        if (p["importInstanceId"] is null)
        {
            var all = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
            return new JsonObject
            {
                ["count"] = all.Count,
                ["instances"] = new JsonArray(all.Select(i => (JsonNode)new JsonObject
                {
                    ["id"] = i.Id.Value, ["name"] = doc.GetElement(i.GetTypeId())?.Name, ["isLinked"] = i.IsLinked,
                    ["viewSpecific"] = i.ViewSpecific, ["ownerViewId"] = i.ViewSpecific ? i.OwnerViewId.Value : null,
                    ["pinned"] = i.Pinned,
                }).ToArray()),
            };
        }
        var inst = doc.GetElement(new ElementId(P.Long(p, "importInstanceId"))) as ImportInstance
            ?? throw new RevitCommandException("invalid_parameter", "importInstanceId must identify a CAD import/link.");
        var mode = (P.StrOrNull(p, "mode") ?? "summary").ToLowerInvariant();
        HashSet<string>? layers = p["layers"] is JsonArray la
            ? la.Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        (double X0, double Y0, double X1, double Y1)? box = null;
        if (p["bboxMeters"] is JsonObject b)
            box = (P.Dbl(b, "minX"), P.Dbl(b, "minY"), P.Dbl(b, "maxX"), P.Dbl(b, "maxY"));

        var data = CadGeometry.Collect(doc, inst, layers, box);
        if (mode == "summary")
        {
            var r = data.ToSummaryJson();
            r["importInstanceId"] = inst.Id.Value;
            if (layers is not null) r["layerFilter"] = new JsonArray(layers.Select(l => (JsonNode)l).ToArray());
            return r;
        }
        if (mode != "curves") throw new RevitCommandException("invalid_parameter", "mode: summary | curves.");
        var offset = Math.Max(0, (int)P.DblOr(p, "offset", 0));
        var limit = Math.Clamp((int)P.DblOr(p, "limit", 2000), 1, 10000);
        var page = data.Items.Skip(offset).Take(limit).Select(i => i.ToJson()).ToArray();
        return new JsonObject
        {
            ["importInstanceId"] = inst.Id.Value, ["total"] = data.Items.Count, ["offset"] = offset, ["count"] = page.Length,
            ["hasMore"] = offset + page.Length < data.Items.Count, ["nextOffset"] = offset + page.Length,
            ["units"] = "meters", ["format"] = "t: line|arc|circle|poly|curve; l: layer; b: block; p: [x,y] points (arc: start, mid, end); c: center; r: radius",
            ["curves"] = new JsonArray(page),
        };
    }
}

/// <summary>Flattens CAD instance geometry (nested blocks included) into plan items by layer.</summary>
internal sealed class CadGeometry
{
    public sealed record Item(string Type, string Layer, string? Block, double[][] Pts, double[]? Center, double? Radius, double Length)
    {
        public JsonNode ToJson()
        {
            var o = new JsonObject { ["t"] = Type, ["l"] = Layer };
            if (Block is not null) o["b"] = Block;
            o["p"] = new JsonArray(Pts.Select(q => (JsonNode)new JsonArray(R(q[0]), R(q[1]))).ToArray());
            if (Center is not null) o["c"] = new JsonArray(R(Center[0]), R(Center[1]));
            if (Radius is not null) o["r"] = R(Radius.Value);
            return o;
        }
    }

    sealed class LayerStat
    {
        public int Lines, Arcs, Circles, Polylines, OtherCurves, Solids, Meshes, Points;
        public double Length, X0 = double.MaxValue, Y0 = double.MaxValue, X1 = double.MinValue, Y1 = double.MinValue;
        public void Grow(double x, double y) { X0 = Math.Min(X0, x); Y0 = Math.Min(Y0, y); X1 = Math.Max(X1, x); Y1 = Math.Max(Y1, y); }
    }

    public List<Item> Items { get; } = new();
    readonly SortedDictionary<string, LayerStat> _layers = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> _blocks = new(StringComparer.OrdinalIgnoreCase);
    double _z0 = double.MaxValue, _z1 = double.MinValue;
    int _maxDepth;
    const double F = P.MetersToFeet;

    static double R(double v) => Math.Round(v, 4);

    public static CadGeometry Collect(Document doc, ImportInstance inst, HashSet<string>? layers, (double X0, double Y0, double X1, double Y1)? box)
    {
        var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
        if (inst.ViewSpecific && doc.GetElement(inst.OwnerViewId) is View owner) options.View = owner;
        else options.DetailLevel = ViewDetailLevel.Fine;
        var geo = inst.get_Geometry(options)
            ?? throw new RevitCommandException("command_failed", "CAD instance has no geometry (not loaded or hidden?).");
        var g = new CadGeometry();
        g.Walk(doc, geo, null, 0, layers, box, new Dictionary<ElementId, string>());
        return g;
    }

    void Walk(Document? doc, GeometryElement ge, string? block, int depth, HashSet<string>? layers,
        (double X0, double Y0, double X1, double Y1)? box, Dictionary<ElementId, string> names)
    {
        _maxDepth = Math.Max(_maxDepth, depth);
        foreach (var obj in ge)
        {
            if (obj is GeometryInstance gi)
            {
                // depth 0 is the CAD file itself; deeper instances are DWG blocks.
                string? name = block;
                if (depth > 0 && doc is not null)
                {
                    var symId = gi.GetSymbolGeometryId().SymbolId;
                    name = doc.GetElement(symId)?.Name ?? $"block#{symId.Value}";
                    _blocks[name] = _blocks.TryGetValue(name, out var c) ? c + 1 : 1;
                }
                Walk(doc, gi.GetInstanceGeometry(), name, depth + 1, layers, box, names);
                continue;
            }
            var layer = LayerName(doc, obj.GraphicsStyleId, names);
            if (layers is not null && !layers.Contains(layer)) continue;
            if (!_layers.TryGetValue(layer, out var st)) _layers[layer] = st = new LayerStat();
            switch (obj)
            {
                case Line line when line.IsBound:
                    Add(st, "line", layer, block, new[] { line.GetEndPoint(0), line.GetEndPoint(1) }, null, null, line.Length, box, () => st.Lines++);
                    break;
                case Arc arc:
                    if (!arc.IsBound)
                        Add(st, "circle", layer, block, new[] { arc.Center + arc.XDirection * arc.Radius }, arc.Center, arc.Radius,
                            2 * Math.PI * arc.Radius, box, () => st.Circles++);
                    else
                        Add(st, "arc", layer, block, new[] { arc.GetEndPoint(0), arc.Evaluate(0.5, true), arc.GetEndPoint(1) }, arc.Center, arc.Radius,
                            arc.Length, box, () => st.Arcs++);
                    break;
                case PolyLine pl:
                {
                    var pts = pl.GetCoordinates().ToArray();
                    double len = 0; for (int i = 1; i < pts.Length; i++) len += pts[i].DistanceTo(pts[i - 1]);
                    Add(st, "poly", layer, block, pts, null, null, len, box, () => st.Polylines++);
                    break;
                }
                case Curve curve when curve.IsBound:
                    Add(st, "curve", layer, block, curve.Tessellate().ToArray(), null, null, curve.Length, box, () => st.OtherCurves++);
                    break;
                case Solid: st.Solids++; break;
                case Mesh: st.Meshes++; break;
                case Point: st.Points++; break;
            }
        }
    }

    void Add(LayerStat st, string type, string layer, string? block, XYZ[] pts, XYZ? center, double? radius, double lengthFt,
        (double X0, double Y0, double X1, double Y1)? box, Action count)
    {
        var m = pts.Select(q => new[] { q.X / F, q.Y / F }).ToArray();
        if (box is { } b && !m.Any(q => q[0] >= b.X0 && q[0] <= b.X1 && q[1] >= b.Y0 && q[1] <= b.Y1)) return;
        count();
        st.Length += lengthFt / F;
        foreach (var q in pts) { _z0 = Math.Min(_z0, q.Z / F); _z1 = Math.Max(_z1, q.Z / F); }
        foreach (var q in m) st.Grow(q[0], q[1]);
        Items.Add(new Item(type, layer, block, m, center is null ? null : new[] { center.X / F, center.Y / F }, radius / F, lengthFt / F));
    }

    static string LayerName(Document? doc, ElementId styleId, Dictionary<ElementId, string> cache)
    {
        if (doc is null || styleId == ElementId.InvalidElementId) return "(none)";
        if (cache.TryGetValue(styleId, out var n)) return n;
        n = (doc.GetElement(styleId) as GraphicsStyle)?.GraphicsStyleCategory?.Name ?? "(none)";
        cache[styleId] = n;
        return n;
    }

    public JsonObject ToSummaryJson()
    {
        var layerList = _layers.Where(kv => kv.Value.X0 != double.MaxValue || kv.Value.Solids + kv.Value.Meshes + kv.Value.Points > 0)
            .OrderByDescending(kv => kv.Value.Length).Select(kv => (JsonNode)new JsonObject
            {
                ["name"] = kv.Key, ["lines"] = kv.Value.Lines, ["arcs"] = kv.Value.Arcs, ["circles"] = kv.Value.Circles,
                ["polylines"] = kv.Value.Polylines, ["otherCurves"] = kv.Value.OtherCurves, ["solids"] = kv.Value.Solids,
                ["meshes"] = kv.Value.Meshes, ["points"] = kv.Value.Points, ["lengthMeters"] = R(kv.Value.Length),
                ["extents"] = kv.Value.X0 == double.MaxValue ? null
                    : new JsonArray(R(kv.Value.X0), R(kv.Value.Y0), R(kv.Value.X1), R(kv.Value.Y1)),
            }).ToArray();
        var withPts = _layers.Values.Where(s => s.X0 != double.MaxValue).ToList();
        return new JsonObject
        {
            ["itemCount"] = Items.Count, ["layerCount"] = layerList.Length,
            ["extentsMeters"] = withPts.Count == 0 ? null : new JsonObject
            {
                ["minX"] = R(withPts.Min(s => s.X0)), ["minY"] = R(withPts.Min(s => s.Y0)),
                ["maxX"] = R(withPts.Max(s => s.X1)), ["maxY"] = R(withPts.Max(s => s.Y1)),
                ["width"] = R(withPts.Max(s => s.X1) - withPts.Min(s => s.X0)), ["height"] = R(withPts.Max(s => s.Y1) - withPts.Min(s => s.Y0)),
            },
            ["zRangeMeters"] = _z0 == double.MaxValue ? null : new JsonArray(R(_z0), R(_z1)),
            ["nestingDepth"] = _maxDepth,
            ["layers"] = new JsonArray(layerList),
            ["blocks"] = new JsonArray(_blocks.OrderByDescending(kv => kv.Value).Take(100)
                .Select(kv => (JsonNode)new JsonObject { ["name"] = kv.Key, ["count"] = kv.Value }).ToArray()),
        };
    }
}
