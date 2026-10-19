using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Tag all (untagged) elements of a given category in a view.
/// Mirrors Revit's "Tag All Not Tagged" workflow.
///
/// Params:
///   - category:    string, required — category display name, e.g. "Doors", "Windows"
///   - viewId:      long, optional (defaults to active view)
///   - leader:      bool, default false
///   - skipTagged:  bool, default true — skip elements that already have a tag
/// </summary>
public sealed class TagAllInViewCommand : IRevitCommand
{
    public string Name => "tag_all_in_view";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;

        var viewId = p["viewId"] is not null
            ? new ElementId(P.Long(p, "viewId"))
            : doc.ActiveView?.Id
            ?? throw new RevitCommandException("not_found", "No active view.");

        var view = doc.GetElement(viewId) as View
            ?? throw new RevitCommandException("not_found", $"View {viewId.Value} not found.");

        var categoryName = P.Str(p, "category");
        var addLeader = P.BoolOr(p, "leader", false);
        var skipTagged = P.BoolOr(p, "skipTagged", true);
        var tagTypeName = P.StrOrNull(p, "tagTypeName");

        // Resolve category by display name
        var cat = doc.Settings.Categories.Cast<Category>()
            .FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new RevitCommandException("not_found",
                $"Category '{categoryName}' not found. Use list_categories to see valid names.");

        // Collect already-tagged element IDs in this view (to honour skipTagged)
        var alreadyTagged = new HashSet<long>();
        if (skipTagged)
        {
            foreach (var tag in new FilteredElementCollector(doc, viewId)
                         .OfClass(typeof(IndependentTag))
                         .Cast<IndependentTag>())
            {
                try
                {
                    var ids = tag.GetTaggedLocalElementIds();
                    if (ids is not null)
                        foreach (var tid in ids)
                            if (tid != ElementId.InvalidElementId)
                                alreadyTagged.Add(tid.Value);
                }
                catch { }
            }
            foreach (var roomTag in new FilteredElementCollector(doc, viewId).OfCategory(BuiltInCategory.OST_RoomTags)
                         .WhereElementIsNotElementType().OfType<RoomTag>())
                if (roomTag.TaggedLocalRoomId != ElementId.InvalidElementId) alreadyTagged.Add(roomTag.TaggedLocalRoomId.Value);
        }
        RoomTagType? roomTagType = null;
        if (tagTypeName is not null)
            roomTagType = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_RoomTags)
                .Cast<FamilySymbol>().OfType<RoomTagType>().FirstOrDefault(t => string.Equals(t.Name, tagTypeName, StringComparison.OrdinalIgnoreCase));
        var errors = new JsonArray();

        var taggedArr = new JsonArray();
        int skipped = 0, failed = 0;

        foreach (var element in new FilteredElementCollector(doc, viewId)
                     .OfCategoryId(cat.Id)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            if (skipTagged && alreadyTagged.Contains(element.Id.Value))
            {
                skipped++;
                continue;
            }

            try
            {
                var bbox = element.get_BoundingBox(view);
                var loc = bbox is not null ? (bbox.Min + bbox.Max) / 2.0 : XYZ.Zero;

                Element tag;
                if (element is Room room)
                {
                    // Rooms need RoomTag (IndependentTag cannot reference a room); place at the room location point.
                    var at = (room.Location as LocationPoint)?.Point ?? loc;
                    var roomTag = doc.Create.NewRoomTag(new LinkElementId(room.Id), new UV(at.X, at.Y), viewId);
                    if (roomTagType is not null) roomTag.RoomTagType = roomTagType;
                    tag = roomTag;
                }
                else
                    tag = IndependentTag.Create(
                        doc, viewId, new Reference(element), addLeader,
                        TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, loc);

                taggedArr.Add(new JsonObject
                {
                    ["tagId"] = tag.Id.Value,
                    ["elementId"] = element.Id.Value,
                });
            }
            catch (System.Exception ex)
            {
                failed++;
                if (errors.Count < 5) errors.Add(new JsonObject { ["elementId"] = element.Id.Value, ["message"] = ex.Message });
            }
        }

        return new JsonObject
        {
            ["tagged"] = taggedArr.Count,
            ["skipped"] = skipped,
            ["failed"] = failed,
            ["tags"] = taggedArr,
            ["errors"] = errors,
        };
    }
}
