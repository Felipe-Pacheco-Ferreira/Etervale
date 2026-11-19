using PristonTale.Application.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// NPCs the world options remove are placed in no field; the others keep their placements.
internal static class RemovedNpcTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the removed NPCs, the event girls by default, are placed in no field while the others stay", RemovedAsync);
    }

    private static Task RemovedAsync()
    {
        var npcs = new NpcCatalog([
            new NpcDefinition(1, "Event Girl", "girl.inx", []),
            new NpcDefinition(2, "Event Girl 2", "girl2.inx", []),
            new NpcDefinition(3, "Warehouse Keeper", "keeper.inx", [])
        ]);
        var spawns = FieldSpawnData.Empty with { FixedNpcs = [new FixedNpc(1, 0, 0, 0), new FixedNpc(2, 10, 0, 0), new FixedNpc(3, 20, 0, 0)] };
        var placed = WorldServer.WithoutRemovedNpcs(spawns, npcs, WorldServerOptions.Default.RemovedNpcs);
        Assert.True(placed.FixedNpcs.Count == 1 && placed.FixedNpcs[0].NpcCode == 3, "an event girl was still placed or another NPC was removed");
        Assert.True(ReferenceEquals(WorldServer.WithoutRemovedNpcs(spawns, npcs, []), spawns), "an empty removal list changed the placements");
        return Task.CompletedTask;
    }
}
