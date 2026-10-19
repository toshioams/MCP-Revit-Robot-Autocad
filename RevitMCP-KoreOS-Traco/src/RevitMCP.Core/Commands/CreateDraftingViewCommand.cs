using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>Create a drafting view for map extracts, legends and other 2D documentation.</summary>
public sealed class CreateDraftingViewCommand : IRevitCommand
{
    public string Name => "create_drafting_view";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting)
            ?? throw new RevitCommandException("not_found", "No drafting ViewFamilyType is loaded.");
        var view = ViewDrafting.Create(doc, vft.Id);
        if (P.StrOrNull(p, "viewName") is string name && !string.IsNullOrWhiteSpace(name))
            view.Name = name;
        if (P.LongOrNull(p, "scale") is long scale)
        {
            if (scale < 1 || scale > 24000)
                throw new RevitCommandException("invalid_parameter", "scale must be between 1 and 24000.");
            view.Scale = checked((int)scale);
        }
        return new JsonObject { ["id"] = view.Id.Value, ["name"] = view.Name, ["scale"] = view.Scale };
    }
}
