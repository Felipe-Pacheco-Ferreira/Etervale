using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

// Gold: the picker's part; Shares: the parts of the party members who split the gold with it; MonsterItem: the item
// picked up when a monster dropped it (what the reward goals count).
public sealed record PreparedPickupOperation(
    PlayerCharacter Original,
    PlayerCharacter Candidate,
    CharacterSaveSnapshot Snapshot,
    InventoryOutcome Outcome,
    FieldGroundLoot Ground,
    Guid DropId,
    Guid OperationId,
    long Gold,
    IReadOnlyList<GoldShare> Shares,
    ItemInstance? MonsterItem = null)
    : PreparedCharacterOperation<InventoryOutcome>(Original, Candidate, Snapshot, Outcome);

// A party member's part of picked-up gold. It is a reward, as a kill's experience is: given once the picker's pickup is
// stored and stored with the member's next save, so no member's save gate is held for it.
public sealed record GoldShare(ulong MemberId, PlayerCharacter Character, long Gold);
