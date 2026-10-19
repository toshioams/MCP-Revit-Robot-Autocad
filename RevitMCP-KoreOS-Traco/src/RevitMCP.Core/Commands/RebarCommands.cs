using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitMCPAddin.Commands;

/// <summary>Shared lookups for the reinforcement commands. Lengths leave in metres, diameters in mm.</summary>
internal static class RebarUtil
{
    public const double FtToMm = 304.8;

    public static double DiameterMm(RebarBarType t) => Math.Round(t.BarNominalDiameter * FtToMm, 2);

    public static string? Grade(Document doc, RebarBarType t)
    {
        var g = Nbr6118.GradeFromName(t.Name);
        if (g is not null) return g;
        var mat = t.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.AsElementId();
        return mat is null || mat == ElementId.InvalidElementId ? null : Nbr6118.GradeFromName(doc.GetElement(mat)?.Name);
    }

    public static List<RebarBarType> BarTypes(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();

    /// <summary>
    /// Bar type from {barTypeName} or {diameterMm, grade (default CA-50)} in <paramref name="spec"/>.
    /// Never creates a type: lists the available ones when nothing matches.
    /// </summary>
    public static RebarBarType ResolveBarType(Document doc, JsonObject spec, string label)
    {
        var all = BarTypes(doc);
        var name = P.StrOrNull(spec, "barTypeName");
        if (name is not null)
            return all.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new RevitCommandException("not_found", $"{label}: bar type '{name}' not found. Available: {string.Join(", ", all.Select(t => t.Name))}.");
        var d = P.Dbl(spec, "diameterMm");
        var grade = P.StrOrNull(spec, "grade") ?? "CA-50";
        var byDiameter = all.Where(t => Math.Abs(DiameterMm(t) - d) < 0.05).ToList();
        var match = byDiameter.Where(t => Grade(doc, t) == grade).ToList();
        if (match.Count == 0 && byDiameter.Count == 1 && Grade(doc, byDiameter[0]) is null) match = byDiameter;
        if (match.Count == 0)
            throw new RevitCommandException("not_found",
                $"{label}: no {grade} bar type with nominal diameter {d} mm. Available: {string.Join(", ", all.Select(t => $"{t.Name} ({DiameterMm(t)} mm)"))}.");
        return match.OrderBy(t => t.Name.Length).First();
    }

    public static JsonObject BarTypeJson(Document doc, RebarBarType t) => new()
    {
        ["id"] = t.Id.Value,
        ["name"] = t.Name,
        ["diameterMm"] = DiameterMm(t),
        ["modelDiameterMm"] = Math.Round(t.BarModelDiameter * FtToMm, 2),
        ["grade"] = Grade(doc, t),
        ["standardBendDiameterMm"] = Math.Round(t.StandardBendDiameter * FtToMm, 1),
        ["hookBendDiameterMm"] = Math.Round(t.StandardHookBendDiameter * FtToMm, 1),
        ["stirrupBendDiameterMm"] = Math.Round(t.StirrupTieBendDiameter * FtToMm, 1),
        ["massKgPerM"] = Math.Round(Nbr6118.MassPerMetre(DiameterMm(t)), 4),
    };

    public static ElementId HostId(Document doc, Element e) => e switch
    {
        Rebar r => r.GetHostId(),
        RebarInSystem ris => doc.GetElement(ris.SystemId) switch
        {
            AreaReinforcement a => a.GetHostId(),
            PathReinforcement p => p.GetHostId(),
            _ => ElementId.InvalidElementId,
        },
        _ => ElementId.InvalidElementId,
    };

    public static double CoverMm(Document doc, Element host, BuiltInParameter bip)
    {
        var id = host.get_Parameter(bip)?.AsElementId();
        return id is not null && doc.GetElement(id) is RebarCoverType c ? Math.Round(c.CoverDistance * FtToMm, 1) : double.NaN;
    }

    public static readonly (string key, BuiltInParameter bip)[] CoverParams =
    {
        ("top", BuiltInParameter.CLEAR_COVER_TOP), ("bottom", BuiltInParameter.CLEAR_COVER_BOTTOM),
        ("exterior", BuiltInParameter.CLEAR_COVER_EXTERIOR), ("interior", BuiltInParameter.CLEAR_COVER_INTERIOR),
        ("other", BuiltInParameter.CLEAR_COVER_OTHER),
    };

    public static JsonArray Ids(IEnumerable<ElementId> ids) => new(ids.Select(i => (JsonNode)i.Value).ToArray());
}

/// <summary>Read-only: everything reinforcement-related the document offers (types, shapes, covers, tags, schedules).</summary>
public sealed class ListRebarTypesCommand : IRevitCommand
{
    public string Name => "list_rebar_types";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var includeShapes = P.BoolOr(ctx.Parameters, "includeShapes", true);
        JsonArray Named<T>(Func<T, JsonObject>? extra = null) where T : Element =>
            new(new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().OrderBy(e => e.Name)
                .Select(e => { var o = extra?.Invoke(e) ?? new JsonObject(); o["id"] = e.Id.Value; o["name"] = e.Name; return (JsonNode)o; }).ToArray());

        var bars = new JsonArray(RebarUtil.BarTypes(doc).OrderBy(RebarUtil.DiameterMm).ThenBy(t => t.Name)
            .Select(t => (JsonNode)RebarUtil.BarTypeJson(doc, t)).ToArray());
        var shapes = new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>().OrderBy(s => s.Name).ToList();

        return new JsonObject
        {
            ["barTypes"] = bars,
            ["hookTypes"] = Named<RebarHookType>(h => new JsonObject
            {
                ["angleDeg"] = Math.Round(h.HookAngle * 180 / Math.PI, 1), ["style"] = h.Style.ToString(),
                ["straightLineMultiplier"] = h.StraightLineMultiplier,
            }),
            ["coverTypes"] = Named<RebarCoverType>(c => new JsonObject { ["coverMm"] = Math.Round(c.CoverDistance * RebarUtil.FtToMm, 1) }),
            ["shapeCount"] = shapes.Count,
            ["shapes"] = includeShapes ? new JsonArray(shapes.Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s.Id.Value, ["name"] = s.Name, ["style"] = s.RebarStyle.ToString(),
                ["simpleLine"] = s.SimpleLine, ["simpleArc"] = s.SimpleArc,
            }).ToArray()) : null,
            ["areaReinforcementTypes"] = Named<AreaReinforcementType>(),
            ["pathReinforcementTypes"] = Named<PathReinforcementType>(),
            ["fabricSheetTypes"] = Named<FabricSheetType>(f => new JsonObject
            {
                ["majorSpacingMm"] = Math.Round(f.MajorSpacing * RebarUtil.FtToMm, 1),
                ["minorSpacingMm"] = Math.Round(f.MinorSpacing * RebarUtil.FtToMm, 1),
                ["lengthM"] = Math.Round(f.OverallLength * P.FeetToMeters, 3),
                ["widthM"] = Math.Round(f.OverallWidth * P.FeetToMeters, 3),
            }),
            ["spliceTypes"] = new JsonArray(RebarSpliceTypeUtils.GetAllRebarSpliceTypes(doc).Select(id => (JsonNode)new JsonObject
            {
                ["id"] = id.Value, ["name"] = doc.GetElement(id)?.Name,
                ["lapLengthMultiplier"] = RebarSpliceTypeUtils.GetLapLengthMultiplier(doc, id),
                ["staggerLengthMultiplier"] = RebarSpliceTypeUtils.GetStaggerLengthMultiplier(doc, id),
            }).ToArray()),
            ["rebarTags"] = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_RebarTags).Cast<FamilySymbol>()
                .Select(s => (JsonNode)new JsonObject { ["id"] = s.Id.Value, ["family"] = s.FamilyName, ["type"] = s.Name }).ToArray()),
            ["rebarSchedules"] = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(v => !v.IsTemplate && v.Definition.CategoryId.Value == (long)BuiltInCategory.OST_Rebar)
                .Select(v => (JsonNode)new JsonObject { ["id"] = v.Id.Value, ["name"] = v.Name }).ToArray()),
        };
    }
}

/// <summary>
/// Read-only: reinforcement in the model (or a view, or some hosts) with quantity, length and steel
/// mass, summarised by grade and diameter. Mass = total bar length x nominal section x 7850 kg/m3.
/// </summary>
public sealed class GetRebarsCommand : IRevitCommand
{
    public string Name => "get_rebars";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var viewId = P.LongOrNull(p, "viewId");
        var hostIds = p["hostIds"] is JsonArray ha ? ha.Select(n => P.LongFrom(n, "hostIds")).ToHashSet() : null;
        var partition = P.StrOrNull(p, "partition");
        var includeItems = P.BoolOr(p, "includeItems", true);
        var limit = P.IntOr(p, "limit", 500);

        var collector = viewId is null ? new FilteredElementCollector(doc) : new FilteredElementCollector(doc, new ElementId(viewId.Value));
        var elements = collector.OfCategory(BuiltInCategory.OST_Rebar).WhereElementIsNotElementType()
            .Where(e => e is Rebar or RebarInSystem).ToList();

        var items = new JsonArray();
        var groups = new SortedDictionary<string, (string? grade, double d, int bars, double len, double kg)>();
        int sets = 0, bars = 0; double length = 0, mass = 0;
        foreach (var e in elements)
        {
            var host = RebarUtil.HostId(doc, e);
            if (hostIds is not null && !hostIds.Contains(host.Value)) continue;
            var part = e.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString();
            if (partition is not null && !string.Equals(part, partition, StringComparison.OrdinalIgnoreCase)) continue;

            var type = doc.GetElement(e.GetTypeId()) as RebarBarType;
            var d = type is null ? 0 : RebarUtil.DiameterMm(type);
            var grade = type is null ? null : RebarUtil.Grade(doc, type);
            var qty = e.get_Parameter(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS)?.AsInteger() ?? 0;
            var total = (e.get_Parameter(BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH)?.AsDouble() ?? 0) * P.FeetToMeters;
            var barLen = (e.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH)?.AsDouble() ?? 0) * P.FeetToMeters;
            var kg = total * Nbr6118.MassPerMetre(d);
            sets++; bars += qty; length += total; mass += kg;
            var key = $"{grade ?? "?"}|{d:000.00}";
            var g = groups.TryGetValue(key, out var acc) ? acc : (grade, d, 0, 0.0, 0.0);
            groups[key] = (grade, d, g.Item3 + qty, g.Item4 + total, g.Item5 + kg);

            if (includeItems && items.Count < limit)
            {
                var shapeId = e is Rebar r ? r.GetShapeId() : (e as RebarInSystem)?.RebarShapeId;
                items.Add(new JsonObject
                {
                    ["id"] = e.Id.Value,
                    ["kind"] = e is RebarInSystem ? "areaOrPath" : (e is Rebar fr && fr.IsRebarFreeForm() ? "freeForm" : "shapeDriven"),
                    ["hostId"] = host.Value,
                    ["number"] = e.get_Parameter(BuiltInParameter.REBAR_NUMBER)?.AsString(),
                    ["partition"] = part,
                    ["barType"] = type?.Name,
                    ["grade"] = grade,
                    ["diameterMm"] = d,
                    ["shape"] = shapeId is null || shapeId == ElementId.InvalidElementId ? null : doc.GetElement(shapeId)?.Name,
                    ["quantity"] = qty,
                    ["barLengthM"] = Math.Round(barLen, 3),
                    ["totalLengthM"] = Math.Round(total, 3),
                    ["layout"] = e.get_Parameter(BuiltInParameter.REBAR_ELEM_LAYOUT_RULE)?.AsValueString(),
                    ["spacingM"] = e.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING) is { HasValue: true } sp ? Math.Round(sp.AsDouble() * P.FeetToMeters, 4) : null,
                    ["massKg"] = Math.Round(kg, 2),
                });
            }
        }

        return new JsonObject
        {
            ["rebarSets"] = sets,
            ["bars"] = bars,
            ["totalLengthM"] = Math.Round(length, 2),
            ["totalMassKg"] = Math.Round(mass, 2),
            ["byDiameter"] = new JsonArray(groups.Values.Select(g => (JsonNode)new JsonObject
            {
                ["grade"] = g.grade, ["diameterMm"] = g.d, ["bars"] = g.bars,
                ["lengthM"] = Math.Round(g.len, 2), ["massKg"] = Math.Round(g.kg, 2),
            }).ToArray()),
            ["items"] = includeItems ? items : null,
            ["itemsTruncated"] = includeItems && sets > items.Count,
            ["massNote"] = "Nominal section x 7850 kg/m3 (NBR 7480); no allowance for losses.",
        };
    }
}

/// <summary>Read-only: can these elements host rebar, which covers they use and how much rebar they hold.</summary>
public sealed class GetRebarHostInfoCommand : IRevitCommand
{
    public string Name => "get_rebar_host_info";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var result = new JsonArray();
        foreach (var node in P.Arr(ctx.Parameters, "elementIds"))
        {
            var id = P.LongFrom(node, "elementIds");
            var e = doc.GetElement(new ElementId(id));
            if (e is null) { result.Add(new JsonObject { ["id"] = id, ["error"] = "not_found" }); continue; }
            var data = RebarHostData.GetRebarHostData(e);
            var covers = new JsonObject();
            foreach (var (key, bip) in RebarUtil.CoverParams)
            {
                var mm = RebarUtil.CoverMm(doc, e, bip);
                if (!double.IsNaN(mm)) covers[key] = mm;
            }
            var hosted = data?.GetRebarsInHost() ?? new List<Rebar>();
            var matId = e.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)?.AsElementId();
            result.Add(new JsonObject
            {
                ["id"] = id,
                ["category"] = e.Category?.Name,
                ["name"] = e.Name,
                ["isValidHost"] = data is not null && data.IsValidHost(),
                ["structuralMaterial"] = matId is null || matId == ElementId.InvalidElementId ? null : doc.GetElement(matId)?.Name,
                ["coversMm"] = covers,
                ["rebarCount"] = hosted.Count,
                ["rebarIds"] = RebarUtil.Ids(hosted.Take(200).Select(r => r.Id)),
            });
        }
        return new JsonObject { ["hosts"] = result };
    }
}

/// <summary>
/// Read-only: checks the open document (a project made from the office template) against the
/// NBR 7480 bar series and the NBR 6118 minimums, and lists what is missing.
/// </summary>
public sealed class AuditRebarTemplateCommand : IRevitCommand
{
    public string Name => "audit_rebar_template";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var gaps = new JsonArray();
        void Gap(string area, string item, string detail) =>
            gaps.Add(new JsonObject { ["area"] = area, ["item"] = item, ["detail"] = detail });

        var bars = RebarUtil.BarTypes(doc);
        foreach (var (grade, series) in new[] { ("CA-50", Nbr6118.Ca50), ("CA-60", Nbr6118.Ca60) })
            foreach (var d in series)
                if (!bars.Any(t => Math.Abs(RebarUtil.DiameterMm(t) - d) < 0.05 && RebarUtil.Grade(doc, t) == grade))
                    Gap("barTypes", $"{grade} {d} mm", "Bitola da NBR 7480 sem tipo de vergalhão.");
        foreach (var t in bars)
        {
            var d = RebarUtil.DiameterMm(t); var grade = RebarUtil.Grade(doc, t);
            if (grade is null) Gap("barTypes", t.Name, "Aço não identificado (nome/material sem CA-50/CA-60).");
            var named = System.Text.RegularExpressions.Regex.Match(t.Name, @"^\s*(\d+(?:[.,]\d+)?)");
            if (named.Success && double.TryParse(named.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var nameMm) && Math.Abs(nameMm - d) > 0.05)
                Gap("barTypes", t.Name, $"Nome indica {nameMm} mm, mas o diâmetro nominal do tipo é {d} mm (peso e desenho saem errados).");
            var bend = Nbr6118.MinBendDiameterMm(d, grade, stirrup: false);
            var stirrup = Nbr6118.MinBendDiameterMm(d, grade, stirrup: true);
            if (t.StandardBendDiameter * RebarUtil.FtToMm < bend - 0.5)
                Gap("bends", t.Name, $"Dobra padrão {t.StandardBendDiameter * RebarUtil.FtToMm:0.#} mm < mínimo NBR {bend:0.#} mm.");
            if (t.StandardHookBendDiameter * RebarUtil.FtToMm < bend - 0.5)
                Gap("bends", t.Name, $"Dobra de gancho {t.StandardHookBendDiameter * RebarUtil.FtToMm:0.#} mm < mínimo NBR {bend:0.#} mm.");
            if (t.StirrupTieBendDiameter * RebarUtil.FtToMm < stirrup - 0.5)
                Gap("bends", t.Name, $"Dobra de estribo {t.StirrupTieBendDiameter * RebarUtil.FtToMm:0.#} mm < mínimo NBR {stirrup:0.#} mm.");
        }

        var hooks = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().ToList();
        foreach (var (angle, style) in new[] { (90, RebarStyle.Standard), (180, RebarStyle.Standard), (90, RebarStyle.StirrupTie), (135, RebarStyle.StirrupTie) })
            if (!hooks.Any(h => Math.Abs(h.HookAngle * 180 / Math.PI - angle) < 0.5 && h.Style == style))
                Gap("hooks", $"{angle}° {style}", "Gancho usual sem tipo definido.");

        var covers = new FilteredElementCollector(doc).OfClass(typeof(RebarCoverType)).Cast<RebarCoverType>().ToList();
        foreach (var mm in Nbr6118.AllNominalCoversMm())
            if (!covers.Any(c => Math.Abs(c.CoverDistance * RebarUtil.FtToMm - mm) < 0.5))
                Gap("covers", $"{mm} mm", "Cobrimento da tabela 7.2 (CAA I a IV, com/sem controle rigoroso) sem tipo.");

        var shapes = new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>().ToList();
        if (!shapes.Any(s => s.SimpleLine)) Gap("shapes", "reta", "Nenhuma forma de barra reta simples.");
        if (!shapes.Any(s => s.SimpleArc)) Gap("shapes", "arco", "Nenhuma forma em arco (anéis de paredes curvas).");
        if (!shapes.Any(s => s.RebarStyle == RebarStyle.StirrupTie)) Gap("shapes", "estribo", "Nenhuma forma de estribo.");

        var tags = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_RebarTags).GetElementCount();
        if (tags == 0) Gap("documentation", "etiqueta de vergalhão", "Nenhuma família de etiqueta de vergalhão carregada.");
        var schedules = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .Count(v => !v.IsTemplate && v.Definition.CategoryId.Value == (long)BuiltInCategory.OST_Rebar);
        if (schedules == 0) Gap("documentation", "tabela de vergalhões", "Nenhuma tabela da categoria Vergalhão estrutural.");
        var fabric = new FilteredElementCollector(doc).OfClass(typeof(FabricSheetType)).GetElementCount();
        if (fabric == 0) Gap("fabric", "telas soldadas", "Nenhum tipo de tela soldada (Fabric Sheet Type).");
        if (RebarSpliceTypeUtils.GetAllRebarSpliceTypes(doc).Count == 0)
            Gap("splices", "tipo de emenda", "Nenhum tipo de emenda por traspasse (Rebar Splice Type).");

        return new JsonObject
        {
            ["document"] = doc.Title,
            ["gapCount"] = gaps.Count,
            ["inventory"] = new JsonObject
            {
                ["barTypes"] = bars.Count, ["hookTypes"] = hooks.Count, ["coverTypes"] = covers.Count,
                ["shapes"] = shapes.Count, ["rebarTags"] = tags, ["rebarSchedules"] = schedules, ["fabricSheetTypes"] = fabric,
            },
            ["gaps"] = gaps,
            ["reference"] = "NBR 7480 (bitolas), NBR 6118 tabelas 7.2, 9.1 e 9.2.",
        };
    }
}

/// <summary>
/// Model write: reinforce a circular or arc concrete wall made of one or more arc walls sharing a
/// centre. Rings are native shape-driven arc sets (editable bar type, spacing, quantity) split into
/// bars no longer than maxBarLength with laps staggered between consecutive rings; vertical bars are
/// free-form sets. Lap length defaults to NBR 6118 9.5.2.2 (50% spliced per section, poor bond).
/// </summary>
public sealed class ReinforceCurvedWallCommand : IRevitCommand
{
    public string Name => "reinforce_curved_wall";
    public bool IsReadOnly => false;

    sealed record WallArc(Wall Wall, XYZ Center, double InnerR, double OuterR, double Start, double Sweep, double BaseZ, double TopZ)
    {
        public bool Contains(double angle)
        {
            var a = (angle - Start) % (2 * Math.PI); if (a < 0) a += 2 * Math.PI;
            return a <= Sweep + 1e-9;
        }
    }

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);
        var toM = units == "feet" ? P.FeetToMeters : 1.0;

        var walls = P.Arr(p, "wallIds").Select(n => ReadWall(doc, P.LongFrom(n, "wallIds"))).ToList();
        var c0 = walls[0].Center;
        foreach (var w in walls)
            if (new XYZ(w.Center.X - c0.X, w.Center.Y - c0.Y, 0).GetLength() > 0.01 || Math.Abs(w.InnerR - walls[0].InnerR) > 0.01)
                throw new RevitCommandException("invalid_parameter", "All walls must be arcs of the same circle (centre and radius).");
        var sweep = walls.Sum(w => w.Sweep);
        var closed = CurvedWallRebarPlanner.IsClosed(sweep - 1e-4);
        var start = closed ? 0 : walls.OrderBy(w => w.Start).First().Start;
        var baseZ = walls.Min(w => w.BaseZ); var topZ = walls.Max(w => w.TopZ);

        var ringSpec = P.Obj(p, "ringBar"); var vertSpec = P.Obj(p, "verticalBar");
        var ringType = RebarUtil.ResolveBarType(doc, ringSpec, "ringBar");
        var vertType = RebarUtil.ResolveBarType(doc, vertSpec, "verticalBar");
        var ringD = RebarUtil.DiameterMm(ringType); var vertD = RebarUtil.DiameterMm(vertType);
        var cover = P.Dbl(p, "cover") * toM;
        var fck = P.DblOr(p, "fckMpa", 30);
        var goodBond = (P.StrOrNull(p, "ringBond") ?? "poor").Equals("good", StringComparison.OrdinalIgnoreCase);
        var lapMemo = Nbr6118.TensionLap(ringD, fck, RebarUtil.Grade(doc, ringType), goodBond, fractionSpliced: 0.5);
        var lap = p["lapLength"] is null ? lapMemo.LapMm / 1000 : P.Dbl(p, "lapLength") * toM;
        // Vertical bars are in a good bond zone (inclined >= 45 degrees, 9.3.1); splices staggered half a bar.
        var vLapMemo = Nbr6118.TensionLap(vertD, fck, RebarUtil.Grade(doc, vertType), goodBond: true, fractionSpliced: 0.5);
        var vLap = p["verticalLapLength"] is null ? vLapMemo.LapMm / 1000 : P.Dbl(p, "verticalLapLength") * toM;
        var faces = (P.StrOrNull(p, "faces") ?? "both").ToLowerInvariant();

        var input = new CurvedWallRebarPlanner.Input(
            InnerRadius: walls[0].InnerR, Thickness: walls[0].OuterR - walls[0].InnerR, Height: topZ - baseZ,
            Cover: cover, RingDiameter: ringD / 1000, VerticalDiameter: vertD / 1000,
            RingSpacing: P.Dbl(ringSpec, "spacing") * toM, VerticalSpacing: P.Dbl(vertSpec, "spacing") * toM,
            LapLength: lap, MaxBarLength: P.DblOr(p, "maxBarLength", 12.0) * toM,
            StartAngle: start, Sweep: closed ? 2 * Math.PI : sweep,
            RingsOutside: !(P.StrOrNull(p, "layerOrder") ?? "ringsOutside").Equals("verticalsOutside", StringComparison.OrdinalIgnoreCase),
            InteriorFace: faces is "both" or "interior", ExteriorFace: faces is "both" or "exterior",
            VerticalLapLength: vLap);
        CurvedWallRebarPlanner.Plan plan;
        try { plan = CurvedWallRebarPlanner.Build(input); }
        catch (ArgumentException ex) { throw new RevitCommandException("invalid_parameter", ex.Message); }

        var partition = P.StrOrNull(p, "partition");
        var comment = P.StrOrNull(p, "comment");
        var m = P.MetersToFeet;
        WallArc HostFor(double angle) => walls.FirstOrDefault(w => w.Contains(angle)) ?? walls[0];
        XYZ At(double r, double angle, double z) => new((c0.X + r * Math.Cos(angle)) * m, (c0.Y + r * Math.Sin(angle)) * m, (baseZ + z) * m);

        var created = new JsonArray();
        var ids = new List<Rebar>();
        foreach (var set in plan.RingSets)
            foreach (var seg in set.Segments)
            {
                var host = HostFor(seg.StartAngle + seg.Sweep / 2);
                var arc = Arc.Create(At(0, 0, set.Z0 + seg.Shift), set.Radius * m, seg.StartAngle, seg.EndAngle, XYZ.BasisX, XYZ.BasisY);
                var rebar = Rebar.CreateFromCurves(doc, RebarStyle.Standard, ringType, null, null, host.Wall, XYZ.BasisZ,
                    new List<Curve> { arc }, RebarHookOrientation.Left, RebarHookOrientation.Left, true, true)
                    ?? throw new RevitCommandException("command_failed", "Revit refused a ring bar (check the arc radius against the bar type's bend limits).");
                var acc = rebar.GetShapeDrivenAccessor();
                if (set.Count > 1) acc.SetLayoutAsNumberWithSpacing(set.Count, set.Spacing * m, true, true, true);
                else acc.SetLayoutAsSingle();
                Tag(rebar, partition, comment, $"anel {set.Face} g{set.Group}");
                ids.Add(rebar);
                created.Add(new JsonObject
                {
                    ["id"] = rebar.Id.Value, ["role"] = "ring", ["face"] = set.Face, ["staggerGroup"] = set.Group,
                    ["hostId"] = host.Wall.Id.Value, ["rings"] = set.Count, ["spacingM"] = Math.Round(set.Spacing, 4),
                    ["radiusM"] = Math.Round(set.Radius, 4), ["barLengthM"] = Math.Round(seg.Sweep * set.Radius, 3),
                    ["fromDeg"] = Math.Round(seg.StartAngle * 180 / Math.PI, 2), ["toDeg"] = Math.Round(seg.EndAngle * 180 / Math.PI, 2),
                    ["contactLapLiftM"] = Math.Round(seg.Shift, 4),
                });
            }

        foreach (var v in plan.VerticalSets)
            for (var k = 0; k < v.Pieces.Count; k++)
            {
                var piece = v.Pieces[k];
                foreach (var group in v.Angles.GroupBy(a => HostFor(a)))
                {
                    var loops = group.Select(a =>
                    {
                        var loop = new CurveLoop();
                        var ang = a + piece.Shift / v.Radius; // contact lap: side by side along the circle
                        loop.Append(Line.CreateBound(At(v.Radius, ang, piece.StartAngle), At(v.Radius, ang, piece.EndAngle)));
                        return loop;
                    }).ToList();
                    var rebar = Rebar.CreateFreeForm(doc, vertType, group.Key.Wall, loops, out RebarFreeFormValidationResult error);
                    if (rebar is null || error != RebarFreeFormValidationResult.Success)
                        throw new RevitCommandException("command_failed", $"Revit refused the vertical bars ({error}).");
                    Tag(rebar, partition, comment, $"vertical {v.Face} g{v.Group} trecho {k + 1}");
                    ids.Add(rebar);
                    created.Add(new JsonObject
                    {
                        ["id"] = rebar.Id.Value, ["role"] = "vertical", ["face"] = v.Face, ["staggerGroup"] = v.Group,
                        ["piece"] = k + 1, ["hostId"] = group.Key.Wall.Id.Value, ["bars"] = loops.Count,
                        ["radiusM"] = Math.Round(v.Radius, 4), ["barLengthM"] = Math.Round(piece.Sweep, 3),
                        ["fromZ"] = Math.Round(piece.StartAngle, 3), ["toZ"] = Math.Round(piece.EndAngle, 3),
                        ["contactLapShiftM"] = Math.Round(piece.Shift, 4),
                    });
                }
            }

        var show3D = P.BoolOr(p, "show3D", true);
        if (show3D)
        {
            var views = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(x => !x.IsTemplate).ToList();
            foreach (var r in ids)
                foreach (var view in views)
                    r.SetUnobscuredInView(view, true); // solid display: Fine detail level (Revit 2023+)
        }

        doc.Regenerate();
        double Mass(Rebar r, double d) => (r.get_Parameter(BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH)?.AsDouble() ?? 0) * P.FeetToMeters * Nbr6118.MassPerMetre(d);
        var ringIds = created.Where(n => n!["role"]!.GetValue<string>() == "ring").Select(n => n!["id"]!.GetValue<long>()).ToHashSet();
        double ringKg = 0, vertKg = 0, ringLen = 0, vertLen = 0;
        int ringBars = 0, vertBars = 0;
        foreach (var r in ids)
        {
            var isRing = ringIds.Contains(r.Id.Value);
            var len = (r.get_Parameter(BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH)?.AsDouble() ?? 0) * P.FeetToMeters;
            var qty = r.get_Parameter(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS)?.AsInteger() ?? 0;
            if (isRing) { ringKg += Mass(r, ringD); ringLen += len; ringBars += qty; }
            else { vertKg += Mass(r, vertD); vertLen += len; vertBars += qty; }
        }

        return new JsonObject
        {
            ["closed"] = plan.Closed,
            ["geometry"] = new JsonObject
            {
                ["innerDiameterM"] = Math.Round(2 * walls[0].InnerR, 3), ["thicknessM"] = Math.Round(walls[0].OuterR - walls[0].InnerR, 3),
                ["heightM"] = Math.Round(topZ - baseZ, 3), ["coverM"] = cover,
            },
            ["rings"] = new JsonObject
            {
                ["barType"] = ringType.Name, ["perFace"] = plan.RingCount, ["spacingActualM"] = Math.Round(plan.RingSpacingActual, 4),
                ["barsPerRing"] = plan.SegmentsPerRing, ["bars"] = ringBars, ["lengthM"] = Math.Round(ringLen, 2), ["massKg"] = Math.Round(ringKg, 2),
            },
            ["verticals"] = new JsonObject
            {
                ["barType"] = vertType.Name, ["bars"] = vertBars, ["lengthM"] = Math.Round(vertLen, 2), ["massKg"] = Math.Round(vertKg, 2),
                ["piecesPerLine"] = plan.VerticalSets.Max(v => v.Pieces.Count),
                ["lapLengthM"] = plan.VerticalSets.Max(v => v.Pieces.Count) > 1 ? Math.Round(vLap, 3) : null,
                ["lapSource"] = p["verticalLapLength"] is null ? $"NBR 6118 9.5.2.2 (boa aderência, 50% por seção, lb {vLapMemo.LbMm:0} mm)" : "user",
                ["note"] = "Do cobrimento inferior ao superior; esperas/ancoragem na fundação ficam para quando a base existir.",
            },
            ["lap"] = new JsonObject
            {
                ["lengthM"] = Math.Round(lap, 3),
                ["source"] = p["lapLength"] is null ? "NBR 6118 9.5.2.2" : "user",
                ["lbMm"] = Math.Round(lapMemo.LbMm, 1), ["lbNecMm"] = Math.Round(lapMemo.LbNecMm, 1), ["alpha0t"] = lapMemo.Alpha0t,
                ["fbdMpa"] = Math.Round(lapMemo.FbdMpa, 3), ["fckMpa"] = fck, ["bond"] = goodBond ? "good" : "poor",
                ["splicedPerSection"] = "50% (traspasses alternados entre anéis consecutivos, defasados de meia barra)",
            },
            ["totalMassKg"] = Math.Round(ringKg + vertKg, 2),
            ["created"] = created,
        };
    }

    static void Tag(Rebar r, string? partition, string? comment, string role)
    {
        if (partition is not null) r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.Set(partition);
        r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(comment ?? role);
    }

    static WallArc ReadWall(Document doc, long id)
    {
        if (doc.GetElement(new ElementId(id)) is not Wall wall)
            throw new RevitCommandException("invalid_parameter", $"Element {id} is not a wall.");
        if (wall.Location is not LocationCurve { Curve: Arc arc })
            throw new RevitCommandException("invalid_parameter", $"Wall {id} is not curved (its location is not an arc).");
        if (!RebarHostData.IsValidHost(wall))
            throw new RevitCommandException("invalid_parameter", $"Wall {id} cannot host rebar (structural concrete wall required).");

        var radii = new List<double>();
        foreach (var obj in wall.get_Geometry(new Options()))
            if (obj is Solid { Volume: > 0 } solid)
                foreach (Face f in solid.Faces)
                    if (f is CylindricalFace cf) radii.Add(cf.get_Radius(0).GetLength());
        if (radii.Count < 2) throw new RevitCommandException("command_failed", $"Wall {id}: could not read its curved faces.");

        var c = arc.Center;
        double Angle(XYZ pt) => Math.Atan2(pt.Y - c.Y, pt.X - c.X);
        var sweep = arc.Length / arc.Radius;
        var start = arc.Normal.Z >= 0 ? Angle(arc.GetEndPoint(0)) : Angle(arc.GetEndPoint(1));
        if (start < 0) start += 2 * Math.PI;
        var bb = wall.get_BoundingBox(null);
        return new WallArc(wall, new XYZ(c.X * P.FeetToMeters, c.Y * P.FeetToMeters, 0),
            radii.Min() * P.FeetToMeters, radii.Max() * P.FeetToMeters, start, sweep,
            bb.Min.Z * P.FeetToMeters, bb.Max.Z * P.FeetToMeters);
    }
}
