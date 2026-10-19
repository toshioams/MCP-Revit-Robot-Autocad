using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using static RevitMCPAddin.Commands.ProfileShapes;
using UV = RevitMCPAddin.Commands.ProfileShapes.UV;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Create a profile family (for roof edge sweeps, wall sweeps, reveals) from a closed loop or
/// a generated ridge cap, save it under %TEMP%\RevitMCP\profiles and load it into the target
/// document. Needed because office profiles are fixed (the metal ridge cap is 20 degrees).
/// Self-managed transactions: the family document is created and saved before the project
/// transaction that loads it. dryRun builds and validates the loop only.
/// </summary>
public sealed class CreateProfileFamilyCommand : IRevitCommand
{
    public string Name => "create_profile_family";
    public bool IsReadOnly => false;
    public ExecutionKind Execution => ExecutionKind.SelfManagedWrite;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var name = P.StrOrNull(p, "familyName");
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new RevitCommandException("invalid_parameter", "familyName must be a valid file name.");
        if (new FilteredElementCollector(doc).OfClass(typeof(Family)).Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new RevitCommandException("name_collision", $"A family named '{name}' is already loaded.");

        UV[] loop;
        try
        {
            if (p["ridgeCap"] is JsonObject rc)
                loop = RidgeCap(P.Dbl(rc, "pitchDegrees"), P.Dbl(rc, "wingLength"), P.Dbl(rc, "thickness")).ToArray();
            else if (p["loop"] is JsonArray raw)
                loop = raw.Select(n => new UV(P.Dbl((JsonObject)n!, "u"), P.Dbl((JsonObject)n!, "v"))).ToArray();
            else throw new ArgumentException("Pass ridgeCap {pitchDegrees, wingLength, thickness} or loop [{u,v}] in meters.");
            ValidateLoop(loop);
        }
        catch (ArgumentException ex) { throw new RevitCommandException("invalid_parameter", ex.Message); }

        var loopJson = new JsonArray(loop.Select(v => (JsonNode)new JsonObject { ["u"] = v.U, ["v"] = v.V }).ToArray());
        if (ctx.DryRun)
            return new JsonObject { ["familyName"] = name, ["loop"] = loopJson, ["areaSquareMeters"] = Math.Abs(Area(loop)),
                ["note"] = "dryRun: loop validated; no family created or loaded." };

        var app = ctx.App.Application;
        var template = P.StrOrNull(p, "templatePath") ?? FindTemplate(app.FamilyTemplatePath);
        var folder = Path.Combine(Path.GetTempPath(), "RevitMCP", "profiles");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".rfa");

        var famDoc = app.NewFamilyDocument(template)
            ?? throw new RevitCommandException("command_failed", "Could not create the family document.");
        try
        {
            if (famDoc.OwnerFamily?.FamilyCategory?.BuiltInCategory != BuiltInCategory.OST_ProfileFamilies)
                throw new RevitCommandException("invalid_parameter", "Template is not a profile family template.");
            using (var t = new Transaction(famDoc, "MCP profile"))
            {
                t.Start();
                var view = new FilteredElementCollector(famDoc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate)
                    ?? throw new RevitCommandException("command_failed", "Profile template has no plan view.");
                for (int i = 0; i < loop.Length; i++)
                {
                    var a = loop[i]; var b = loop[(i + 1) % loop.Length];
                    famDoc.FamilyCreate.NewDetailCurve(view, Line.CreateBound(
                        new XYZ(a.U * P.MetersToFeet, a.V * P.MetersToFeet, 0), new XYZ(b.U * P.MetersToFeet, b.V * P.MetersToFeet, 0)));
                }
                if (t.Commit() != TransactionStatus.Committed)
                    throw new RevitCommandException("command_failed", "Family document transaction did not commit.");
            }
            famDoc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
        }
        finally { famDoc.Close(false); }

        Family? family;
        using (var t = new Transaction(doc, "MCP: load profile family"))
        {
            t.Start();
            if (!doc.LoadFamily(path, out family) || family is null)
            {
                t.RollBack();
                throw new RevitCommandException("command_failed", "Revit did not load the profile family.");
            }
            if (t.Commit() != TransactionStatus.Committed)
                throw new RevitCommandException("transaction_not_committed", "Loading the profile family did not commit.");
        }
        var symbols = family.GetFamilySymbolIds().Select(id => doc.GetElement(id)).ToList();
        return new JsonObject
        {
            ["familyId"] = family.Id.Value, ["familyName"] = family.Name, ["path"] = path, ["template"] = template,
            ["types"] = new JsonArray(symbols.Select(s => (JsonNode)new JsonObject { ["id"] = s.Id.Value, ["name"] = s.Name }).ToArray()),
            ["loop"] = loopJson, ["areaSquareMeters"] = Math.Abs(Area(loop)),
            ["note"] = "Assign the profile to a fascia/gutter type (duplicate_family_type + set_parameter 'Perfil' {id}). Origin = host edge; u across, v up.",
        };
    }

    static string FindTemplate(string root)
    {
        foreach (var dir in new[] { root, Path.Combine(root, "English"), Path.Combine(Path.GetDirectoryName(root.TrimEnd('\\')) ?? root, "English") })
        {
            var candidate = Path.Combine(dir, "Metric Profile.rft");
            if (File.Exists(candidate)) return candidate;
        }
        var any = Directory.Exists(root) ? Directory.GetFiles(root, "Metric Profile.rft", SearchOption.AllDirectories).FirstOrDefault() : null;
        return any ?? throw new RevitCommandException("not_found", $"Metric Profile.rft not found under {root}; pass templatePath.");
    }
}
