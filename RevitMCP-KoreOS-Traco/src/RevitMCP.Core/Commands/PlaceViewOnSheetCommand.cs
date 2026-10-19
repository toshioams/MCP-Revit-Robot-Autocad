using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Place a view on a sheet (creates a Viewport).
///
/// Params:
///   - sheetId:  long, required
///   - viewId:   long, required
///   - location: { x, y }, optional (center point on sheet in sheet coordinates)
/// </summary>
public sealed class PlaceViewOnSheetCommand : IRevitCommand
{
    public string Name => "place_view_on_sheet";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;

        var sheetId = new ElementId(P.Long(p, "sheetId"));
        var viewId = new ElementId(P.Long(p, "viewId"));

        if (doc.GetElement(sheetId) is not ViewSheet)
            throw new RevitCommandException("invalid_parameter", "sheetId must identify a sheet.");
        var location = p["location"] is JsonObject loc
            ? new XYZ(P.DblOr(loc, "x", 0), P.DblOr(loc, "y", 0), 0)
            : new XYZ(1.0, 0.75, 0);

        // Schedules are native ScheduleSheetInstances, never Viewports or drawn tables.
        // Their insertion point is the upper-left corner, in sheet feet.
        if (doc.GetElement(viewId) is ViewSchedule schedule)
        {
            if (schedule.IsTemplate || schedule.IsTitleblockRevisionSchedule)
                throw new RevitCommandException("invalid_parameter", "This schedule cannot be placed independently.");
            if (new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>().Any(s => s.OwnerViewId == sheetId && s.ScheduleId == viewId))
                throw new RevitCommandException("already_exists", "Schedule is already on this sheet; move the existing instance.");
            var instance = ScheduleSheetInstance.Create(doc, sheetId, viewId, location);
            doc.Regenerate();
            return new JsonObject
            {
                ["scheduleInstanceId"] = instance.Id.Value,
                ["sheetId"] = sheetId.Value,
                ["viewId"] = viewId.Value,
                ["placementKind"] = "ScheduleSheetInstance",
                ["locationUnits"] = "feet",
            };
        }

        if (!Viewport.CanAddViewToSheet(doc, sheetId, viewId))
            throw new RevitCommandException("invalid_parameter",
                "Cannot add this view to the sheet — it may already be on another sheet or be a parent view.");

        var viewport = Viewport.Create(doc, sheetId, viewId, location);

        return new JsonObject
        {
            ["viewportId"] = viewport.Id.Value,
            ["sheetId"] = sheetId.Value,
            ["viewId"] = viewId.Value,
        };
    }
}
