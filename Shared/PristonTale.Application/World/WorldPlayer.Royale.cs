using PristonTale.Simulation.Characters;

namespace PristonTale.Application.World;

// The player in the Battle Royale (RoyaleRules): registered for the next event, or fighting it as a stand-in
// (RoyaleKits). While it fights, Character is the stand-in and Home the player's own character, which nothing changes:
// saves store the session's character (the own one) and no stored change starts from a stand-in.
public sealed partial class WorldPlayer
{
    // The player's place in the running event, null outside one; WorldServer.Royale keeps it.
    public RoyaleSeat? Royale { get; internal set; }

    // Registered for the next event, with the field and place it registered from.
    public RoyaleEntry? RoyaleEntry { get; internal set; }

    // The player's own character while a stand-in fights for it, null otherwise.
    internal PlayerCharacter? Home { get; private set; }

    // Players of one event, neither out: they fight each other, and nobody else fights them.
    public bool RoyaleFoeOf(WorldPlayer other) =>
        Royale is { Out: false } seat && other.Royale is { Out: false } theirs && ReferenceEquals(seat.Event, theirs.Event) && !ReferenceEquals(seat, theirs);

    // A heal or buff from this player reaches the other: in the Battle Royale everyone stands alone, so only itself.
    public bool MaySupport(WorldPlayer other) => ReferenceEquals(this, other) || Royale is null && other.Royale is null;

    internal void TakeStandIn(PlayerCharacter standIn)
    {
        Home ??= _character;
        UseCharacter(standIn);
    }

    internal void ReturnHome()
    {
        if (Home is { } home)
        {
            Home = null;
            UseCharacter(home);
        }
    }

    // A swap is no progress: the cues start again from the character now worn.
    private void UseCharacter(PlayerCharacter character)
    {
        _character = character;
        ClearAttackTarget();
        _cueLevel = character.Level;
        _cueSkillRevision = character.Skills.Revision;
        _cueRanks = TrainedRanks();
    }
}
