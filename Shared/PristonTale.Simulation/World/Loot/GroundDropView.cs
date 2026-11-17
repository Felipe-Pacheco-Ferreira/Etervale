using System.Numerics;

namespace PristonTale.Simulation.World.Loot;

// A drop as clients see it; Id names it on the wire and in the pick-up command.
public sealed record GroundDropView(Guid Id, uint ItemCode, string CatalogKey, string Name, int Amount, bool Gold, Vector3 Position)
{
    public bool HasEquipmentDetails { get; init; }
    public int RequiredLevel { get; init; }
    public uint SpecJob { get; init; }
    public bool Rare { get; init; }
}

public sealed record GroundDropState(GroundDropView View, ulong OwnerId, bool Public);
