using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// One in the boss roulette: the player, the damage it dealt the boss and its chance (0 to 1).
internal readonly record struct RouletteEntrant(WorldPlayer Player, long Damage, float Chance);

// A boss roulette drawn in a field: the boss, the winner's index among the entrants and the prize's catalog key.
internal sealed record RouletteDraw(int FieldId, MonsterDefinition Boss, RouletteEntrant[] Entrants, int Winner, string Prize);

public sealed partial class FieldInstance
{
    // The roulettes drawn this step, taken by the world after the fields step (WorldServer.AnnounceRoulettes).
    internal List<RouletteDraw> RouletteDraws { get; } = [];

    // A scheduled boss's death (BossRoulette): those who dealt their share and their party members here, by the field's
    // generator.
    private void DrawBossRoulette(MonsterInstance killed, KillPayout reward)
    {
        if (!killed.Scheduled || reward.BossDamage is not { Count: > 0 } damage)
        {
            return;
        }

        var players = new List<WorldPlayer>();
        foreach (var (id, dealt) in damage)
        {
            if (BossRoulette.Qualifies(dealt, killed.Def.Combat.MaxLife) && _players.TryGetValue(id, out var player))
            {
                players.Add(player);
            }
        }

        for (var index = players.Count - 1; index >= 0; index--)
        {
            foreach (var member in PartyHere(players[index]))
            {
                if (!players.Contains(member))
                {
                    players.Add(member);
                }
            }
        }

        if (players.Count == 0)
        {
            return;
        }

        var dealtBy = new long[players.Count];
        for (var index = 0; index < dealtBy.Length; index++)
        {
            dealtBy[index] = damage.GetValueOrDefault(players[index].EntityId);
        }

        var chances = BossRoulette.Chances(dealtBy);
        var entrants = new RouletteEntrant[players.Count];
        for (var index = 0; index < entrants.Length; index++)
        {
            entrants[index] = new RouletteEntrant(players[index], dealtBy[index], chances[index]);
        }

        RouletteDraws.Add(new RouletteDraw(FieldId, killed.Def, entrants, BossRoulette.Draw(chances, _rng), BossRoulette.Prize(killed.Def.Combat.Level, _rng)));
    }
}
