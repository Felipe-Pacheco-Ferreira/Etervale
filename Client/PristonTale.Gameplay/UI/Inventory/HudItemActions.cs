using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.UI.Inventory;

public enum HudItemAction
{
    Use,
    Drink,
    BeltPut,
    BeltTake,
    Equip,
    Unequip
}

// What can be done with an owned item from its sheet (the touch HUD's bag, as the Android client's item sheet): a belt
// potion goes back to the bag; a bag potion is drunk or put on a belt box; a usable item is used; gear is worn or taken
// off. The server still validates each command.
public static class HudItemActions
{
    public static void Offered(OwnedItemView item, IReadOnlyList<OwnedItemView> items, List<HudItemAction> actions)
    {
        actions.Clear();
        if (item.BeltBox >= 0)
        {
            actions.Add(HudItemAction.BeltTake);
            return;
        }

        if (item.InBag && item.Kind == ItemKind.Potion)
        {
            actions.Add(HudItemAction.Drink);
            actions.Add(HudItemAction.BeltPut);
            return;
        }

        if (item.InBag && UsableItemCatalog.IsUsable(item.CatalogKey))
        {
            actions.Add(HudItemAction.Use);
        }

        if (item.Equipped)
        {
            actions.Add(HudItemAction.Unequip);
        }
        else if (item.InBag && SlotFor(item.Kind, items).HasValue)
        {
            actions.Add(HudItemAction.Equip);
        }
    }

    // The slot a bag item of this kind is worn in: its kind's, the free one of a pair.
    public static EquipmentSlot? SlotFor(ItemKind kind, IReadOnlyList<OwnedItemView> items) =>
        ItemEquipmentMetadata.DefaultSlot(kind, ItemEquipmentMetadata.SlotOf(kind), slot => OwnedItemView.InSlot(items, (int)slot) is not null);
}
