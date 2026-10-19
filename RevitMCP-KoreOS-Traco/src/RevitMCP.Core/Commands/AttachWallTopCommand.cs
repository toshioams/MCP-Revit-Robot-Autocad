using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
namespace RevitMCPAddin.Commands;
public sealed class AttachWallTopCommand : IRevitCommand
{
    public string Name => "attach_wall_top";
    public bool IsReadOnly => false;
    public JsonNode? Execute(CommandContext ctx)
    {
        var doc=ctx.RequireDoc();
        var wall=doc.GetElement(new ElementId(P.Long(ctx.Parameters,"wallId"))) as Wall
            ?? throw new RevitCommandException("invalid_parameter","wallId must identify a wall.");
        var target=new ElementId(P.Long(ctx.Parameters,"roofId"));
        if(doc.GetElement(target) is not RoofBase || !Wall.IsValidTargetAttachment(doc,target))
            throw new RevitCommandException("invalid_parameter","roofId must identify a valid roof attachment target.");
        if(wall.Pinned || wall.GroupId!=ElementId.InvalidElementId)
            throw new RevitCommandException("invalid_parameter","Pinned/grouped walls are not supported.");
        var existing=wall.GetAttachmentIds(AttachmentLocation.Top);
        var changed=!existing.Contains(target);
        if(changed && existing.Count>0)
            throw new RevitCommandException("attachment_conflict","Wall already has a different top attachment; inspect before replacing it.");
        if(changed)wall.AddAttachment(target,AttachmentLocation.Top);
        doc.Regenerate();
        if(!wall.GetAttachmentIds(AttachmentLocation.Top).Contains(target))
            throw new RevitCommandException("verification_failed","Top attachment was not retained.");
        return new JsonObject { ["wallId"]=wall.Id.Value,["roofId"]=target.Value,["changed"]=changed,
            ["note"]="Native top attachment. Does not create structural supports, bearings or trim. Review section geometry." };
    }
}
