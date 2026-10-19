using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

namespace RevitMCPAddin.Commands;

/// <summary>Creates an independent Generic appearance, optionally with a tiled opacity cutout.
/// Existing materials are never modified. White in the cutout is opaque, black is empty.
/// Graphics transparency is a shaded-view approximation; realistic/rendered views use the bitmap.</summary>
public sealed class CreateMaterialCommand : IRevitCommand
{
    public string Name => "create_material";
    public bool IsReadOnly => false;
    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc(); var p = ctx.Parameters;
        var name = P.Str(p, "name");
        if (string.IsNullOrWhiteSpace(name)) throw new RevitCommandException("invalid_parameter", "name is required.");
        if (new FilteredElementCollector(doc).OfClass(typeof(Material)).Any(e => e.Name == name))
            throw new RevitCommandException("name_collision", "Material already exists; inspect it or choose a new name.");
        var hex = P.StrOrNull(p, "color") ?? "#707070";
        if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$"))
            throw new RevitCommandException("invalid_parameter", "color must be #RRGGBB.");
        var color = new Color(Convert.ToByte(hex.Substring(1,2),16), Convert.ToByte(hex.Substring(3,2),16), Convert.ToByte(hex.Substring(5,2),16));
        var transparency = P.IntOr(p,"graphicsTransparency",0);
        if (transparency < 0 || transparency > 100) throw new RevitCommandException("invalid_parameter", "graphicsTransparency must be 0..100.");
        var path = P.StrOrNull(p,"cutoutImagePath");
        var scale = P.DblOr(p,"textureScaleMeters",.05);
        if (!double.IsFinite(scale) || scale <= 0) throw new RevitCommandException("invalid_parameter", "textureScaleMeters must be positive and finite.");
        if (path is not null && (!Path.IsPathFullyQualified(path) || !File.Exists(path)))
            throw new RevitCommandException("invalid_parameter", "cutoutImagePath must be an existing absolute local image path.");
        var hatchSpacing = P.DblOr(p,"crosshatchSpacingMeters",.05);
        var hatchAngle = P.DblOr(p,"crosshatchAngleDegrees",45);
        var hatch = P.BoolOr(p,"crosshatch",false);
        if (!double.IsFinite(hatchSpacing) || hatchSpacing < .005 || hatchSpacing > 1 || !double.IsFinite(hatchAngle))
            throw new RevitCommandException("invalid_parameter", "Crosshatch spacing must be 0.005..1 meters and angle finite.");
        var template = ctx.App.Application.GetAssets(AssetType.Appearance)
            .FirstOrDefault(a => a.FindByName(Generic.GenericDiffuse) is not null && a.FindByName(Generic.GenericCutoutOpacity) is not null)
            ?? throw new RevitCommandException("not_found", "No Generic appearance template with Cutouts is available.");
        var appearance = AppearanceAssetElement.Create(doc, name + " - Appearance", template);
        using (var edit = new AppearanceAssetEditScope(doc))
        {
            var asset = edit.Start(appearance.Id);
            var diffuse = asset.FindByName(Generic.GenericDiffuse) as AssetPropertyDoubleArray4d
                ?? throw new RevitCommandException("unsupported_type", "Generic diffuse property unavailable.");
            if (diffuse.NumberOfConnectedProperties > 0) diffuse.RemoveConnectedAsset();
            diffuse.SetValueAsColor(color);
            var opacity = asset.FindByName(Generic.GenericCutoutOpacity) as AssetPropertyDouble
                ?? throw new RevitCommandException("unsupported_type", "Generic cutout property unavailable.");
            if (opacity.NumberOfConnectedProperties > 0) opacity.RemoveConnectedAsset();
            opacity.Value = 1;
            if (asset.FindByName(Generic.GenericTransparency) is AssetPropertyDouble tr) {
                if (tr.NumberOfConnectedProperties > 0) tr.RemoveConnectedAsset();
                tr.Value = 0;
            }
            if (path is not null)
            {
                opacity.AddConnectedAsset("UnifiedBitmap");
                var bitmap = opacity.GetSingleConnectedAsset();
                (bitmap.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString
                    ?? throw new RevitCommandException("unsupported_type","Bitmap property unavailable.")).Value = path;
                foreach (var key in new[] { UnifiedBitmap.TextureRealWorldScaleX, UnifiedBitmap.TextureRealWorldScaleY })
                    (bitmap.FindByName(key) as AssetPropertyDistance
                        ?? throw new RevitCommandException("unsupported_type","Bitmap real-world scale unavailable.")).Value = scale * P.MetersToFeet;
                foreach (var key in new[] { UnifiedBitmap.TextureURepeat, UnifiedBitmap.TextureVRepeat })
                    (bitmap.FindByName(key) as AssetPropertyBoolean
                        ?? throw new RevitCommandException("unsupported_type","Bitmap repeat property unavailable.")).Value = true;
            }
            edit.Commit(false);
        }
        var mat = (Material)doc.GetElement(Material.Create(doc,name));
        mat.Color = color; mat.Transparency = transparency; mat.UseRenderAppearanceForShading = false;
        mat.AppearanceAssetId = appearance.Id;
        mat.MaterialClass = P.StrOrNull(p,"materialClass") ?? "Metal";
        if (hatch)
        {
            var patternName = name + " - Crosshatch";
            if (new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Any(e=>e.Name==patternName))
                throw new RevitCommandException("name_collision","Crosshatch pattern name already exists; choose another material name.");
            using var pattern = new FillPattern(patternName, FillPatternTarget.Model, FillPatternHostOrientation.ToHost);
            var angle = hatchAngle * Math.PI / 180;
            pattern.SetFillGrids(new[] { new FillGrid(angle,hatchSpacing*P.MetersToFeet), new FillGrid(angle+Math.PI/2,hatchSpacing*P.MetersToFeet) });
            var patternElement = FillPatternElement.Create(doc,pattern);
            mat.SurfaceForegroundPatternId = patternElement.Id;
            mat.SurfaceForegroundPatternColor = color;
        }
        var actual = appearance.GetRenderingAsset();
        var actualBitmap = actual.FindByName(Generic.GenericCutoutOpacity)?.GetSingleConnectedAsset();
        var actualPath = (actualBitmap?.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString)?.Value;
        if (path is not null && actualPath != path)
            throw new RevitCommandException("verification_failed","Cutout image was not retained.");
        return new JsonObject { ["materialId"]=mat.Id.Value,["name"]=mat.Name,["appearanceAssetId"]=appearance.Id.Value,
            ["color"]=hex,["graphicsTransparency"]=mat.Transparency,["cutoutImagePath"]=actualPath,
            ["surfacePatternId"]=mat.SurfaceForegroundPatternId.Value,["crosshatchSpacingMeters"]=hatch ? hatchSpacing : null,
            ["textureScaleMeters"]=path is null ? null : scale,
            ["note"]="Independent Generic appearance. Cutout display must be checked in realistic/rendered views. Shaded views use uniform graphics transparency; this is not live visual validation." };
    }
}
