using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.UI;

// What the touch HUD's tutorials (touch-tutorial.js) ask of the game to know when a lesson starts and when its steps are
// done, as the Android client's lessons read the session: alive, idle (no automatic quest running), the starter piece
// still to wear, the bag potion and the empty belt, a hunt not begun, a target that can be fought, the attribute points
// to spend and whether the player is attacking.
public sealed partial class GameplayHud
{
    // The monster the player attacks, or none (GameplayScreen sets it with the target frame's target).
    internal bool TouchAttacking { get; set; }

    internal string TouchTutorial()
    {
        var state = _state;
        var alive = state is { Life: > 0 };
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteBoolean("alive", alive);
            json.WriteBoolean("idle", alive && !AutoHuntEnabled && !QuestGuideEnabled);
            json.WriteString("piece", state is not null && TouchStarterPiece(state.Items) is { } piece ? piece.InstanceId.ToString() : "");
            json.WriteString("potion", state is not null && TouchBagPotion(state.Items) is { } potion ? potion.InstanceId.ToString() : "");
            json.WriteBoolean("beltEmpty", state is not null && TouchBeltEmpty(state.Items));
            json.WriteBoolean("hunting", _questView is { KillsNeeded: > 0, Kills: 0 });
            json.WriteNumber("kills", _questView?.Kills ?? 0);
            json.WriteBoolean("hostile", Combat.TryGetTargetState(TouchTargetId, out _, out _, out _, out var maxLife) && maxLife > 0);
            json.WriteNumber("statPoints", state?.StatPoints ?? 0);
            json.WriteBoolean("attacking", TouchAttacking);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // A piece of the starter kit (weapon, off-hand or armour) still in the bag while its slot is empty.
    internal static OwnedItemView TouchStarterPiece(IReadOnlyList<OwnedItemView> items)
    {
        foreach (var item in items)
        {
            if (item.InBag && ItemEquipmentMetadata.SlotOf(item.Kind) is EquipmentSlot.Weapon or EquipmentSlot.OffHand or EquipmentSlot.Armor
                && OwnedItemView.InSlot(items, (int)ItemEquipmentMetadata.SlotOf(item.Kind)!.Value) is null)
            {
                return item;
            }
        }

        return null;
    }

    internal static OwnedItemView TouchBagPotion(IReadOnlyList<OwnedItemView> items)
    {
        foreach (var item in items)
        {
            if (item.InBag && item.Kind == ItemKind.Potion)
            {
                return item;
            }
        }

        return null;
    }

    internal static bool TouchBeltEmpty(IReadOnlyList<OwnedItemView> items)
    {
        for (var box = 0; box < PotionBelt.Boxes; box++)
        {
            if (OwnedItemView.InBox(items, box) is not null)
            {
                return false;
            }
        }

        return true;
    }
}
