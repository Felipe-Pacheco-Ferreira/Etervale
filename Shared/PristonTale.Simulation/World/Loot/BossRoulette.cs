using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.World.Loot;

// The boss roulette: when a scheduled boss dies, one of those who fought it wins a prize sent by mail. In it are the
// players in the field who dealt at least MinimumSharePercent of the boss's life, and the party members of theirs in the
// field (their support counts, as a heal cannot be tied to one boss). A player's chance follows its share of the damage
// dealt, no lower than FloorPercent (a supporter's) and no higher than CapPercent; a bound that cannot hold (more than
// twenty players under the floor, fewer than four under the cap) is left out.
public static class BossRoulette
{
    public const int MinimumSharePercent = 1;
    public const float FloorPercent = 5f;
    public const float CapPercent = 30f;

    // The prizes by the boss's level: a tier applies from its level up to the next tier's, and each of its stones is
    // equally likely.
    private static readonly (int FromLevel, AgingStone[] Stones)[] Tiers =
    [
        (0, [AgingStone.AgingStone, AgingStone.CopperOre]),
        (80, [AgingStone.SuperAgingStone, AgingStone.ElderCopperOre]),
        (110, [AgingStone.SuperiorCopperOre, AgingStone.SuperAgingStone])
    ];

    public static bool Qualifies(long damage, int maxLife) => maxLife > 0 && damage * 100 >= (long)maxLife * MinimumSharePercent;

    // Each one's chance (0 to 1, summing to 1) from the damage it dealt; 0 for a party member who only supported.
    public static float[] Chances(ReadOnlySpan<long> damage)
    {
        var chances = new float[damage.Length];
        if (chances.Length == 0)
        {
            return chances;
        }

        long total = 0;
        foreach (var dealt in damage)
        {
            total += Math.Max(0, dealt);
        }

        for (var index = 0; index < chances.Length; index++)
        {
            chances[index] = total > 0 ? Math.Max(0, damage[index]) / (float)total : 1f / chances.Length;
        }

        Bound(chances);
        return chances;
    }

    // Raises every chance under the floor to it, taking the difference from the others by their chances, then lowers
    // every one over the cap to it, giving the difference to the others the same way (which keeps them over the floor),
    // each again until none passes.
    private static void Bound(float[] chances)
    {
        if (chances.Length * FloorPercent / 100f <= 1f)
        {
            Hold(chances, FloorPercent / 100f, below: true);
        }

        if (chances.Length * CapPercent / 100f >= 1f)
        {
            Hold(chances, CapPercent / 100f, below: false);
        }
    }

    private static void Hold(float[] chances, float bound, bool below)
    {
        var held = new bool[chances.Length];
        for (var round = 0; round < chances.Length; round++)
        {
            var moved = 0f;
            var open = 0f;
            var openCount = 0;
            for (var index = 0; index < chances.Length; index++)
            {
                if (!held[index] && (below ? chances[index] < bound : chances[index] > bound))
                {
                    moved += chances[index] - bound;
                    chances[index] = bound;
                    held[index] = true;
                }
                else if (!held[index])
                {
                    open += chances[index];
                    openCount++;
                }
            }

            if (moved == 0f || openCount == 0)
            {
                return;
            }

            for (var index = 0; index < chances.Length; index++)
            {
                if (!held[index])
                {
                    chances[index] += open > 0f ? moved * chances[index] / open : moved / openCount;
                }
            }
        }
    }

    // The index the roll lands on.
    public static int Draw(ReadOnlySpan<float> chances, ISimRng rng)
    {
        var roll = rng.NextUnit();
        var reached = 0f;
        for (var index = 0; index < chances.Length; index++)
        {
            reached += chances[index];
            if (roll < reached)
            {
                return index;
            }
        }

        return chances.Length - 1;
    }

    // The prize's catalog key for a boss of this level.
    public static string Prize(int bossLevel, ISimRng rng)
    {
        var stones = Tiers[0].Stones;
        foreach (var tier in Tiers)
        {
            if (bossLevel >= tier.FromLevel)
            {
                stones = tier.Stones;
            }
        }

        return AgingRules.KeyOf(stones[rng.NextExclusive(0, stones.Length)]);
    }
}
