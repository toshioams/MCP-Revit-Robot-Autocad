using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Create a component stair between two levels from straight runs, with automatic landings between
/// consecutive runs (straight, L or U layouts by choosing run start points and directions).
///
/// Params (meters):
///   - baseLevelName, topLevelName: string, required
///   - runs: [{ start:{x,y}, direction:{x,y}, risers:int }], required; risers per run add up to the total
///   - width: run width (default 1.00)
///   - treadDepth: optional (default: the stair type's minimum tread depth)
///
/// Stairs need StairsEditScope, which can only start outside a transaction: self-managed. dryRun validates
/// the geometry (riser height, run lengths) without creating anything. Warnings (riser/tread outside the type's
/// rules) are kept and reported; errors cancel the stair.
/// </summary>
public sealed class CreateStairCommand : IRevitCommand
{
    public string Name => "create_stair";
    public bool IsReadOnly => false;
    public ExecutionKind Execution => ExecutionKind.SelfManagedWrite;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var baseLevel = CreateWallCommand.ResolveLevel(doc, P.Str(p, "baseLevelName"));
        var topLevel = CreateWallCommand.ResolveLevel(doc, P.Str(p, "topLevelName"));
        var height = topLevel.Elevation - baseLevel.Elevation;
        if (height <= 0)
            throw new RevitCommandException("invalid_parameter", "topLevelName must be above baseLevelName.");
        if (p["runs"] is not JsonArray runsArr || runsArr.Count == 0)
            throw new RevitCommandException("invalid_parameter", "runs must be a non-empty array of {start, direction, risers}.");
        var width = P.DblOr(p, "width", 1.0) * P.MetersToFeet;

        var runs = new List<(XYZ start, XYZ dir, int risers)>();
        for (var i = 0; i < runsArr.Count; i++)
        {
            if (runsArr[i] is not JsonObject r)
                throw new RevitCommandException("invalid_parameter", $"runs[{i}] must be an object.");
            var s = P.Xyz(r, "start", "meters");
            var d = P.Xyz(r, "direction", "meters");
            var dir = new XYZ(d.X, d.Y, 0);
            if (dir.GetLength() < 1e-6)
                throw new RevitCommandException("invalid_parameter", $"runs[{i}].direction must be a non-zero plan vector.");
            var n = P.IntOr(r, "risers", 0);
            if (n < 2)
                throw new RevitCommandException("invalid_parameter", $"runs[{i}].risers must be at least 2.");
            runs.Add((new XYZ(s.X, s.Y, 0), dir.Normalize(), n));
        }
        var total = runs.Sum(r => r.risers);
        var riser = height / total;

        JsonObject Plan(double tread) => new()
        {
            ["heightMeters"] = Math.Round(height / P.MetersToFeet, 3),
            ["risers"] = total,
            ["riserHeightMeters"] = Math.Round(riser / P.MetersToFeet, 4),
            ["treadDepthMeters"] = Math.Round(tread / P.MetersToFeet, 4),
            ["blondel"] = Math.Round((2 * riser + tread) / P.MetersToFeet, 3),
            ["runs"] = new JsonArray(runs.Select(r => (JsonNode)new JsonObject
            {
                ["risers"] = r.risers,
                ["lengthMeters"] = Math.Round((r.risers - 1) * tread / P.MetersToFeet, 3),
                ["end"] = new JsonArray(Math.Round((r.start.X + r.dir.X * (r.risers - 1) * tread) / P.MetersToFeet, 3),
                                        Math.Round((r.start.Y + r.dir.Y * (r.risers - 1) * tread) / P.MetersToFeet, 3)),
            }).ToArray()),
        };

        var requestedTread = p["treadDepth"] is null ? (double?)null : P.Dbl(p, "treadDepth") * P.MetersToFeet;

        // dryRun builds the stair inside the edit scope and cancels it, so Revit validates the runs (a geometry-only
        // dry run passed live while the commit failed on the run elevations).
        var warnings = new StairFailureLog();
        ElementId stairsId;
        var runIds = new List<ElementId>();
        double tread;
        using (var scope = new StairsEditScope(doc, "MCP: create_stair"))
        {
            stairsId = scope.Start(baseLevel.Id, topLevel.Id);
            using (var t = new Transaction(doc, "MCP: create_stair runs"))
            {
                var options = t.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(warnings);
                t.SetFailureHandlingOptions(options);
                t.Start();
                var stairs = (Stairs)doc.GetElement(stairsId);
                stairs.DesiredRisersNumber = total;
                if (requestedTread is double td) stairs.ActualTreadDepth = td;
                tread = stairs.ActualTreadDepth;
                // The path Z is the run's base elevation in MODEL coordinates (>= the stairs base), not relative to it:
                // live 14/09 a relative 0 was refused as "not a valid location path line".
                var rise = 0.0;
                for (var i = 0; i < runs.Count; i++)
                {
                    var r = runs[i];
                    var a = new XYZ(r.start.X, r.start.Y, baseLevel.Elevation + rise);
                    var b = a + r.dir * ((r.risers - 1) * tread);
                    StairsRun run;
                    try { run = StairsRun.CreateStraightRun(doc, stairsId, Line.CreateBound(a, b), StairsRunJustification.Center); }
                    catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                    {
                        t.RollBack(); scope.Cancel();
                        throw new RevitCommandException("invalid_parameter", $"runs[{i}] rejected by Revit: {ex.Message}");
                    }
                    run.ActualRunWidth = width;
                    runIds.Add(run.Id);
                    rise += r.risers * riser;
                }
                for (var i = 1; i < runIds.Count; i++)
                    StairsLanding.CreateAutomaticLanding(doc, runIds[i - 1], runIds[i]);
                if (ctx.DryRun)
                {
                    doc.Regenerate();
                    var preview = Plan(tread);
                    preview["actualRisers"] = ((Stairs)doc.GetElement(stairsId)).ActualRisersNumber;
                    preview["warnings"] = warnings.Messages;
                    preview["note"] = "dryRun: built inside the stairs edit scope and cancelled; nothing was kept.";
                    t.RollBack(); scope.Cancel();
                    return preview;
                }
                if (t.Commit() != TransactionStatus.Committed)
                {
                    scope.Cancel();
                    throw new RevitCommandException("transaction_not_committed", "Stair runs were rolled back: " + warnings.Messages.ToJsonString());
                }
            }
            scope.Commit(warnings);
        }
        if (doc.GetElement(stairsId) is not Stairs created)
            throw new RevitCommandException("command_failed", "The stair was not kept: " + warnings.Messages.ToJsonString());

        var result = Plan(tread);
        result["id"] = stairsId.Value;
        result["runIds"] = new JsonArray(runIds.Select(id => (JsonNode)id.Value).ToArray());
        result["actualRisers"] = created.ActualRisersNumber;
        result["actualRiserHeightMeters"] = Math.Round(created.ActualRiserHeight / P.MetersToFeet, 4);
        result["warnings"] = warnings.Messages;
        return result;
    }

    /// <summary>Warnings are recorded and deleted (the type's comfort rules fire often on existing stairs); errors roll back.</summary>
    private sealed class StairFailureLog : IFailuresPreprocessor
    {
        internal JsonArray Messages { get; } = new();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var hasError = false;
            foreach (var f in accessor.GetFailureMessages())
            {
                Messages.Add(new JsonObject { ["severity"] = f.GetSeverity().ToString(), ["description"] = f.GetDescriptionText() });
                if (f.GetSeverity() == FailureSeverity.Warning) accessor.DeleteWarning(f);
                else hasError = true;
            }
            return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
