using System;
using System.IO;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>Link or import a raster image into a 2D view with an explicit model-space size.</summary>
public sealed class PlaceImageInViewCommand : IRevitCommand
{
    public string Name => "place_image_in_view";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var path = P.Str(p, "path");
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new RevitCommandException("invalid_parameter", "path must be an existing absolute local image path.");
        var view = p["viewId"] is null ? doc.ActiveView : doc.GetElement(new ElementId(P.Long(p, "viewId"))) as View;
        if (view is null || !ImageInstance.IsValidView(view))
            throw new RevitCommandException("unsupported_view", "The target view cannot host raster images.");
        var units = P.Units(p);
        var width = P.Dbl(p, "width") * (units == "feet" ? 1.0 : P.MetersToFeet);
        if (!double.IsFinite(width) || width <= 0)
            throw new RevitCommandException("invalid_parameter", "width must be positive.");
        var center = p["center"] is JsonObject ? P.Xyz(p, "center", units) : XYZ.Zero;
        var source = P.BoolOr(p, "link", true) ? ImageTypeSource.Link : ImageTypeSource.Import;
        using var options = new ImageTypeOptions(path, false, source);
        var imageType = ImageType.Create(doc, options);
        var instance = ImageInstance.Create(doc, view, imageType.Id,
            new ImagePlacementOptions(center, BoxPlacement.Center));
        instance.LockProportions = true;
        instance.Width = width;
        doc.Regenerate();
        return new JsonObject
        {
            ["imageInstanceId"] = instance.Id.Value,
            ["imageTypeId"] = imageType.Id.Value,
            ["viewId"] = view.Id.Value,
            ["path"] = path,
            ["linked"] = source == ImageTypeSource.Link,
            ["widthMeters"] = instance.Width * 0.3048,
            ["heightMeters"] = instance.Height * 0.3048,
        };
    }
}
