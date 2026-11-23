using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Aging;

namespace PristonTale.Gameplay.UI;

// The touch HUD's bag, drawn by the page as the Android client's character window (touch-bag.js): the equipment around
// the figure, gold and weight, the 12 x 6 bag, the potion belt and the chosen item's sheet with its buttons. Opening the
// inventory (Bolsa, its key) while the touch HUD is in use asks the page for it instead of the canvas window.
public sealed partial class GameplayHud
{
    private const string TouchBagPrefix = "touch-bag:";

    private readonly List<HudItemAction> _touchItemActions = new(4);

    // Grows each time the inventory was asked for while the page draws the bag.
    private int _touchBagRequests;

    private void ToggleInventory()
    {
        if (TouchHudShown)
        {
            _touchBagRequests++;
            return;
        }

        TogglePanel(_inventorySlide);
    }

    // How many times the bag was asked for; with full, the bag itself and, for chosen (an item's id), its sheet. Reading
    // the full bag counts its items as seen, as opening the inventory does.
    internal string TouchBag(bool full, string chosen)
    {
        var state = _state;
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("requests", _touchBagRequests);
            if (full && state is not null)
            {
                _touchBagSeen = false;
                WriteTouchBag(json, state, Guid.TryParse(chosen, out var id) ? OwnedItemView.Find(state.Items, id) : null);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private void WriteTouchBag(Utf8JsonWriter json, OwnerState state, OwnedItemView chosen)
    {
        json.WriteNumber("money", state.Money);
        json.WriteNumber("weight", state.CarriedWeight);
        json.WriteNumber("maxWeight", state.MaxWeight);
        json.WriteBoolean("overweight", state.Overweight);
        json.WriteNumber("columns", Simulation.Items.Inventory.DefaultWidth);
        json.WriteNumber("rows", Simulation.Items.Inventory.DefaultHeight);
        json.WriteNumber("belt", PotionBelt.Boxes);
        json.WriteBoolean("busy", _inventory.Busy);
        json.WriteStartArray("items");
        foreach (var item in state.Items)
        {
            var bag = item.InBag && item.Anchor >= 0;
            if (!bag && !item.Equipped && item.BeltBox < 0)
            {
                continue;
            }

            json.WriteStartObject();
            json.WriteString("id", item.InstanceId);
            json.WriteString("name", item.Name);
            json.WriteString("picture", _icons.TryPath(item.CatalogKey, out var sprite) ? TouchPicturePath(sprite) : "");
            if (bag)
            {
                json.WriteNumber("column", item.Anchor % Simulation.Items.Inventory.DefaultWidth);
                json.WriteNumber("row", item.Anchor / Simulation.Items.Inventory.DefaultWidth);
                json.WriteNumber("width", item.Width);
                json.WriteNumber("height", item.Height);
            }
            else if (item.Equipped)
            {
                json.WriteString("slot", ((EquipmentSlot)item.Slot).ToString());
            }
            else
            {
                json.WriteNumber("box", item.BeltBox);
            }

            json.WriteNumber("count", item.Count);
            json.WriteNumber("upgrade", item.Details?.UpgradeLevel ?? 0);
            json.WriteBoolean("most", item.Details?.UpgradeLevel == AgingRules.MaxLevel);
            json.WriteString("tint", TouchItemTint(item));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        if (chosen is not null)
        {
            WriteTouchSheet(json, state, chosen);
        }
    }

    // The chosen item's sheet: its kind, rank, the tooltip's lines (but the name), its maturing and its buttons.
    private void WriteTouchSheet(Utf8JsonWriter json, OwnerState state, OwnedItemView item)
    {
        json.WriteStartObject("sheet");
        json.WriteString("id", item.InstanceId);
        var slot = ItemEquipmentMetadata.SlotOf(item.Kind);
        json.WriteString("kind", item.Kind == ItemKind.Potion ? "potion" : slot?.ToString() ?? "item");
        json.WriteString("rank", item.Details switch
        {
            { UniqueItem: not 0 } => "unique",
            { MixSheltom: > 0 } => "mixed",
            { UpgradeLevel: > 0 } => "upgraded",
            { AgingGoal: > 0 } => "aging",
            _ => ""
        });
        json.WriteBoolean("blocked", item.Details is { ClassAllowed: false });
        json.WriteStartArray("lines");
        var lines = HudItemDetailTooltip.Lines(item, state, null, null);
        for (var index = 1; index < lines.Count; index++)
        {
            if (lines[index].Text.Length > 0)
            {
                json.WriteStartObject();
                json.WriteString("text", lines[index].Text);
                json.WriteString("color", $"rgb({lines[index].Color.R},{lines[index].Color.G},{lines[index].Color.B})");
                json.WriteEndObject();
            }
        }

        json.WriteEndArray();
        if (HudItemDetailTooltip.Gauge(item, state) is { Goal: > 0 } aging)
        {
            json.WriteStartObject("maturity");
            json.WriteNumber("level", aging.Level);
            json.WriteNumber("count", aging.Count);
            json.WriteNumber("goal", aging.Goal);
            json.WriteEndObject();
        }

        HudItemActions.Offered(item, state.Items, _touchItemActions);
        json.WriteStartArray("actions");
        foreach (var action in _touchItemActions)
        {
            json.WriteStringValue(action.ToString());
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    // An item's well colour on the page, as the Android client's: blocked (the class cannot use it), unique, enhanced
    // (upgraded, maturing or mixed) or none, also while its details have not arrived.
    private static string TouchItemTint(OwnedItemView item) => item.Details switch
    {
        { ClassAllowed: false } => "blocked",
        { UniqueItem: not 0 } => "unique",
        { UpgradeLevel: > 0 } or { AgingGoal: > 0 } or { MixSheltom: > 0 } => "enhanced",
        _ => ""
    };

    // touch-bag:<action>:<item id>[:<belt box>], an item sheet's button.
    private bool RunTouchBag(string action)
    {
        if (!action.StartsWith(TouchBagPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = action[TouchBagPrefix.Length..].Split(':');
        if (_state is { } state && parts.Length >= 2 && Enum.TryParse<HudItemAction>(parts[0], out var command) && Guid.TryParse(parts[1], out var id))
        {
            var box = parts.Length > 2 && int.TryParse(parts[2], out var parsed) ? parsed : -1;
            _inventory.Run(OwnedItemView.Find(state.Items, id), command, box, _touchItemActions);
        }

        return true;
    }
}
