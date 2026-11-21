using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Application.World;

// Players fighting each other: two duellists only each other (DuelRules), arena players only their match's other side
// (ArenaRules), Battle Royale players only each other while neither is out (RoyaleRules), else in the castle's battle zone by the siege's rules and in a hunting field by the PvP mode (PvpRules).
// Outside a duel or a match, locking a player as the target or a blow that hurts in a hunting field holds both sides'
// PvP on, and a kill there marks the killer a murderer unless the victim was one. Blows in a hunting field and the arena
// take PvpDamagePercent. The step runs in parallel with other fields: what the world must settle (a murder, a duel won,
// a player out of its match) waits in this field's lists until WorldServer takes them on the tick.
public sealed partial class FieldInstance
{
    private readonly List<(WorldPlayer Killer, WorldPlayer Victim)> _murders = [];
    private readonly List<(WorldPlayer Attacker, WorldPlayer Target)> _spared = [];

    // A hunting field, where players with the PvP mode on fight (PvpRules.FieldAllows).
    internal bool PvpField => PvpRules.FieldAllows(State);

    // The arena's field, where matches run (ArenaRules.FieldCode).
    internal bool ArenaField => FieldId == ArenaRules.FieldCode;

    // The running Battle Royale's potions lying in this field (the arena's), null without one; the tick sets it.
    internal IReadOnlySet<Guid>? EventDrops { get; set; }

    // The percent of a blow's damage players take from each other in a hunting field and the arena (pvp.damage_percent).
    internal int PvpDamagePercent
    {
        get => _combat.PvpDamagePercent;
        set => _combat.PvpDamagePercent = value;
    }

    internal bool MayFight(WorldPlayer attacker, WorldPlayer target) =>
        attacker.EntityId != target.EntityId && !attacker.IsDead && !target.IsDead && !attacker.Hidden && !target.Hidden
        && (attacker.Duel is not null || target.Duel is not null || attacker.Arena is not null || target.Arena is not null || attacker.Royale is not null
            || target.Royale is not null
            ? attacker.Spares(target)
            : PlayersFight && CastleSiegeRules.MayFight(attacker.Position, attacker.Clan?.ClanId ?? 0, target.Position, target.Clan?.ClanId ?? 0)
                || PvpRules.MayFight(State, attacker.Pvp, target.Pvp, target.Murderer, attacker.SharesPartyWith(target),
                    PvpRules.SameClan(attacker.Clan?.ClanId ?? 0, target.Clan?.ClanId ?? 0)));

    bool IFieldCombatHost.MayFight(WorldPlayer attacker, WorldPlayer target) => MayFight(attacker, target);

    bool IFieldCombatHost.ScalesPlayerBlows => PvpField || ArenaField;

    void IFieldCombatHost.Spared(WorldPlayer attacker, WorldPlayer target) => _spared.Add((attacker, target));

    bool IFieldSkillHost.MayFight(WorldPlayer attacker, WorldPlayer target) => MayFight(attacker, target);

    bool IFieldSkillHost.PlayersFight => PlayersFight || PvpField || ArenaField;

    // A blow that hurt. In an arena match it counts for the attacker's side (the tie-break at the time limit). Else, in a
    // hunting field outside a duel (the castle keeps its siege's rules): both sides' PvP holds; a kill leaves the
    // victim's restart free and marks the killer, which the world then tells it.
    internal void RecordPvpBlow(WorldPlayer attacker, WorldPlayer target, int damage)
    {
        if (attacker.Arena is { } seat && attacker.ArenaFoeOf(target))
        {
            seat.Match.Damage[seat.Side] += damage;
            return;
        }

        if (attacker.Royale is { } royaleSeat && attacker.RoyaleFoeOf(target))
        {
            royaleSeat.Damage += damage;
            return;
        }

        if (!PvpField || attacker.DuelsWith(target))
        {
            return;
        }

        attacker.HoldPvp();
        target.HoldPvp();
        if (!target.IsDead)
        {
            return;
        }

        target.FallToPlayer();
        if (PvpRules.MakesMurderer(target.Murderer))
        {
            attacker.MarkMurderer();
            _murders.Add((attacker, target));
        }
    }

    // Tick thread, after the step: the murders and the spared blows (duels won, players out of a match) since the last call.
    internal void TakeFightResults(List<(WorldPlayer Killer, WorldPlayer Victim)> murders, List<(WorldPlayer Attacker, WorldPlayer Target)> spared)
    {
        murders.AddRange(_murders);
        spared.AddRange(_spared);
        _murders.Clear();
        _spared.Clear();
    }
}
