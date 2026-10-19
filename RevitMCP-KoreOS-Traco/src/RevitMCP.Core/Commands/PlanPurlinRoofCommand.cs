using System;
using System.Linq;
using System.Text.Json.Nodes;
using static RevitMCPAddin.Commands.PurlinRoofPlanner;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Read-only: lays out a timber purlin roof on masonry gables (see PurlinRoofPlanner).
/// Returns members ready for create_framing_member, the covering roof base elevation,
/// gable profiles for create_profile_wall with purlin pockets, and joint trims. Does not
/// touch the model and does not design members. All lengths in meters.
/// </summary>
public sealed class PlanPurlinRoofCommand : IRevitCommand
{
    public string Name => "plan_purlin_roof";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var p = ctx.Parameters;
        var d = new Input();
        Section S(string key, Section fallback) => p[key] is JsonObject o
            ? new Section(P.DblOr(o, "width", fallback.Width), P.DblOr(o, "height", fallback.Height)) : fallback;
        var input = d with
        {
            X0 = P.Dbl(p, "x0"), Y0 = P.Dbl(p, "y0"), X1 = P.Dbl(p, "x1"), Y1 = P.Dbl(p, "y1"),
            RidgeAxis = P.StrOrNull(p, "ridgeAxis") ?? d.RidgeAxis,
            PitchDegrees = P.DblOr(p, "pitchDegrees", d.PitchDegrees),
            EaveOverhang = P.DblOr(p, "eaveOverhang", d.EaveOverhang),
            GableOverhang = P.DblOr(p, "gableOverhang", d.GableOverhang),
            EaveWallThickness = P.DblOr(p, "eaveWallThickness", d.EaveWallThickness),
            GableWallThickness = P.DblOr(p, "gableWallThickness", d.GableWallThickness),
            MasonryTop = P.DblOr(p, "masonryTop", d.MasonryTop),
            Plate = S("plate", d.Plate), Purlin = S("purlin", d.Purlin), Ridge = S("ridge", d.Ridge),
            IntermediatePurlinsPerSide = P.IntOr(p, "intermediatePurlinsPerSide", d.IntermediatePurlinsPerSide),
            Rafter = S("rafter", d.Rafter), RafterMaxSpacing = P.DblOr(p, "rafterMaxSpacing", d.RafterMaxSpacing),
            Boarding = P.DblOr(p, "boarding", d.Boarding), Membrane = P.DblOr(p, "membrane", d.Membrane),
            Battens = P.DblOr(p, "battens", d.Battens), Tile = P.DblOr(p, "tile", d.Tile),
            JointGap = P.DblOr(p, "jointGap", d.JointGap), JointTrim = S("jointTrim", d.JointTrim),
            PurlinPocketClearance = P.DblOr(p, "purlinPocketClearance", d.PurlinPocketClearance),
            BlockingThickness = P.DblOr(p, "blockingThickness", d.BlockingThickness), Blocking = P.BoolOr(p, "blocking", d.Blocking),
            Fascia = S("fascia", d.Fascia), FasciaBoards = P.BoolOr(p, "fasciaBoards", d.FasciaBoards),
            PlateAlignment = P.StrOrNull(p, "plateAlignment") ?? d.PlateAlignment,
            FasciaRecess = P.DblOr(p, "fasciaRecess", d.FasciaRecess),
            FasciaAutoHeight = P.BoolOr(p, "fasciaAutoHeight", d.FasciaAutoHeight),
        };
        Plan plan;
        try { plan = Build(input); }
        catch (ArgumentException ex) { throw new RevitCommandException("invalid_parameter", ex.Message); }

        JsonObject Pt(P3 v) => new() { ["x"] = R(v.X), ["y"] = R(v.Y), ["z"] = R(v.Z) };
        var members = new JsonArray(plan.Members.Select(m => (JsonNode)new JsonObject
        {
            ["role"] = m.Role, ["side"] = m.Side, ["start"] = Pt(m.Start), ["end"] = Pt(m.End),
            ["width"] = m.Width, ["height"] = R(m.Height), ["zJustification"] = m.ZJustification,
            ["sectionKey"] = $"{m.Role}:{R(m.Width)}x{R(m.Height)}",
            ["cutByCovering"] = m.CutByCovering,
        }).ToArray());
        var counts = new JsonObject();
        foreach (var g in plan.Members.GroupBy(m => m.Role)) counts[g.Key] = g.Count();
        return new JsonObject
        {
            ["units"] = "meters",
            ["members"] = members, ["memberCounts"] = counts,
            ["gables"] = new JsonArray(plan.Gables.Select(g => (JsonNode)new JsonObject
            {
                ["name"] = g.Name, ["wallCentre"] = R(g.WallCentreV),
                ["profile"] = new JsonArray(g.Profile.Select(v => (JsonNode)Pt(v)).ToArray()),
                ["purlinOpenings"] = new JsonArray(g.PurlinOpenings.Select(o => (JsonNode)new JsonObject
                    { ["role"] = o.Role, ["lower"] = Pt(o.Min), ["upper"] = Pt(o.Max) }).ToArray()),
            }).ToArray()),
            ["covering"] = new JsonObject
            {
                ["footprint"] = new JsonObject { ["x0"] = plan.RoofFootprintX0, ["y0"] = plan.RoofFootprintY0, ["x1"] = plan.RoofFootprintX1, ["y1"] = plan.RoofFootprintY1 },
                ["baseElevationAtFootprint"] = R(plan.CoveringBaseElevationAtFootprint),
                ["thickness"] = R(plan.CoveringThickness),
                ["layersTopToBottom"] = new JsonArray(
                    new JsonObject { ["role"] = "tile", ["width"] = input.Tile },
                    new JsonObject { ["role"] = "battens_and_counter_battens", ["width"] = input.Battens },
                    new JsonObject { ["role"] = "membrane", ["width"] = input.Membrane },
                    new JsonObject { ["role"] = "boarding", ["width"] = input.Boarding }),
                ["boardingUndersideAtRidge"] = R(plan.BoardingUndersideAtRidge),
            },
            ["eave"] = new JsonObject
            {
                ["fasciaOuterFaceOffsetFromWallOuterFace"] = R(plan.Eave.FasciaOuterFaceOffset),
                ["boardingUndersideAtFasciaOuterFace"] = R(plan.Eave.BoardingUndersideAtFasciaOuterFace),
                ["rafterUndersideAtFasciaInnerFace"] = R(plan.Eave.RafterUndersideAtFasciaInnerFace),
                ["note"] = "Hang the gutter on the fascia outer face with its top below boardingUndersideAtFasciaOuterFace; the tile edge then drips inside it by fasciaRecess.",
            },
            ["checks"] = new JsonObject
            {
                ["rafterUndersideAtRidge"] = R(plan.RafterUndersideAtRidge),
                ["maxRafterSpacing"] = R(plan.MaxRafterSpacing),
                ["maxRafterSpanHorizontal"] = R(plan.MaxRafterSpanHorizontal),
                ["rafterStations"] = new JsonArray(plan.RafterStations.Select(s => (JsonNode)R(s)).ToArray()),
            },
            ["warnings"] = new JsonArray(plan.Warnings.Select(w => (JsonNode)w).ToArray()),
            ["note"] = "Layout only. Sections are inputs, not a structural design: loads, spans, deflection, connections, anchorage, "
                + "uplift and bearing on masonry must be checked by the engineer. Walls under the plates must end at masonryTop "
                + "(detach from the roof first); gables become separate profile walls with purlin openings and a perimeter joint.",
        };
    }

    static double R(double v) => Math.Round(v, 6);
}
