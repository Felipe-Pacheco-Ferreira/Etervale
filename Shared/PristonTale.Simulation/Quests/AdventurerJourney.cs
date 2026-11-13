using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Quests;

internal static class AdventurerJourney
{
    private const QuestCode Code = AdventurerJourneyRoute.Code;
    private static readonly uint HopyCrystal = AdventurerJourneyRoute.Crystal;
    private static readonly uint SmallLifePotion = ItemCodes.Parse("PL101");
    private const int TutorialPotionGranted = 1;

    internal static bool IsAvailable(PlayerCharacter character) =>
        (character.Quests.ElementaryLog & ElementaryQuestLog.AdventurerJourney) == 0;

    public static bool Talk(QuestContext context)
    {
        if (!IsGuide(context) || IsFinished(context) || QuestSteps.Blocked(context, Code))
        {
            return false;
        }

        var state = context.Journal.Active.State;
        // The guide asks for the kit in hand: one who lacks a piece of it is given that piece again.
        if (state == 1)
        {
            GiveStarterKit(context);
        }

        if (state == 1 && (context.Journal.Active.Flags & TutorialPotionGranted) == 0 && GiveTutorialPotion(context))
        {
            context.Journal.Active = context.Journal.Active with { Flags = TutorialPotionGranted };
        }
        var ready = CanAdvance(context, state);
        var document = state switch
        {
            0 => "avalon_journey_start",
            1 when !ready => "avalon_journey_bag",
            1 => context.Tempskron ? "avalon_journey_hunt_skadi" : "avalon_journey_hunt_jin",
            2 when !ready => "avalon_journey_collect",
            _ => "avalon_journey_finish"
        };
        context.Show(ready ? QuestSteps.Proceed(Code, state, document) : QuestSteps.Close(Code, state, document));
        return true;
    }

    public static bool Proceed(QuestContext context, int state)
    {
        if (!IsGuide(context) || IsFinished(context) || !QuestSteps.Matches(context, Code, state) || !CanAdvance(context, state))
        {
            return QuestSteps.Stale(context);
        }

        if (state == 2)
        {
            QuestInventory.Remove(context.Character, HopyCrystal, 1);
            context.Experience = LevelOneExperience(20);
            context.Character.TryChangeMoney(300);
            context.Journal.ElementaryLog |= ElementaryQuestLog.AdventurerJourney;
            QuestSteps.Finish(context, Code);
        }
        else
        {
            if (state == 0 ? !GiveTutorialPotion(context) || !GiveStarterKit(context) : !GrantPotions(context, 15))
            {
                return QuestInventory.Refused(context);
            }

            context.Experience = LevelOneExperience(state == 0 ? 15UL : 30UL);
            if (state == 1)
            {
                context.Character.TryChangeMoney(500);
            }

            context.Journal.Active = new ActiveQuest
            {
                Code = Code,
                State = state + 1,
                Target = state == 1 ? context.Tempskron ? QuestMonsters.Hopy : QuestMonsters.Rabie : 0,
                KillsNeeded = state == 1 ? 3 : 0,
                Variant = state == 1 ? QuestInventory.Count(context.Character, HopyCrystal) : 0,
                Flags = TutorialPotionGranted
            };
        }

        return true;
    }

    private static bool IsGuide(QuestContext context) =>
        context.Npc.Name == AdventurerJourneyRoute.For(context.Tempskron).GuideName;

    private static bool IsFinished(QuestContext context) => !IsAvailable(context.Character);

    private static bool GrantPotions(QuestContext context, int count) =>
        QuestInventory.TryGrant(context, [new QuestGrant(SmallLifePotion, Count: count)]);

    private static bool GiveTutorialPotion(QuestContext context)
    {
        if (!context.Items.TryCreate(SmallLifePotion, 1, 0, out var potion))
        {
            return false;
        }

        context.GrantRefusal = QuestInventory.Check(context.Character, [potion]);
        return context.GrantRefusal == QuestGrantCheck.Fits && context.Character.Inventory.TryPlaceFirstFree(potion);
    }

    // Places in the bag the pieces of the class's starter kit (StarterGifts: weapon, off-hand, armour) whose slot the
    // character has nothing for, worn or carried; true when they all fit (or none was missing).
    private static bool GiveStarterKit(QuestContext context)
    {
        var character = context.Character;
        var playerClass = QuestClasses.ClassOf(character);
        var kit = new List<ItemInstance>();
        foreach (var index in StarterKit)
        {
            if (context.Items.StarterGift(playerClass, index) is { } piece && ItemEquipmentMetadata.SlotOf(piece.Definition.Kind) is { } slot
                && character.GetEquipped(slot) is null
                && !character.Inventory.Items().Any(item => ItemEquipmentMetadata.SlotOf(item.Definition.Kind) == slot))
            {
                kit.Add(piece);
            }
        }

        if (kit.Count == 0)
        {
            return true;
        }

        context.GrantRefusal = QuestInventory.Check(character, kit);
        return context.GrantRefusal == QuestGrantCheck.Fits && kit.All(character.Inventory.TryPlaceFirstFree);
    }

    private static readonly int[] StarterKit = [StarterGifts.Weapon, StarterGifts.OffHand, StarterGifts.Armour];

    private static ulong LevelOneExperience(ulong percent) =>
        (ExperienceTable.ForLevel(2) - ExperienceTable.ForLevel(1)) * percent / 100;

    private static bool CanAdvance(QuestContext context, int state) => state switch
    {
        0 => true,
        1 => context.Character.GetEquipped(EquipmentSlot.Weapon) is not null
            && Enumerable.Range(0, PotionBelt.Boxes).Any(box => context.Character.Belt[box] is not null),
        2 => context.Journal.Active.HuntComplete
            && QuestInventory.Count(context.Character, HopyCrystal) > context.Journal.Active.Variant,
        _ => false
    };
}
