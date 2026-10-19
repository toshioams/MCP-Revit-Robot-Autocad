using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
namespace RevitMCPAddin.Commands;
public sealed class SetFamilyOrientationCommand : IRevitCommand
{
    public string Name => "set_family_orientation";
    public bool IsReadOnly => false;
    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var f = doc.GetElement(new ElementId(P.Long(p,"id"))) as FamilyInstance
            ?? throw new RevitCommandException("invalid_parameter","Expected family instance.");
        bool? facing = p["facingFlipped"]?.GetValue<bool>();
        bool? hand = p["handFlipped"]?.GetValue<bool>();
        if (facing is null && hand is null) throw new RevitCommandException("invalid_parameter","Specify a target state.");
        if (f.Pinned || f.GroupId != ElementId.InvalidElementId) throw new RevitCommandException("invalid_parameter","Pinned/grouped instance.");
        var host = f.Host?.Id;
        var point = (f.Location as LocationPoint)?.Point;
        bool changed = false;
        if (facing.HasValue && facing != f.FacingFlipped) {
            if (!f.CanFlipFacing || !f.flipFacing()) throw new RevitCommandException("unsupported_operation","Cannot flip facing.");
            changed = true;
        }
        if (hand.HasValue && hand != f.HandFlipped) {
            if (!f.CanFlipHand || !f.flipHand()) throw new RevitCommandException("unsupported_operation","Cannot flip hand.");
            changed = true;
        }
        doc.Regenerate();
        var after = (f.Location as LocationPoint)?.Point;
        if ((facing.HasValue && facing != f.FacingFlipped) || (hand.HasValue && hand != f.HandFlipped)
            || host != f.Host?.Id || (point is not null && (after is null || point.DistanceTo(after)>1e-5)))
            throw new RevitCommandException("verification_failed","Unexpected orientation, host or insertion point.");
        return new JsonObject { ["id"]=f.Id.Value, ["changed"]=changed, ["facingFlipped"]=f.FacingFlipped,
            ["handFlipped"]=f.HandFlipped, ["requiresVisualReview"]=true };
    }
}
