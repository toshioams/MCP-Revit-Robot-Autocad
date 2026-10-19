using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Configure the build-up of a railing type (meters): top rail, non-continuous rails, the baluster pattern, the
/// start/corner/end posts and balusters per tread. Rails and balusters live in API objects (RailStructure,
/// BalusterPlacement), not in type parameters, so they cannot be edited with set_parameter.
///
/// Params:
///   - typeId | typeName: the RailingType to edit, required
///   - duplicateAs: optional new type name; the type is duplicated first and the copy is configured
///   - topRail: { enabled?, typeId | typeName, height } — enabled:false removes the top rail
///   - rails: [{ name, height, offset?, profileId | profileName?, materialName? }] — replaces all non-continuous rails
///   - balusters: { familyTypeId | familyTypeName, spacing, baseReference?, baseOffset?, topReference?, topOffset?,
///                  offset?, justification?, breakPattern? } — the main pattern becomes this single baluster
///   - posts: { familyTypeId | familyTypeName, baseReference?, baseOffset?, topReference?, topOffset?, cornerCondition? }
///            — start, corner and end posts
///   - balusterPerTread: { enabled, number?, familyTypeId | familyTypeName? }
/// References: "host", "topRail" or the name of one of the rails. Omitted sections are left unchanged.
/// </summary>
public sealed class ConfigureRailingTypeCommand : IRevitCommand
{
    public string Name => "configure_railing_type";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var type = ResolveRailingType(doc, p);

        var newName = P.StrOrNull(p, "duplicateAs");
        if (!string.IsNullOrWhiteSpace(newName))
        {
            if (new FilteredElementCollector(doc).OfClass(typeof(RailingType)).Any(t => t.Name == newName))
                throw new RevitCommandException("invalid_parameter", $"A railing type named '{newName}' already exists.");
            type = type.Duplicate(newName) as RailingType
                ?? throw new RevitCommandException("command_failed", "Duplicating the railing type did not return a RailingType.");
        }

        if (p["topRail"] is JsonObject top)
        {
            if (!P.BoolOr(top, "enabled", true)) type.TopRailType = ElementId.InvalidElementId;
            else
            {
                if (top["typeId"] is not null || top["typeName"] is not null)
                    type.TopRailType = ResolveTypeId<TopRailType>(doc, top, "typeId", "typeName", "top rail type");
                if (top["height"] is not null) type.TopRailHeight = P.Dbl(top, "height") * P.MetersToFeet;
            }
        }

        if (p["rails"] is JsonArray railsArr)
        {
            var structure = type.RailStructure;
            for (var i = structure.GetNonContinuousRailCount() - 1; i >= 0; i--) structure.RemoveNonContinuousRail(i);
            for (var i = 0; i < railsArr.Count; i++)
            {
                if (railsArr[i] is not JsonObject r)
                    throw new RevitCommandException("invalid_parameter", $"rails[{i}] must be an object.");
                var name = P.Str(r, "name");
                var rail = structure.AddNonContinuousRail(name, P.Dbl(r, "height") * P.MetersToFeet, P.DblOr(r, "offset", 0) * P.MetersToFeet);
                if (r["profileId"] is not null || r["profileName"] is not null)
                {
                    var profileId = ResolveSymbolId(doc, r, "profileId", "profileName", BuiltInCategory.OST_ProfileFamilies, "profile");
                    if (!structure.IsValidNonContinuousRailProfile(profileId))
                        throw new RevitCommandException("invalid_parameter", $"rails[{i}]: profile {profileId.Value} is not valid for a rail.");
                    rail.ProfileId = profileId;
                }
                var materialName = P.StrOrNull(r, "materialName");
                if (!string.IsNullOrWhiteSpace(materialName))
                {
                    var mat = new FilteredElementCollector(doc).OfClass(typeof(Material)).FirstOrDefault(m => m.Name == materialName)
                        ?? throw new RevitCommandException("not_found", $"rails[{i}]: material '{materialName}' not found.");
                    rail.MaterialId = mat.Id;
                }
            }
        }

        var railNames = RailNames(type);
        var placement = type.BalusterPlacement;

        if (p["balusters"] is JsonObject bal)
        {
            var pattern = placement.BalusterPattern;
            if (pattern.GetBalusterCount() == 0)
                throw new RevitCommandException("command_failed", "The baluster pattern of this type is empty; start from a type that has one baluster.");
            for (var i = pattern.GetBalusterCount() - 1; i >= 1; i--) pattern.RemoveBaluster(i);
            var info = pattern.GetBaluster(0);
            ApplyBaluster(doc, info, bal, railNames, "balusters");
            info.DistanceFromPreviousOrSpace = P.Dbl(bal, "spacing") * P.MetersToFeet;
            if (bal["offset"] is not null) info.Offset = P.Dbl(bal, "offset") * P.MetersToFeet;
            if (bal["justification"] is not null) pattern.DistributionJustification = ParseEnum<PatternJustification>(P.Str(bal, "justification"), "balusters.justification");
            if (bal["breakPattern"] is not null) pattern.BreakPattern = ParseEnum<BreakPatternCondition>(P.Str(bal, "breakPattern"), "balusters.breakPattern");
        }

        if (p["posts"] is JsonObject posts)
        {
            var pp = placement.PostPattern;
            foreach (var (post, label) in new[] { (pp.StartPost, "start"), (pp.CornerPost, "corner"), (pp.EndPost, "end") })
                ApplyBaluster(doc, post, posts, railNames, $"posts ({label})");
            if (posts["cornerCondition"] is not null) pp.CornerPostCondition = ParseEnum<BreakCornerCondition>(P.Str(posts, "cornerCondition"), "posts.cornerCondition");
        }

        if (p["balusterPerTread"] is JsonObject bpt)
        {
            placement.UseBalusterPerTreadOnStairs = P.BoolOr(bpt, "enabled", true);
            if (bpt["number"] is not null) placement.BalusterPerTreadNumber = P.IntOr(bpt, "number", 1);
            if (bpt["familyTypeId"] is not null || bpt["familyTypeName"] is not null)
                placement.BalusterPerTreadFamilyId = ResolveSymbolId(doc, bpt, "familyTypeId", "familyTypeName", BuiltInCategory.OST_StairsRailingBaluster, "baluster");
        }

        doc.Regenerate();
        return Describe(doc, type);
    }

    static RailingType ResolveRailingType(Document doc, JsonObject p)
    {
        if (p["typeId"] is not null)
            return doc.GetElement(new ElementId(P.Long(p, "typeId"))) as RailingType
                ?? throw new RevitCommandException("invalid_parameter", "typeId must identify a RailingType.");
        var name = P.StrOrNull(p, "typeName");
        var types = new FilteredElementCollector(doc).OfClass(typeof(RailingType)).Cast<RailingType>().ToList();
        if (string.IsNullOrWhiteSpace(name))
            throw new RevitCommandException("invalid_parameter", $"typeId or typeName is required. Available: {string.Join(", ", types.Select(t => t.Name))}.");
        return types.FirstOrDefault(t => t.Name == name)
            ?? throw new RevitCommandException("not_found", $"Railing type '{name}' not found. Available: {string.Join(", ", types.Select(t => t.Name))}.");
    }

    static ElementId ResolveTypeId<T>(Document doc, JsonObject o, string idKey, string nameKey, string what) where T : ElementType
    {
        var all = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().ToList();
        if (o[idKey] is not null)
        {
            var id = new ElementId(P.Long(o, idKey));
            return all.Any(t => t.Id == id) ? id : throw new RevitCommandException("invalid_parameter", $"{idKey} {id.Value} is not a {what}.");
        }
        var name = P.Str(o, nameKey);
        return all.FirstOrDefault(t => t.Name == name)?.Id
            ?? throw new RevitCommandException("not_found", $"{what} '{name}' not found. Available: {string.Join(", ", all.Select(t => t.Name))}.");
    }

    /// Family types by id, "Family: Type" or a type name that is unique in the category.
    static ElementId ResolveSymbolId(Document doc, JsonObject o, string idKey, string nameKey, BuiltInCategory cat, string what)
    {
        var all = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(cat).Cast<FamilySymbol>().ToList();
        if (o[idKey] is not null)
        {
            var id = new ElementId(P.Long(o, idKey));
            return all.Any(s => s.Id == id) ? id : throw new RevitCommandException("invalid_parameter", $"{idKey} {id.Value} is not a {what} type.");
        }
        var name = P.Str(o, nameKey);
        var hits = all.Where(s => $"{s.FamilyName}: {s.Name}" == name || s.Name == name).ToList();
        if (hits.Count == 1) return hits[0].Id;
        var list = string.Join(", ", all.Select(s => $"{s.Id.Value} {s.FamilyName}: {s.Name}"));
        throw new RevitCommandException(hits.Count == 0 ? "not_found" : "invalid_parameter",
            hits.Count == 0 ? $"{what} '{name}' not found. Available: {list}." : $"{what} '{name}' is ambiguous; use 'Family: Type' or an id. Available: {list}.");
    }

    static List<string> RailNames(RailingType type)
    {
        var s = type.RailStructure;
        return Enumerable.Range(0, s.GetNonContinuousRailCount()).Select(i => s.GetNonContinuousRail(i).Name).ToList();
    }

    static string Reference(string value, List<string> railNames, string where)
    {
        if (value.Equals("host", StringComparison.OrdinalIgnoreCase)) return BalusterInfo.GetReferenceNameForHost();
        if (value.Equals("topRail", StringComparison.OrdinalIgnoreCase)) return BalusterInfo.GetReferenceNameForTopRail();
        if (railNames.Contains(value)) return value;
        throw new RevitCommandException("invalid_parameter", $"{where}: reference '{value}' must be host, topRail or a rail name ({string.Join(", ", railNames)}).");
    }

    static void ApplyBaluster(Document doc, BalusterInfo info, JsonObject o, List<string> railNames, string where)
    {
        if (o["familyTypeId"] is not null || o["familyTypeName"] is not null)
            info.BalusterFamilyId = ResolveSymbolId(doc, o, "familyTypeId", "familyTypeName", BuiltInCategory.OST_StairsRailingBaluster, "baluster");
        if (o["baseReference"] is not null) info.BaseReferenceName = Reference(P.Str(o, "baseReference"), railNames, where);
        if (o["baseOffset"] is not null) info.BaseOffset = P.Dbl(o, "baseOffset") * P.MetersToFeet;
        if (o["topReference"] is not null) info.TopReferenceName = Reference(P.Str(o, "topReference"), railNames, where);
        if (o["topOffset"] is not null) info.TopOffset = P.Dbl(o, "topOffset") * P.MetersToFeet;
    }

    static T ParseEnum<T>(string value, string where) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var v) ? v
            : throw new RevitCommandException("invalid_parameter", $"{where} must be one of: {string.Join(", ", Enum.GetNames(typeof(T)))}.");

    static double M(double feet) => Math.Round(feet / P.MetersToFeet, 3);

    static JsonObject Describe(Document doc, RailingType type)
    {
        string? NameOf(ElementId id) => id == ElementId.InvalidElementId ? null
            : doc.GetElement(id) is FamilySymbol fs ? $"{fs.FamilyName}: {fs.Name}" : doc.GetElement(id)?.Name;
        JsonObject Info(BalusterInfo b) => new()
        {
            ["family"] = NameOf(b.BalusterFamilyId), ["base"] = b.BaseReferenceName, ["baseOffsetMeters"] = M(b.BaseOffset),
            ["top"] = b.TopReferenceName, ["topOffsetMeters"] = M(b.TopOffset),
        };
        var s = type.RailStructure;
        var bp = type.BalusterPlacement;
        var pattern = bp.BalusterPattern;
        return new JsonObject
        {
            ["typeId"] = type.Id.Value,
            ["typeName"] = type.Name,
            ["topRail"] = new JsonObject { ["type"] = NameOf(type.TopRailType), ["heightMeters"] = M(type.TopRailHeight) },
            ["rails"] = new JsonArray(Enumerable.Range(0, s.GetNonContinuousRailCount()).Select(i =>
            {
                var r = s.GetNonContinuousRail(i);
                return (JsonNode)new JsonObject
                {
                    ["name"] = r.Name, ["heightMeters"] = M(r.Height), ["offsetMeters"] = M(r.Offset),
                    ["profile"] = NameOf(r.ProfileId), ["material"] = NameOf(r.MaterialId),
                };
            }).ToArray()),
            ["balusterPattern"] = new JsonObject
            {
                ["justification"] = pattern.DistributionJustification.ToString(),
                ["breakPattern"] = pattern.BreakPattern.ToString(),
                ["balusters"] = new JsonArray(Enumerable.Range(0, pattern.GetBalusterCount()).Select(i =>
                {
                    var b = pattern.GetBaluster(i);
                    var j = Info(b);
                    j["spacingMeters"] = M(b.DistanceFromPreviousOrSpace);
                    return (JsonNode)j;
                }).ToArray()),
            },
            ["posts"] = new JsonObject
            {
                ["start"] = Info(bp.PostPattern.StartPost), ["corner"] = Info(bp.PostPattern.CornerPost), ["end"] = Info(bp.PostPattern.EndPost),
                ["cornerCondition"] = bp.PostPattern.CornerPostCondition.ToString(),
            },
            ["balusterPerTread"] = new JsonObject
            {
                ["enabled"] = bp.UseBalusterPerTreadOnStairs, ["number"] = bp.BalusterPerTreadNumber, ["family"] = NameOf(bp.BalusterPerTreadFamilyId),
            },
        };
    }
}
