using PristonTale.Simulation.Combat;

namespace PristonTale.Application.World;

// The player's PvP mode (PvpRules): the switch, the hold after a blow between players, the murderer's mark and a death
// at another player's hands. Every viewer sees Pvp and Murderer in the player's looks.
public sealed partial class WorldPlayer
{
    public bool Pvp { get; private set; }

    public float PvpCombatMsLeft { get; private set; }

    public float MurdererMsLeft { get; private set; }

    public bool Murderer => MurdererMsLeft > 0f;

    // The player's last death came from another player: standing up costs nothing (RestartTransactions).
    public bool SlainByPlayer { get; private set; }

    // The duel the player fights (DuelRules), null for none; WorldServer.Duels keeps it.
    public Duel? Duel { get; internal set; }

    // A challenge to a duel waiting for this player's answer.
    public DuelRequest? DuelRequest { get; internal set; }

    // Fighting one duel together: only then do two duellists fight, and nobody else fights either.
    public bool DuelsWith(WorldPlayer other) => Duel is { } duel && ReferenceEquals(duel, other.Duel);

    // The player's place in an arena match (ArenaRules), null outside one; WorldServer.Arena keeps it.
    public ArenaSeat? Arena { get; internal set; }

    // The arena queue entry the player waits in, null for none.
    public ArenaQueueEntry? ArenaQueued { get; internal set; }

    // Players of one match on opposite sides, neither out: only they fight each other.
    public bool ArenaFoeOf(WorldPlayer other) => Arena is { } seat && other.Arena is { } theirs && seat.FoeOf(theirs);

    // A fight where nobody dies: a blow that would kill leaves 1 life and ends the fight for the one it falls on.
    public bool Spares(WorldPlayer other) => DuelsWith(other) || ArenaFoeOf(other) || RoyaleFoeOf(other);

    // Asked on (wanted) or off; a switch already that way answers as if it had just turned.
    public PvpSwitch SwitchPvp(bool wanted)
    {
        if (wanted == Pvp)
        {
            return wanted ? PvpSwitch.On : PvpSwitch.Off;
        }

        var answer = PvpRules.Switch(wanted, Level, PvpCombatMsLeft, MurdererMsLeft);
        if (answer is PvpSwitch.On or PvpSwitch.Off)
        {
            Pvp = answer == PvpSwitch.On;
        }

        return answer;
    }

    // A blow between this player and another: the switch stays on for PvpRules.CombatHoldMs.
    internal void HoldPvp() => PvpCombatMsLeft = PvpRules.CombatHoldMs;

    internal void MarkMurderer() => MurdererMsLeft = PvpRules.MurdererMs;

    internal void FallToPlayer() => SlainByPlayer = true;

    // Entering again with what was left of the hold and the mark when it left (WorldServer.Pvp): PvP on while either runs.
    internal void RestorePvp(float holdMs, float murdererMs)
    {
        PvpCombatMsLeft = MathF.Max(0f, holdMs);
        MurdererMsLeft = MathF.Max(0f, murdererMs);
        Pvp = PvpCombatMsLeft > 0f || Murderer;
    }

    // Every tick, dead or alive.
    internal void TickPvp(float dtMs)
    {
        PvpCombatMsLeft = MathF.Max(0f, PvpCombatMsLeft - dtMs);
        MurdererMsLeft = MathF.Max(0f, MurdererMsLeft - dtMs);
    }
}
