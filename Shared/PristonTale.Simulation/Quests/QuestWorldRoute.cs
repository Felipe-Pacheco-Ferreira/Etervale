using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Quests;

public static class QuestWorldRoute
{
    public static RouteExit FindFirstExit(WorldCatalog world, int from, int target, int level)
    {
        if (!world.TryGetFieldByCode(from, out var start))
        {
            return null;
        }

        var seen = new HashSet<int> { from };
        var pending = new Queue<(FieldDef Field, RouteExit First)>();
        pending.Enqueue((start, null));
        while (pending.TryDequeue(out var current))
        {
            var code = current.Field.FieldCode;
            if (code == target)
            {
                return current.First;
            }

            // The Ricarten seam is traversable in both directions, but only Forest 1 declares a gate.
            foreach (var exit in AdventurerJourneyRoute.Tempskron.Exits)
            {
                if (exit.From == code && world.TryGetFieldByCode(exit.To, out var next))
                {
                    Add(next, exit.Point, exit.Seam);
                }
            }

            foreach (var gate in current.Field.Gates)
            {
                AddGate(gate.DestinationFieldId, gate.X, gate.Z, seam: true);
            }

            foreach (var gate in current.Field.WarpGates)
            {
                if (!WingGates.IsStone(gate) && level >= gate.LimitLevel)
                {
                    AddGate(gate.DestinationFieldId, gate.X, gate.Z, seam: false);
                }
            }

            void AddGate(string destination, int x, int z, bool seam)
            {
                if (world.TryGetField(destination, out var field))
                {
                    Add(field, new RoutePoint(code, x, z), seam);
                }
            }

            void Add(FieldDef field, RoutePoint point, bool seam)
            {
                if (level >= field.LimitLevel && seen.Add(field.FieldCode))
                {
                    pending.Enqueue((field, current.First ?? new RouteExit(code, field.FieldCode, point, seam)));
                }
            }
        }

        return null;
    }
}
