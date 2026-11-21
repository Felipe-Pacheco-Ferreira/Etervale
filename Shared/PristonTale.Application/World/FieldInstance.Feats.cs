using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// A boss slain (Boss, the credited Killer, its party members in the field, the drops) or a player killed by another (the
// Killer and the Victim), as the field saw it; WorldServer names it for the public activity (WorldServer.Feats).
internal sealed record FieldFeat(int FieldId, MonsterDefinition? Boss, WorldPlayer Killer, WorldPlayer[] Party, WorldPlayer? Victim, IReadOnlyList<RolledDrop> Drops);

public sealed partial class FieldInstance
{
    internal Action<FieldFeat>? FeatRaised { get; set; }

    private void RaiseBossFeat(MonsterInstance killed, KillPayout reward)
    {
        if (FeatRaised is { } raise && killed.Def.IsBoss && _players.TryGetValue(reward.CreditedPlayerId, out var killer))
        {
            raise(new FieldFeat(FieldId, killed.Def, killer, PartyHere(killer), null, reward.Drops));
        }
    }

    // A blow between players: the siege's board, the arena's damage or the PvP mode's hold and murderer, and the feat when
    // it killed.
    private void OnPlayerHurtPlayer(WorldPlayer attacker, WorldPlayer target, int damage)
    {
        RecordSiegeBlow(attacker, target, damage);
        RecordPvpBlow(attacker, target, damage);
        if (target.IsDead && FeatRaised is { } raise)
        {
            raise(new FieldFeat(FieldId, null, attacker, [], target, []));
        }
    }

    // The killer's party members in this field, the killer left out.
    private WorldPlayer[] PartyHere(WorldPlayer killer)
    {
        if (killer.Party is not { } party)
        {
            return [];
        }

        var members = new List<WorldPlayer>();
        foreach (var id in party.Members)
        {
            if (id != killer.EntityId && _players.TryGetValue(id, out var member))
            {
                members.Add(member);
            }
        }

        return members.ToArray();
    }
}
