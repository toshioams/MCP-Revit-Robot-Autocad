using System;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Show or hide whole categories in views (Visibility/Graphics), e.g. section marks hidden in plans by a
/// detached office template — a plan without its section and elevation marks is not a valid drawing.
///
/// Params:
///   - viewIds:    long[], required
///   - categories: string[], required — BuiltInCategory names ("OST_Sections") or display names in the UI
///                 language ("Seções")
///   - hidden:     bool, required
/// Views whose visibility is controlled by a view template are refused (change the template instead).
/// </summary>
public sealed class SetCategoryVisibilityCommand : IRevitCommand
{
    public string Name => "set_category_visibility";
    public bool IsReadOnly => false;
    public string RiskLevel => "low";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var hidden = P.BoolFrom(p["hidden"], "hidden");
        var viewIds = P.Arr(p, "viewIds");
        var names = P.Arr(p, "categories");
        if (viewIds.Count == 0 || names.Count == 0)
            throw new RevitCommandException("invalid_parameter", "viewIds and categories must not be empty.");

        var categories = names.Select(n =>
        {
            var name = P.StrFrom(n, "categories[]");
            if (Enum.TryParse<BuiltInCategory>(name, out var bic))
                return Category.GetCategory(doc, bic)
                    ?? throw new RevitCommandException("not_found", $"Category {name} does not exist in this document.");
            return doc.Settings.Categories.Cast<Category>().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new RevitCommandException("not_found", $"Category '{name}' not found; use a BuiltInCategory name or the UI display name.");
        }).ToList();

        var results = new JsonArray();
        foreach (var node in viewIds)
        {
            var view = doc.GetElement(new ElementId(P.LongFrom(node, "viewIds[]"))) as View
                ?? throw new RevitCommandException("not_found", "viewIds must identify views.");
            if (view.ViewTemplateId != ElementId.InvalidElementId)
                throw new RevitCommandException("template_controlled",
                    $"View '{view.Name}' is controlled by a view template; change the template or detach it first.");
            var changed = new JsonArray();
            foreach (var cat in categories)
            {
                if (!view.CanCategoryBeHidden(cat.Id))
                    throw new RevitCommandException("invalid_parameter", $"Category '{cat.Name}' cannot be hidden or shown in view '{view.Name}'.");
                view.SetCategoryHidden(cat.Id, hidden);
                changed.Add(cat.Name);
            }
            doc.Regenerate();
            if (categories.Any(c => view.GetCategoryHidden(c.Id) != hidden))
                throw new RevitCommandException("verification_failed", $"View '{view.Name}' did not retain the category visibility.");
            results.Add(new JsonObject { ["viewId"] = view.Id.Value, ["viewName"] = view.Name, ["categories"] = changed, ["hidden"] = hidden });
        }
        return new JsonObject { ["views"] = results };
    }
}
