using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Place a Room at a given point.
///
/// Params:
///   - location:   { x, y, z? }, required — point inside the room-bounding walls
///   - levelName:  string, optional (defaults to lowest level)
///   - name:       string, optional
///   - number:     string, optional
///   - units:      "meters"|"feet"
/// </summary>
public sealed class CreateRoomCommand : IRevitCommand
{
    public string Name => "create_room";
    public bool IsReadOnly => false;

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var units = P.Units(p);

        var location = P.Xyz(p, "location", units);
        var level = CreateWallCommand.ResolveLevel(doc, P.StrOrNull(p, "levelName"));

        var pt = new UV(location.X, location.Y);
        Room room;
        var phaseName = P.StrOrNull(p, "phaseName");
        if (string.IsNullOrWhiteSpace(phaseName))
            room = doc.Create.NewRoom(level, pt); // last phase of the project
        else
        {
            // Surveys of existing buildings need rooms in the "Existente" phase (NewRoom(level, point) always uses the
            // last phase): place an unplaced room of that phase into the plan circuit that contains the point.
            var phase = doc.Phases.Cast<Phase>().FirstOrDefault(ph => ph.Name == phaseName)
                ?? throw new RevitCommandException("not_found", $"Phase '{phaseName}' not found.");
            var probe = new XYZ(location.X, location.Y, level.Elevation + 1.0);
            Room? placed = null;
            foreach (PlanCircuit circuit in doc.get_PlanTopology(level, phase).Circuits)
            {
                if (circuit.IsRoomLocated) continue;
                using var sub = new SubTransaction(doc);
                sub.Start();
                var candidate = doc.Create.NewRoom(phase);
                doc.Create.NewRoom(candidate, circuit);
                doc.Regenerate();
                if (candidate.IsPointInRoom(probe)) { sub.Commit(); placed = candidate; break; }
                sub.RollBack();
            }
            room = placed ?? throw new RevitCommandException("not_found",
                $"No free enclosed area of phase '{phaseName}' on level '{level.Name}' contains the point (walls not closed, or a room is already there).");
        }

        var nameVal = P.StrOrNull(p, "name");
        if (!string.IsNullOrWhiteSpace(nameVal))
            room.Name = nameVal;

        var numberVal = P.StrOrNull(p, "number");
        if (!string.IsNullOrWhiteSpace(numberVal))
            room.Number = numberVal;

        return new JsonObject
        {
            ["id"] = room.Id.Value,
            ["name"] = room.Name,
            ["number"] = room.Number,
            ["levelName"] = level.Name,
        };
    }
}
