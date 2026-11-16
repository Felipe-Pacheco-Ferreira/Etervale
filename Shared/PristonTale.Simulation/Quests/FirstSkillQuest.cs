using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Quests;

public static class FirstSkillQuest
{
    public const QuestCode Code = QuestCode.FirstSkill;
    public const int RequiredCasts = 3;
    public const int ReadyState = 2;
    private const int MinimumLevel = 10;
    private const int ActiveState = 1;
    private static readonly uint SmallManaPotion = ItemCodes.Parse("PM101");

    public static bool IsAvailable(PlayerCharacter character) =>
        character.Level >= MinimumLevel &&
        (character.Quests.ElementaryLog & (ElementaryQuestLog.AdventurerJourney | ElementaryQuestLog.FirstSkill)) == ElementaryQuestLog.AdventurerJourney;

    public static void CreditCast(PlayerCharacter character)
    {
        if (!IsAvailable(character))
        {
            return;
        }

        var journal = character.Quests;
        var active = journal.Active;
        if ((!active.IsIdle && active.Code != Code) || active.State == ReadyState)
        {
            return;
        }

        var casts = active.IsIdle ? 1 : Math.Min(active.Kills + 1, RequiredCasts);
        journal.Active = active with
        {
            Code = Code,
            State = casts == RequiredCasts ? ReadyState : ActiveState,
            Kills = casts,
            KillsNeeded = RequiredCasts
        };
    }

    public static bool Claim(QuestContext context, int state)
    {
        if (state != ReadyState || !IsAvailable(context.Character) ||
            !QuestSteps.Matches(context, Code, state) || context.Journal.Active.Kills < RequiredCasts)
        {
            return QuestSteps.Stale(context);
        }

        if (!QuestInventory.TryGrant(context, [new QuestGrant(SmallManaPotion, 15)]))
        {
            return QuestInventory.Refused(context);
        }

        context.Character.TryChangeMoney(1100);
        context.Journal.ElementaryLog |= ElementaryQuestLog.FirstSkill;
        QuestSteps.Finish(context, Code);
        return true;
    }
}
