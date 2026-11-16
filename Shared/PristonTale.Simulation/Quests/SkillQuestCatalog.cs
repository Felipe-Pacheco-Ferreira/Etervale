using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Quests;

public enum SkillQuestObjective
{
    MaintainEffect,
    SummonKills,
    MarkedKills,
    SkillKills,
    UndeadHits,
    Hits,
    ApplyEffect,
    ConvertUndead,
    Casts
}

public sealed record SkillQuestDefinition(
    PlayerClass Class, uint SkillCode, int MinimumLevel, SkillQuestObjective Objective, int Required, int ExperienceTenthsPercent)
{
    // One stable block per class, with sixteen slots for the first four skill tiers.
    public QuestCode Code => (QuestCode)(0x100 + ((int)Class - 1) * 16
        + System.Numerics.BitOperations.TrailingZeroCount((SkillCode >> 16) & 0xff) * 4 + (int)(SkillCode & 0xff) - 1);
}

public static class SkillQuestCatalog
{
    private const int LevelWindow = 20;

    // The published mission order is preserved within each class.
    private static readonly SkillQuestDefinition[] Definitions =
    [
        new(PlayerClass.Mechanician, 0x01010001, 10, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Archer, 0x04010001, 10, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Pikeman, 0x03010001, 10, SkillQuestObjective.Hits, 20, 100),
        new(PlayerClass.Atalanta, 0x06010001, 10, SkillQuestObjective.ApplyEffect, 20, 100),
        new(PlayerClass.Knight, 0x05010001, 10, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Magician, 0x08010001, 10, SkillQuestObjective.Casts, 5, 100),
        new(PlayerClass.Priestess, 0x07010001, 10, SkillQuestObjective.Casts, 5, 100),
        new(PlayerClass.Mechanician, 0x01010002, 12, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Atalanta, 0x06010002, 12, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Knight, 0x05010002, 12, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Magician, 0x08010002, 12, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Priestess, 0x07010002, 12, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Fighter, 0x02010003, 14, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Archer, 0x04010003, 14, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Pikeman, 0x03010003, 14, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Magician, 0x08010003, 14, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Priestess, 0x07010003, 14, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Fighter, 0x02010004, 17, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Mechanician, 0x01010003, 17, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Archer, 0x04010004, 17, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Pikeman, 0x03010004, 17, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Atalanta, 0x06010004, 17, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Knight, 0x05010004, 17, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Magician, 0x08010004, 17, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Priestess, 0x07010004, 17, SkillQuestObjective.MarkedKills, 10, 100),
        new(PlayerClass.Fighter, 0x02020001, 20, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Mechanician, 0x01020001, 20, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Pikeman, 0x03020001, 20, SkillQuestObjective.Hits, 20, 100),
        new(PlayerClass.Atalanta, 0x06020001, 20, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Knight, 0x05020001, 20, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Fighter, 0x02020002, 23, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Mechanician, 0x01020002, 23, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Archer, 0x04020002, 23, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Pikeman, 0x03020002, 23, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Atalanta, 0x06020002, 23, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Knight, 0x05020002, 23, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Magician, 0x08020002, 23, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Priestess, 0x07020002, 23, SkillQuestObjective.Hits, 20, 100),
        new(PlayerClass.Fighter, 0x02020003, 26, SkillQuestObjective.ApplyEffect, 20, 100),
        new(PlayerClass.Mechanician, 0x01020003, 26, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Archer, 0x04020003, 26, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Atalanta, 0x06020003, 26, SkillQuestObjective.Hits, 20, 100),
        new(PlayerClass.Knight, 0x05020003, 26, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Magician, 0x08020003, 26, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Priestess, 0x07020003, 26, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Fighter, 0x02020004, 30, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Mechanician, 0x01020004, 30, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Archer, 0x04020004, 30, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Pikeman, 0x03020004, 30, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Atalanta, 0x06020004, 30, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Knight, 0x05020004, 30, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Magician, 0x08020004, 30, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Priestess, 0x07020004, 30, SkillQuestObjective.Casts, 5, 100),
        new(PlayerClass.Fighter, 0x02040001, 40, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Mechanician, 0x01040001, 40, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Archer, 0x04040001, 40, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Pikeman, 0x03040001, 40, SkillQuestObjective.Hits, 20, 100),
        new(PlayerClass.Atalanta, 0x06040001, 40, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Magician, 0x08040001, 40, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Priestess, 0x07040001, 40, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Fighter, 0x02040002, 43, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Mechanician, 0x01040002, 43, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Archer, 0x04040002, 43, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Pikeman, 0x03040002, 43, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Atalanta, 0x06040002, 43, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Knight, 0x05040002, 43, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Magician, 0x08040002, 43, SkillQuestObjective.Hits, 20, 100),
        new(PlayerClass.Priestess, 0x07040002, 43, SkillQuestObjective.Casts, 5, 100),
        new(PlayerClass.Fighter, 0x02040003, 46, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Archer, 0x04040003, 46, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Atalanta, 0x06040003, 46, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Knight, 0x05040003, 46, SkillQuestObjective.ConvertUndead, 5, 100),
        new(PlayerClass.Magician, 0x08040003, 46, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Priestess, 0x07040003, 46, SkillQuestObjective.UndeadHits, 20, 100),
        new(PlayerClass.Fighter, 0x02040004, 50, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Mechanician, 0x01040004, 50, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Archer, 0x04040004, 50, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Pikeman, 0x03040004, 50, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Atalanta, 0x06040004, 50, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Knight, 0x05040004, 50, SkillQuestObjective.SkillKills, 10, 100),
        new(PlayerClass.Magician, 0x08040004, 50, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Priestess, 0x07040004, 50, SkillQuestObjective.MaintainEffect, 60, 100),
        new(PlayerClass.Fighter, 0x02080001, 60, SkillQuestObjective.SkillKills, 10, 47),
        new(PlayerClass.Mechanician, 0x01080001, 60, SkillQuestObjective.SkillKills, 10, 47),
        new(PlayerClass.Archer, 0x04080001, 60, SkillQuestObjective.SummonKills, 10, 47),
        new(PlayerClass.Pikeman, 0x03080001, 60, SkillQuestObjective.MarkedKills, 10, 47),
        new(PlayerClass.Atalanta, 0x06080001, 60, SkillQuestObjective.MaintainEffect, 60, 47),
        new(PlayerClass.Knight, 0x05080001, 60, SkillQuestObjective.SkillKills, 10, 47),
        new(PlayerClass.Magician, 0x08080001, 60, SkillQuestObjective.SummonKills, 10, 47),
        new(PlayerClass.Priestess, 0x07080001, 60, SkillQuestObjective.SkillKills, 10, 47),
        new(PlayerClass.Fighter, 0x02080002, 63, SkillQuestObjective.MaintainEffect, 60, 46),
        new(PlayerClass.Mechanician, 0x01080002, 63, SkillQuestObjective.MaintainEffect, 60, 46),
        new(PlayerClass.Pikeman, 0x03080002, 63, SkillQuestObjective.SkillKills, 10, 46),
        new(PlayerClass.Atalanta, 0x06080002, 63, SkillQuestObjective.SkillKills, 10, 46),
        new(PlayerClass.Knight, 0x05080002, 63, SkillQuestObjective.MaintainEffect, 60, 46),
        new(PlayerClass.Magician, 0x08080002, 63, SkillQuestObjective.SkillKills, 10, 46),
        new(PlayerClass.Priestess, 0x07080002, 63, SkillQuestObjective.MaintainEffect, 60, 46),
        new(PlayerClass.Fighter, 0x02080003, 66, SkillQuestObjective.SkillKills, 10, 45),
        new(PlayerClass.Mechanician, 0x01080003, 66, SkillQuestObjective.MaintainEffect, 60, 45),
        new(PlayerClass.Archer, 0x04080003, 66, SkillQuestObjective.SkillKills, 10, 45),
        new(PlayerClass.Pikeman, 0x03080003, 66, SkillQuestObjective.MaintainEffect, 60, 45),
        new(PlayerClass.Atalanta, 0x06080003, 66, SkillQuestObjective.MaintainEffect, 60, 45),
        new(PlayerClass.Knight, 0x05080003, 66, SkillQuestObjective.MaintainEffect, 60, 45),
        new(PlayerClass.Magician, 0x08080003, 66, SkillQuestObjective.ApplyEffect, 20, 45),
        new(PlayerClass.Priestess, 0x07080003, 66, SkillQuestObjective.SkillKills, 10, 45),
        new(PlayerClass.Mechanician, 0x01080004, 70, SkillQuestObjective.SummonKills, 10, 44),
        new(PlayerClass.Archer, 0x04080004, 70, SkillQuestObjective.MaintainEffect, 60, 44),
        new(PlayerClass.Pikeman, 0x03080004, 70, SkillQuestObjective.SkillKills, 10, 44),
        new(PlayerClass.Atalanta, 0x06080004, 70, SkillQuestObjective.SkillKills, 10, 44),
        new(PlayerClass.Knight, 0x05080004, 70, SkillQuestObjective.SkillKills, 10, 44),
        new(PlayerClass.Magician, 0x08080004, 70, SkillQuestObjective.SkillKills, 10, 44),
        new(PlayerClass.Priestess, 0x07080004, 70, SkillQuestObjective.MaintainEffect, 60, 44),
    ];

    private static readonly IReadOnlyDictionary<QuestCode, SkillQuestDefinition> ByCode =
        Definitions.ToDictionary(static definition => definition.Code);

    public static bool TryGet(QuestCode code, out SkillQuestDefinition definition) => ByCode.TryGetValue(code, out definition);

    public static IEnumerable<SkillQuestDefinition> For(PlayerClass playerClass) =>
        Definitions.Where(definition => definition.Class == playerClass);

    public static SkillQuestDefinition Next(PlayerCharacter character) =>
        Eligible(character).FirstOrDefault(definition => !character.Quests.HasFinished(definition.Code));

    public static IEnumerable<SkillQuestDefinition> Eligible(PlayerCharacter character)
    {
        var skills = character.Class.Skills;
        var opened = Math.Min(skills.Count, SkillLearning.OpenedBy(character.Tier));
        foreach (var definition in For(QuestClasses.ClassOf(character)))
        {
            if (character.Level < definition.MinimumLevel || character.Level > definition.MinimumLevel + LevelWindow)
            {
                continue;
            }

            for (var index = 0; index < opened; index++)
            {
                if (skills[index].Code == definition.SkillCode && character.Level >= skills[index].RequiredLevel)
                {
                    yield return definition;
                    break;
                }
            }
        }
    }
}
