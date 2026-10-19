using System.Linq;
using System;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Set a single parameter on a single element by name.
///
/// Parameters:
///   - id:             long, required
///   - parameterName:  string, required
///   - value:          any, required (string / number / bool / { id: long })
///   - units:          "meters"|"feet"|"internal", optional, default "internal".
///                     Applies unit conversion for Double storage-type parameters
///                     that carry measurable units (length, area, volume, etc.).
///                     "internal" = raw Revit internal units (feet for length).
///                     Pass "meters" or "feet" for automatic conversion via
///                     UnitUtils.ConvertToInternalUnits.  Dimensionless doubles
///                     (ratio, percentage, etc.) are never converted regardless
///                     of this field.
/// </summary>
public sealed class SetParameterCommand : IRevitCommand
{
    public string Name => "set_parameter";
    public bool IsReadOnly => false;
    public string RiskLevel => "medium";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;

        var idValue = P.Long(p, "id");
        var paramName = P.Str(p, "parameterName");
        var valueNode = p["value"]
            ?? throw new RevitCommandException("bad_request", "Missing required parameter 'value'.");
        var units = P.StrOrNull(p, "units") ?? "internal";

        var element = doc.GetElement(new ElementId(idValue))
            ?? throw new RevitCommandException("not_found", $"No element with id {idValue}.");

        var param = ResolveParameter(element, paramName);

        if (param.IsReadOnly)
            throw new RevitCommandException("read_only_parameter", $"Parameter '{paramName}' is read-only.");

        var previousValueString = SafeAsValueString(param);

        bool wrote;
        bool unitConversionApplied = false;

        switch (param.StorageType)
        {
            case StorageType.String:
                wrote = param.Set(P.StrFrom(valueNode, "value"));
                break;

            case StorageType.Integer:
                wrote = valueNode.GetValueKind() is System.Text.Json.JsonValueKind.True
                                                  or System.Text.Json.JsonValueKind.False
                    ? param.Set(P.BoolFrom(valueNode, "value") ? 1 : 0)
                    : param.Set(P.IntFrom(valueNode, "value"));
                break;

            case StorageType.Double:
            {
                var raw = P.DblFrom(valueNode, "value");
                var converted = ConvertToInternal(param, raw, units, out unitConversionApplied);
                wrote = param.Set(converted);
                break;
            }

            case StorageType.ElementId:
            {
                long target;
                if (valueNode is JsonObject obj && obj["id"] is JsonNode idn)
                    target = P.LongFrom(idn, "value.id");
                else if (valueNode is JsonValue sv && sv.TryGetValue<string>(out var byName) && !long.TryParse(byName, out _))
                    target = ResolveElementIdByName(doc, param, byName);
                else
                    target = P.LongFrom(valueNode, "value");
                wrote = param.Set(new ElementId(target));
                break;
            }

            default:
                throw new RevitCommandException("invalid_parameter",
                    $"Unsupported StorageType '{param.StorageType}' for '{paramName}'.");
        }

        ctx.RequireDoc().Regenerate();
        var newValueString = SafeAsValueString(param);

        var result = new JsonObject
        {
            ["id"] = element.Id.Value,
            ["parameterName"] = paramName,
            ["storageType"] = param.StorageType.ToString(),
            ["written"] = wrote,
            ["newValueString"] = newValueString,
            ["changes"] = new JsonObject
            {
                ["before"] = previousValueString,
                ["after"] = newValueString,
            },
            ["changeSummary"] = $"Set '{paramName}' on element {idValue}: '{previousValueString}' → '{newValueString}'",
        };

        if (param.StorageType == StorageType.Double)
        {
            result["inputUnits"] = units;
            result["unitConversionApplied"] = unitConversionApplied;
        }

        return result;
    }

    /// <summary>
    /// Convert a user-supplied double to Revit internal units based on the
    /// parameter's spec type.  The branching decision (which unit, or fail
    /// closed) lives in the pure, unit-tested <see cref="UnitConversionPolicy"/>;
    /// this method only does the Revit-coupled work of classifying the
    /// <c>ForgeTypeId</c> and calling <c>UnitUtils.ConvertToInternalUnits</c>.
    /// </summary>
    internal static double ConvertToInternal(Parameter param, double value, string units,
        out bool conversionApplied)
    {
        conversionApplied = false;

        var spec = ClassifySpec(param.Definition.GetDataType());
        var choice = UnitConversionPolicy.Decide(spec, units, param.Definition.Name);
        if (choice == UnitChoice.NoConversion) return value;

        conversionApplied = true;
        return UnitUtils.ConvertToInternalUnits(value, ToUnitTypeId(choice));
    }

    /// <summary>Maps a Revit <c>ForgeTypeId</c> spec to the testable <see cref="UnitSpec"/>.</summary>
    private static UnitSpec ClassifySpec(ForgeTypeId specType)
    {
        if (!UnitUtils.IsMeasurableSpec(specType)) return UnitSpec.Dimensionless;
        if (specType == SpecTypeId.Length) return UnitSpec.Length;
        if (specType == SpecTypeId.Area)   return UnitSpec.Area;
        if (specType == SpecTypeId.Volume) return UnitSpec.Volume;
        // Length-dimension specs other than Length (framing section Largura/Altura use
        // SectionDimension; also bar diameter, displacement...) accept the same units.
        // Seen live 11/09/2026: set_parameter refused meters for a timber section width.
        var valid = UnitUtils.GetValidUnits(specType);
        if (valid.Contains(UnitTypeId.Feet) && valid.Contains(UnitTypeId.Meters)) return UnitSpec.Length;
        return UnitSpec.OtherMeasurable;
    }

    private static ForgeTypeId ToUnitTypeId(UnitChoice choice) => choice switch
    {
        UnitChoice.Meters       => UnitTypeId.Meters,
        UnitChoice.Feet         => UnitTypeId.Feet,
        UnitChoice.SquareMeters => UnitTypeId.SquareMeters,
        UnitChoice.SquareFeet   => UnitTypeId.SquareFeet,
        UnitChoice.CubicMeters  => UnitTypeId.CubicMeters,
        UnitChoice.CubicFeet    => UnitTypeId.CubicFeet,
        _ => throw new RevitCommandException("invalid_parameter",
            $"Internal error: unmapped UnitChoice '{choice}'."),
    };

    private static string? SafeAsValueString(Parameter p)
    {
        try { return p.AsValueString(); } catch { return null; }
    }

    /// <summary>
    /// parameterName may be a BuiltInParameter name ("SPACING_LAYOUT_VERT"). Localized names can repeat on
    /// one element (a curtain wall type has two "Layout", two "Espaçamento"): LookupParameter silently
    /// took the first (live 11/09: could not turn the vertical grid off). Ambiguous writable names fail.
    /// </summary>
    internal static Parameter ResolveParameter(Element element, string paramName)
    {
        Parameter? param = null;
        if (Enum.TryParse<BuiltInParameter>(paramName, false, out var bip) && !int.TryParse(paramName, out _))
            param = element.get_Parameter(bip);
        if (param is null)
        {
            var named = element.GetParameters(paramName).ToList();
            var writable = named.Where(x => !x.IsReadOnly).ToList();
            if (writable.Count > 1)
                throw new RevitCommandException("ambiguous_parameter",
                    $"Element {element.Id.Value} has {writable.Count} writable parameters named '{paramName}' ("
                    + string.Join(", ", writable.Select(x => x.Definition is InternalDefinition d && d.BuiltInParameter != BuiltInParameter.INVALID ? d.BuiltInParameter.ToString() : x.Id.Value.ToString()))
                    + "). Pass the BuiltInParameter name instead.");
            param = writable.FirstOrDefault() ?? named.FirstOrDefault();
        }
        return param ?? throw new RevitCommandException("not_found", $"Element {element.Id.Value} has no parameter named '{paramName}'.");
    }

    /// <summary>
    /// ElementId parameters by name ("Existente" for PHASE_CREATED, "N1-MEZ-AC" for a top level): candidates
    /// are elements of the current value's class, else phases and levels. Live 14/09: phases had to be set
    /// by numeric id.
    /// </summary>
    internal static long ResolveElementIdByName(Document doc, Parameter param, string name)
    {
        var current = param.AsElementId();
        var currentElement = current is not null && current != ElementId.InvalidElementId ? doc.GetElement(current) : null;
        var candidates = new List<Element>();
        if (currentElement is not null)
        {
            try { candidates.AddRange(new FilteredElementCollector(doc).OfClass(currentElement.GetType()).ToElements()); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        }
        if (candidates.Count == 0)
        {
            candidates.AddRange(doc.Phases.Cast<Element>());
            candidates.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Level)).ToElements());
        }
        var matches = candidates.Where(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) return matches[0].Id.Value;
        var kind = currentElement?.GetType().Name ?? "Phase/Level";
        throw matches.Count == 0
            ? new RevitCommandException("not_found", $"No {kind} named '{name}' for parameter '{param.Definition.Name}'. Pass the element id.")
            : new RevitCommandException("ambiguous_value", $"{matches.Count} {kind} elements are named '{name}'. Pass the element id.");
    }
}
