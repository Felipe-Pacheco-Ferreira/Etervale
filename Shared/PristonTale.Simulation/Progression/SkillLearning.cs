using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Progression;

// Skill-master training. The class skill list is in skill-window order: each class tier (0..4) opens the next four
// skills, a skill needs one rank in the skill before it, rank r+1 needs level RequiredLevel + 2r, and the price grows
// with the window position. The first twelve skills spend skill points; the fourth and fifth tiers' eight share the
// elite skill points.
public static class SkillLearning
{
    public const int SkillsPerTier = 4;
    public const int EliteStart = 12;
    // The ranks a skill takes.
    public const int MaxRank = 10;

    private static readonly int[] BasePrices = [500, 1000, 1500, 2000, 3000, 5000, 7000, 9000, 10000, 12000, 15000, 20000, 25000, 30000, 35000, 40000,
        50000, 60000, 70000, 80000];
    private static readonly int[] RankPrices = [500, 600, 700, 800, 1000, 1200, 1400, 1600, 2000, 2500, 3000, 4000, 5000, 6000, 7000, 8000,
        10000, 12000, 14000, 16000];

    public static long Price(int index, int currentRank, int tax) => Social.Castle.CastleRules.WithTax(Price(index, currentRank), tax);

    public static int Price(int index, int currentRank)
    {
        if (index < 0 || index >= BasePrices.Length || currentRank is < 0 or >= MaxRank)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return BasePrices[index] + RankPrices[index] * currentRank;
    }

    // Skills a class tier has opened in the window (tier 0 = the first four).
    public static int OpenedBy(int tier) => SkillsPerTier * (Math.Clamp(tier, 0, PlayerCharacter.LastTier) + 1);

    public static bool UsesElitePoints(int index) => index >= EliteStart;

    // Whether the window skill at index can take its next rank, money aside. A definition without its ten ranks of costs
    // cannot be trained.
    public static bool CanTrain(IReadOnlyList<Skill> skills, int index, Func<uint, int> rankOf, int level, int tier, int skillPoints,
        int elitePoints) => Check(skills, index, rankOf, level, tier, skillPoints, elitePoints) == SkillTrainingCheck.Ready;

    // What keeps the window skill at index from its next rank, money aside, or Ready; the first that applies in this order.
    public static SkillTrainingCheck Check(IReadOnlyList<Skill> skills, int index, Func<uint, int> rankOf, int level, int tier, int skillPoints,
        int elitePoints)
    {
        if (index < 0 || index >= skills.Count || skills[index].Code == 0 || skills[index].ManaByRank.Count != MaxRank)
        {
            return SkillTrainingCheck.Unavailable;
        }

        if (index >= OpenedBy(tier))
        {
            return SkillTrainingCheck.TierLocked;
        }

        var skill = skills[index];
        var rank = rankOf(skill.Code);
        if (rank >= MaxRank)
        {
            return SkillTrainingCheck.MaxRank;
        }

        if (index > 0 && rankOf(skills[index - 1].Code) <= 0)
        {
            return SkillTrainingCheck.NeedsPrevious;
        }

        if (level < LevelFor(skill, rank))
        {
            return SkillTrainingCheck.NeedsLevel;
        }

        return (UsesElitePoints(index) ? elitePoints : skillPoints) > 0 ? SkillTrainingCheck.Ready : SkillTrainingCheck.NoPoints;
    }

    // The level the rank after currentRank needs.
    public static int LevelFor(Skill skill, int currentRank) => skill.RequiredLevel + currentRank * 2;

    public static bool TryQuote(PlayerCharacter character, uint code, out long price, out bool elite, int tax = 0)
    {
        price = 0;
        elite = false;
        var skills = character.Class.Skills;
        for (var index = 0; index < skills.Count; index++)
        {
            if (skills[index].Code != code)
            {
                continue;
            }

            if (!CanTrain(skills, index, character.Skills.RankOf, character.Level, character.Tier, character.SkillPoints, character.EliteSkillPoints))
            {
                return false;
            }

            price = Price(index, character.Skills.RankOf(code), tax);
            elite = UsesElitePoints(index);
            return character.Money >= price;
        }

        return false;
    }
}

public enum SkillTrainingCheck
{
    Ready,
    // No such skill in the window, or one without its ten ranks of costs.
    Unavailable,
    // The class tier has not opened it yet.
    TierLocked,
    MaxRank,
    // The skill before it in the window has no rank.
    NeedsPrevious,
    NeedsLevel,
    // No skill points (elite points for the fourth and fifth tiers).
    NoPoints
}
