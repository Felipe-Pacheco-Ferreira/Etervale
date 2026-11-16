using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Quests;

public static class ClassSkillQuests
{
    public const int ReadyState = 2;
    private const int ActiveState = 1;
    public const string Document = "Avalon/ClassSkill.msg";

    public static QuestBoardEntry BoardEntry(SkillQuestDefinition definition) =>
        new(ClassSkillCatalog.TryGet(definition.SkillCode, out var skill) ? skill.Name : "Habilidade de classe", Document);

    public static void Talk(QuestContext context)
    {
        if (context.Prompt is null && context.Npc.HomeMaster && context.Journal.Active.IsIdle &&
            SkillQuestCatalog.Next(context.Character) is { } definition)
        {
            context.Show(QuestSteps.Confirm(definition.Code, 0, Document, QuestTexts.GetQuest));
        }
    }

    public static bool Accept(QuestContext context, QuestCode code, int state)
    {
        if (state != 0 || !context.Npc.HomeMaster || !context.Journal.Active.IsIdle ||
            SkillQuestCatalog.Next(context.Character) is not { } definition || definition.Code != code)
        {
            return QuestSteps.Stale(context);
        }

        context.Journal.Active = new ActiveQuest { Code = code, State = ActiveState, KillsNeeded = definition.Required };
        return true;
    }

    public static void Credit(PlayerCharacter character, uint skillCode, SkillQuestObjective objective, int count = 1)
    {
        var active = character.Quests.Active;
        if (active.State != ActiveState || !SkillQuestCatalog.TryGet(active.Code, out var definition) ||
            definition.SkillCode != skillCode || definition.Objective != objective || definition.Class != QuestClasses.ClassOf(character))
        {
            return;
        }

        var progress = Math.Min(active.Kills + count, definition.Required);
        if (progress > active.Kills)
        {
            character.Quests.Active = active with { Kills = progress, State = progress == definition.Required ? ReadyState : ActiveState };
        }
    }

    public static bool Claim(QuestContext context, QuestCode code, int state)
    {
        if (state != ReadyState || !QuestSteps.Matches(context, code, state) ||
            !SkillQuestCatalog.TryGet(code, out var definition) || definition.Class != QuestClasses.ClassOf(context.Character) ||
            context.Journal.Active.Kills < definition.Required || context.Journal.HasFinished(code))
        {
            return QuestSteps.Stale(context);
        }

        var level = definition.MinimumLevel;
        var experience = (ExperienceTable.ForLevel(level + 1) - ExperienceTable.ForLevel(level))
            * (ulong)definition.ExperienceTenthsPercent / 1000;
        context.Experience += experience;
        QuestSteps.Finish(context, code);
        return true;
    }
}
