using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
namespace RevitMCPAddin.Commands;
/// <summary>
/// Reverse of attach_wall_top. Added after the pilot found no tool path to undo a
/// committed attachment. After removal the top follows the wall's own top constraint.
/// </summary>
public sealed class DetachWallTopCommand : IRevitCommand
{
    public string Name => "detach_wall_top";
    public bool IsReadOnly => false;
    public JsonNode? Execute(CommandContext ctx)
    {
        var doc=ctx.RequireDoc();
        var wall=doc.GetElement(new ElementId(P.Long(ctx.Parameters,"wallId"))) as Wall
            ?? throw new RevitCommandException("invalid_parameter","wallId must identify a wall.");
        var target=new ElementId(P.Long(ctx.Parameters,"roofId"));
        if(wall.Pinned || wall.GroupId!=ElementId.InvalidElementId)
            throw new RevitCommandException("invalid_parameter","Pinned/grouped walls are not supported.");
        var (_,topBefore)=CreateProfileWallCommand.SolidZRange(wall);
        var changed=wall.GetAttachmentIds(AttachmentLocation.Top).Contains(target);
        if(changed)wall.RemoveAttachment(target,AttachmentLocation.Top);
        doc.Regenerate();
        if(wall.GetAttachmentIds(AttachmentLocation.Top).Contains(target))
            throw new RevitCommandException("verification_failed","Top attachment is still present.");
        var (_,topAfter)=CreateProfileWallCommand.SolidZRange(wall);
        return new JsonObject { ["wallId"]=wall.Id.Value,["roofId"]=target.Value,["changed"]=changed,
            ["solidTopBeforeMeters"]=topBefore/P.MetersToFeet,["solidTopAfterMeters"]=topAfter/P.MetersToFeet,
            ["remainingTopAttachments"]=wall.GetAttachmentIds(AttachmentLocation.Top).Count,
            ["note"]="Top now follows the wall top constraint/unconnected height. Hosted openings are not moved; review sections." };
    }
}
