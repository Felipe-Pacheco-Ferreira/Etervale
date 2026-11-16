using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Mobile;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.Items.Usable;

// The character window, a HUD window (VulkanPanel): on the left the equipment slots around the faint figure of the
// character with gold and weight under them; in the middle the 12 x 6 bag as a field of item wells with each item's own
// sprite at its footprint and the potion belt under it; on the right, once an item is chosen, its sheet with what can be
// done with it. Wells take the item's tint (unique, enhanced, not for this class). The server validates changes and
// the window redraws from the authoritative inventory.
internal sealed class VulkanInventoryPanel
{
    private static readonly Color Unique = Color.Rgb(255, 164, 64);
    private static readonly Color Enhanced = Color.Rgb(120, 172, 255);
    private static readonly Color AgingBlue = Color.Rgb(HudItemDetailTooltip.AgingColor.R, HudItemDetailTooltip.AgingColor.G, HudItemDetailTooltip.AgingColor.B);
    private const int SlotSize = 44;
    private const int SlotPitch = 53;
    private const int FigureMargin = 6;

    // The equipment slots around the figure: weapon, ring, gloves and bracelet down the left; shield, ring, boots and
    // sheltom down the right; the amulet at the neck and the armour on the chest. Row may fall between rows.
    private static readonly (EquipmentSlot Slot, string Label, int Column, float Row)[] Doll =
    [
        (EquipmentSlot.Weapon, "Arma", 0, 0), (EquipmentSlot.RingLeft, "Anel", 0, 1), (EquipmentSlot.Gloves, "Luvas", 0, 2),
        (EquipmentSlot.Bracelet, "Bracelete", 0, 3), (EquipmentSlot.OffHand, "Escudo", 2, 0), (EquipmentSlot.RingRight, "Anel", 2, 1),
        (EquipmentSlot.Boots, "Botas", 2, 2), (EquipmentSlot.Sheltom, "Sheltom", 2, 3), (EquipmentSlot.Amulet, "Amuleto", 1, .85f),
        (EquipmentSlot.Armor, "Armadura", 1, 1.85f)
    ];

    private readonly Activity _activity;
    private readonly FrameLayout _overlay;
    private readonly VulkanWorldSession _session;
    private readonly VulkanItemIcons _icons;
    private VulkanPanel _panel;
    private Dialog _crystalConfirmation;
    private FrameLayout _equipment;
    private FrameLayout _bag;
    private LinearLayout _belt;
    private LinearLayout _sheet;
    private TextView _gold;
    private TextView _bagNote;
    private VulkanGaugeView _weight;
    private VulkanGaugeView _maturity;
    private OwnerState _owner;
    private Guid _selected;
    private bool _pending;
    private long _expectedRevision;
    private int _bagWidth;
    private int _bagHeight;
    // The bag's item tiles and the chosen item's action buttons as drawn last, for the tutorial to point at.
    private readonly Dictionary<Guid, View> _tiles = [];
    private readonly List<Button> _actions = [];

    public VulkanInventoryPanel(Activity activity, FrameLayout overlay, VulkanWorldSession session, VulkanItemIcons icons)
    {
        _activity = activity;
        _overlay = overlay;
        _session = session;
        _icons = icons;
        _overlay.ViewDetachedFromWindow += (_, _) => DismissCrystalConfirmation();
        _icons.Loaded += () =>
        {
            if (_panel is not null)
            {
                Render();
            }
        };
    }

    public void Toggle()
    {
        if (_panel is not null)
        {
            _panel.Close();
            return;
        }

        _owner = _session.Owner;
        Open();
        Render();
    }

    public bool IsOpen => _panel is not null;

    public Guid SelectedItem => _selected;

    // The tile of an item in the bag, or null while the window is closed or the item is elsewhere.
    public View TileOf(Guid instanceId) => _panel is not null && _tiles.TryGetValue(instanceId, out var tile) ? tile : null;

    // The first action button of the chosen item whose label starts with prefix, or null.
    public View ActionStartingWith(string prefix) => _panel is null ? null : _actions.FirstOrDefault(button => button.Text?.StartsWith(prefix, StringComparison.Ordinal) == true);

    public void Update(OwnerState state)
    {
        var changed = _owner?.InventoryRevision != state.InventoryRevision || !ReferenceEquals(_owner?.Items, state.Items)
            || _owner?.Money != state.Money || _owner?.CarriedWeight != state.CarriedWeight
            || _owner?.MaxWeight != state.MaxWeight || _owner?.Overweight != state.Overweight;
        var agingChanged = !ReferenceEquals(_owner?.Aging, state.Aging);
        _owner = state;
        if (_panel is null)
        {
            return;
        }

        if (changed)
        {
            Render();
        }
        else if (agingChanged && _maturity is not null)
        {
            UpdateMaturity(OwnedItemView.Find(state.Items, _selected));
        }
    }

    private void Open()
    {
        _panel = new VulkanPanel(_activity, _overlay, "Inventário", 820, 392);
        _panel.Closed += () =>
        {
            DismissCrystalConfirmation();
            _panel = null;
            _maturity = null;
            _selected = Guid.Empty;
            _tiles.Clear();
            _actions.Clear();
        };
        var body = new LinearLayout(_activity);
        _panel.Content.AddView(body, new LinearLayout.LayoutParams(-1, -1));

        var left = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        left.AddView(VulkanPanelArt.Section(_activity, "Equipamento"));
        _equipment = new FrameLayout(_activity) { Background = new VulkanPanelArt.FigureDrawable(Dp(10), Dp(1)) };
        var figureWidth = Dp(FigureMargin * 2 + SlotPitch * 2 + SlotSize);
        left.AddView(_equipment, new LinearLayout.LayoutParams(figureWidth, Dp(FigureMargin * 2 + SlotPitch * 3 + SlotSize)) { TopMargin = Dp(5) });
        var purse = new LinearLayout(_activity);
        purse.SetGravity(GravityFlags.CenterVertical);
        var coin = new View(_activity) { Background = new VulkanPanelArt.CoinDrawable(Dp(16)) };
        purse.AddView(coin, new LinearLayout.LayoutParams(Dp(16), Dp(16)) { RightMargin = Dp(6) });
        purse.AddView(VulkanPanel.Body(_activity, "Ouro", 13, VulkanPanel.Muted), new LinearLayout.LayoutParams(0, -2, 1));
        _gold = VulkanPanel.Text(_activity, "", 15, VulkanHudArt.EdgeLight, serif: true);
        purse.AddView(_gold);
        left.AddView(purse, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });
        _weight = new VulkanGaugeView(_activity);
        left.AddView(_weight, new LinearLayout.LayoutParams(-1, Dp(16)) { TopMargin = Dp(6) });
        body.AddView(left, new LinearLayout.LayoutParams(figureWidth, -1) { RightMargin = Dp(14) });

        var middle = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        _bagNote = VulkanPanel.Body(_activity, "", 12, VulkanPanel.Muted);
        middle.AddView(VulkanPanelArt.Section(_activity, "Bolsa", _bagNote));
        _bag = new FrameLayout(_activity);
        // The bag's cells take the room the layout gives it, which changes when the sheet opens or closes.
        _bag.LayoutChange += (_, _) =>
        {
            if (_bag.Width != _bagWidth || _bag.Height != _bagHeight)
            {
                _bag.Post(Render);
            }
        };
        middle.AddView(_bag, new LinearLayout.LayoutParams(-1, 0, 1) { TopMargin = Dp(5) });
        var beltRow = new LinearLayout(_activity);
        beltRow.SetGravity(GravityFlags.CenterVertical);
        beltRow.AddView(VulkanPanel.Heading(_activity, "Cinto"), new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(10) });
        _belt = new LinearLayout(_activity);
        beltRow.AddView(_belt);
        middle.AddView(beltRow, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        body.AddView(middle, new LinearLayout.LayoutParams(0, -1, 1));

        _sheet = new LinearLayout(_activity) { Orientation = Orientation.Vertical, Clickable = true, Visibility = ViewStates.Gone };
        _sheet.SetPadding(Dp(10), Dp(10), Dp(10), Dp(10));
        _sheet.Background = VulkanPanelArt.Inset(_activity, lit: true);
        body.AddView(_sheet, new LinearLayout.LayoutParams(Dp(212), -1) { LeftMargin = Dp(12) });
    }

    private void Render()
    {
        if (_panel is null)
        {
            return;
        }

        var items = _owner?.Items ?? [];
        _gold.Text = _owner is null ? "—" : $"{_owner.Money:N0}";
        if (_owner is { MaxWeight: > 0 } owner)
        {
            _weight.Set(owner.Overweight ? VulkanPanelArt.Overweight : VulkanHudArt.Stamina, owner.CarriedWeight / (float)owner.MaxWeight,
                $"Peso {owner.CarriedWeight}/{owner.MaxWeight}");
        }
        else
        {
            _weight.Set(VulkanHudArt.Stamina, 0f, "Peso");
        }

        RenderEquipment(items);
        RenderBag(items);
        RenderBelt(items);
        RenderSheet(OwnedItemView.Find(items, _selected));
    }

    private void RenderEquipment(IReadOnlyList<OwnedItemView> items)
    {
        _equipment.RemoveAllViews();
        foreach (var (slot, label, column, row) in Doll)
        {
            var item = OwnedItemView.InSlot(items, (int)slot);
            _equipment.AddView(Slot(item, label), new FrameLayout.LayoutParams(Dp(SlotSize), Dp(SlotSize))
            {
                LeftMargin = Dp(FigureMargin + column * SlotPitch),
                TopMargin = (int)MathF.Round((FigureMargin + row * SlotPitch) * Density)
            });
        }
    }

    private void RenderBag(IReadOnlyList<OwnedItemView> items)
    {
        _bag.RemoveAllViews();
        _tiles.Clear();
        _bagWidth = _bag.Width;
        _bagHeight = _bag.Height;
        var used = 0;
        foreach (var item in items)
        {
            if (item.InBag && item.Anchor >= 0)
            {
                used += item.Width * item.Height;
            }
        }

        _bagNote.Text = _owner is { MaxWeight: > 0 } owner
            ? $"Peso {owner.CarriedWeight} / {owner.MaxWeight}  ·  {used}/{Inventory.DefaultWidth * Inventory.DefaultHeight}"
            : "Peso — / —";
        if (_bagWidth == 0 || _bagHeight == 0)
        {
            return;
        }

        var cell = Math.Min(_bagWidth / Inventory.DefaultWidth, _bagHeight / Inventory.DefaultHeight);
        var grid = new View(_activity) { Background = new VulkanPanelArt.GridDrawable(cell, Inventory.DefaultWidth, Inventory.DefaultHeight, Dp(2), Dp(1)) };
        _bag.AddView(grid, new FrameLayout.LayoutParams(cell * Inventory.DefaultWidth, cell * Inventory.DefaultHeight));
        foreach (var item in items)
        {
            if (!item.InBag || item.Anchor < 0)
            {
                continue;
            }

            var tile = Tile(item, Dp(3));
            _tiles[item.InstanceId] = tile;
            _bag.AddView(tile, new FrameLayout.LayoutParams(cell * item.Width - Dp(2), cell * item.Height - Dp(2))
            {
                LeftMargin = item.Anchor % Inventory.DefaultWidth * cell + Dp(1),
                TopMargin = item.Anchor / Inventory.DefaultWidth * cell + Dp(1)
            });
        }
    }

    private void RenderBelt(IReadOnlyList<OwnedItemView> items)
    {
        _belt.RemoveAllViews();
        for (var box = 0; box < PotionBelt.Boxes; box++)
        {
            var item = OwnedItemView.InBox(items, box);
            _belt.AddView(Slot(item, $"{box + 1}"), new LinearLayout.LayoutParams(Dp(42), Dp(42)) { RightMargin = Dp(6) });
        }
    }

    // An equipment or belt box: the item's sprite and count, or an empty well with the box's name.
    private View Slot(OwnedItemView item, string label)
    {
        if (item is not null)
        {
            return Tile(item, Dp(4));
        }

        var empty = VulkanPanel.Body(_activity, label, 10, Color.Argb(150, 168, 199, 193));
        empty.Gravity = GravityFlags.Center;
        empty.SetSingleLine(true);
        empty.Background = new VulkanPanelArt.WellDrawable(Dp(6), Dp(1), Color.Transparent, selected: false);
        return empty;
    }

    // An item's sprite in its well, tinted by what the item is and lit gold when it is the chosen one, with its upgrade
    // level at the top left and its count at the bottom right.
    private View Tile(OwnedItemView item, int padding)
    {
        var tile = new FrameLayout(_activity) { Clickable = true, ContentDescription = item.Name };
        tile.Background = new VulkanPanelArt.WellDrawable(Dp(6), Dp(1), Tint(item), item.InstanceId == _selected);
        VulkanAttention.Gold(tile, item.Details?.UpgradeLevel == AgingRules.MaxLevel, Dp(6));
        var image = new ImageView(_activity);
        image.SetPadding(padding, padding, padding, padding);
        image.SetScaleType(ImageView.ScaleType.FitCenter!);
        SetItemPicture(image, item, Dp(18));

        tile.AddView(image, new FrameLayout.LayoutParams(-1, -1));
        if (item.Details is { UpgradeLevel: > 0 } details)
        {
            var upgrade = VulkanPanel.Text(_activity, $"+{details.UpgradeLevel}", 10, VulkanHudArt.EdgeLight, serif: true);
            tile.AddView(upgrade, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Top | GravityFlags.Left) { LeftMargin = Dp(3), TopMargin = Dp(2) });
        }

        if (item.Count > 1)
        {
            var count = VulkanPanel.Text(_activity, $"{item.Count}", 11, Color.White, serif: false);
            count.Typeface = Typeface.Create(Typeface.SansSerif, TypefaceStyle.Bold);
            tile.AddView(count, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Bottom | GravityFlags.Right) { RightMargin = Dp(4), BottomMargin = Dp(2) });
        }

        tile.Click += (_, _) =>
        {
            _selected = _selected == item.InstanceId ? Guid.Empty : item.InstanceId;
            Render();
        };
        return tile;
    }

    private void SetItemPicture(ImageView picture, OwnedItemView item, int glyphSize, HudGlyph fallback = HudGlyph.Bag)
    {
        if (_icons.Get(item.CatalogKey) is { } icon)
        {
            picture.SetImageBitmap(icon);
            picture.Drawable?.SetFilterBitmap(true);
            return;
        }

        var glyph = item.Kind switch
        {
            ItemKind.Potion => HudGlyph.Potion,
            ItemKind.Shield => HudGlyph.Shield,
            ItemKind.Ring => HudGlyph.Ring,
            _ => fallback
        };
        picture.SetImageDrawable(new HudIcon(glyph, glyphSize));
    }

    // The chosen item's sheet: its sprite, name and kind, its values on alternating bands, its durability as a gauge and
    // what can be done with it here.
    private void RenderSheet(OwnedItemView item)
    {
        _sheet.RemoveAllViews();
        _actions.Clear();
        _maturity = null;
        _sheet.Visibility = item is null ? ViewStates.Gone : ViewStates.Visible;
        if (item is null)
        {
            return;
        }

        var heading = new LinearLayout(_activity);
        heading.SetGravity(GravityFlags.CenterVertical);
        var picture = new ImageView(_activity);
        picture.SetScaleType(ImageView.ScaleType.FitCenter!);
        picture.SetPadding(Dp(5), Dp(5), Dp(5), Dp(5));
        picture.Background = new VulkanPanelArt.WellDrawable(Dp(8), Dp(1), Tint(item), selected: false);
        VulkanAttention.Gold(picture, item.Details?.UpgradeLevel == AgingRules.MaxLevel, Dp(8));
        SetItemPicture(picture, item, Dp(28));

        heading.AddView(picture, new LinearLayout.LayoutParams(Dp(50), Dp(50)) { RightMargin = Dp(8) });
        var identity = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        var name = VulkanPanel.Text(_activity, item.Name, 15, Tint(item) is { A: > 0 } tint ? tint : MobileHudTheme.Gold, serif: true);
        name.SetMaxLines(2);
        name.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        identity.AddView(name);
        var chips = new LinearLayout(_activity);
        chips.AddView(VulkanPanelArt.Chip(_activity, KindName(item.Kind), VulkanPanel.Muted), new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(4) });
        if (Rank(item) is { } rank)
        {
            chips.AddView(VulkanPanelArt.Chip(_activity, rank, Tint(item)));
        }

        identity.AddView(chips, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(4) });
        heading.AddView(identity, new LinearLayout.LayoutParams(0, -2, 1));
        _sheet.AddView(heading);
        if (item.Details is { ClassAllowed: false })
        {
            _sheet.AddView(VulkanPanelArt.Chip(_activity, "Sua classe não pode usar", VulkanPanelArt.Warning),
                new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(6) });
        }

        _maturity = new VulkanGaugeView(_activity);
        _sheet.AddView(_maturity, new LinearLayout.LayoutParams(-1, Dp(20)) { TopMargin = Dp(7) });
        UpdateMaturity(item);

        var lines = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(_activity);
        scroll.AddView(lines);
        _sheet.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1) { TopMargin = Dp(8) });
        var band = 0;
        var aged = item.Details is { UpgradeLevel: > 0 } ? AgingRules.RaisedBy(item.CatalogKey) : AgedStats.None;
        foreach (var (label, value, stat) in Values(item))
        {
            var row = new LinearLayout(_activity);
            row.SetGravity(GravityFlags.CenterVertical);
            row.SetPadding(Dp(8), Dp(4), Dp(8), Dp(4));
            if (band++ % 2 == 0)
            {
                row.SetBackgroundColor(VulkanPanel.Row);
            }

            var raised = (aged & stat) != 0;
            row.AddView(VulkanPanel.Body(_activity, label, 12, raised ? AgingBlue : VulkanPanel.Muted), new LinearLayout.LayoutParams(0, -2, 1));
            row.AddView(VulkanPanel.Text(_activity, value, 13, raised ? AgingBlue : Color.White, serif: true));
            lines.AddView(row);
        }

        if (item.Details is { DurabilityMax: > 0 } details)
        {
            var fraction = details.DurabilityCurrent / (float)details.DurabilityMax;
            var durability = new VulkanGaugeView(_activity);
            var gauge = (aged & AgedStats.Integrity) != 0 ? VulkanHudArt.Mana : VulkanHudArt.Experience;
            durability.Set(fraction < .25f ? VulkanHudArt.Life : gauge, fraction,
                $"Durabilidade {details.DurabilityCurrent}/{details.DurabilityMax}");
            lines.AddView(durability, new LinearLayout.LayoutParams(-1, Dp(16)) { TopMargin = Dp(6) });
        }

        var actions = new LinearLayout(_activity);
        _sheet.AddView(actions, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });
        if (item.InBag && _session.Quest is { State: PristonTale.Simulation.Quests.EliteQuests.Running } quest
            && PristonTale.Simulation.Quests.EliteQuests.Find(quest.Code) is
                { Objective: PristonTale.Simulation.Quests.EliteObjective.Use or PristonTale.Simulation.Quests.EliteObjective.UseAndKill } definition
            && item.Code == definition.AcceptItem)
        {
            var use = MobileHudTheme.Button(_activity, "Usar item de missão");
            actions.AddView(use);
            use.Click += async (_, _) =>
            {
                use.Enabled = false;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var command = new PristonTale.Simulation.Quests.QuestCommand(Guid.NewGuid(), 0,
                        PristonTale.Simulation.Quests.QuestAction.UseItem, quest.Code, quest.State, 0);
                    var result = await _session.Play.RequestQuestAsync(command, timeout.Token);
                    Toast.MakeText(_activity, result.Ok ? "Item de missão usado." : "O servidor recusou o uso do item.", ToastLength.Short)?.Show();
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
                {
                    Toast.MakeText(_activity, "Não foi possível usar o item de missão.", ToastLength.Short)?.Show();
                }
                finally
                {
                    use.Enabled = true;
                }
            };
        }
        var items = _owner?.Items ?? [];
        var slot = item.Equipped ? (EquipmentSlot?)item.Slot
            : ItemEquipmentMetadata.DefaultSlot(item.Kind, ItemEquipmentMetadata.SlotOf(item.Kind),
                candidate => OwnedItemView.InSlot(items, (int)candidate) is not null);
        if (item.BeltBox >= 0)
        {
            AddAction(actions, "Guardar na bolsa", item, InventoryAction.BeltTake, -1, slot, prominent: true);
        }
        else if (item.InBag && item.Kind == ItemKind.Potion)
        {
            for (var box = 0; box < PotionBelt.Boxes; box++)
            {
                AddAction(actions, $"Cinto {box + 1}", item, InventoryAction.BeltPut, box, slot, prominent: false);
            }
        }
        else if (item.InBag && UsableItemCatalog.TryGet(item.CatalogKey, out var rule) && rule.Action == UsableItemAction.MonsterCrystal)
        {
            var summon = AddActionButton(actions, "Invocar", prominent: true);
            summon.Click += (_, _) => ConfirmCrystal(item);
        }
        else if ((item.Equipped || item.InBag) && slot.HasValue)
        {
            AddAction(actions, item.Equipped ? "Retirar" : "Equipar", item, item.Equipped ? InventoryAction.Unequip : InventoryAction.Equip,
                item.Equipped ? -1 : 0, slot, prominent: true);
        }
    }

    private void UpdateMaturity(OwnedItemView item)
    {
        if (HudItemDetailTooltip.Gauge(item, _owner) is not { Goal: > 0 } aging)
        {
            _maturity.Visibility = ViewStates.Gone;
            return;
        }

        _maturity.Visibility = ViewStates.Visible;
        _maturity.Set(VulkanHudArt.Mana, aging.Count / (float)aging.Goal, $"Maturação +{aging.Level} · {aging.Count}/{aging.Goal}");
    }

    // An item's tint: red when the character's class cannot use it, amber for a unique item, blue for an upgraded, aged
    // or mixed one, none otherwise (and while its details have not arrived).
    private static Color Tint(OwnedItemView item) => item.Details switch
    {
        { ClassAllowed: false } => VulkanPanelArt.Warning,
        { UniqueItem: not 0 } => Unique,
        { UpgradeLevel: > 0 } or { AgingGoal: > 0 } or { MixSheltom: > 0 } => Enhanced,
        _ => Color.Transparent
    };

    private static string Rank(OwnedItemView item) => item.Details switch
    {
        { UniqueItem: not 0 } => "Único",
        { MixSheltom: > 0 } => "Misturado",
        { UpgradeLevel: > 0 } details => $"Aprimorado +{details.UpgradeLevel}",
        { AgingGoal: > 0 } => "Em maturação",
        _ => null
    };

    private static IEnumerable<(string Label, string Value, AgedStats Stat)> Values(OwnedItemView item)
    {
        if (item.Count > 1)
        {
            yield return ("Quantidade", $"{item.Count}", AgedStats.None);
        }

        if (item.Details is not { } details)
        {
            yield break;
        }

        var roll = details.Roll;
        if (roll.DamageMax > 0)
        {
            yield return ("Dano", $"{roll.DamageMin}–{roll.DamageMax}", AgedStats.Damage);
        }

        if (roll.AttackRating > 0)
        {
            yield return ("Precisão", $"{roll.AttackRating}", AgedStats.AttackRating);
        }

        if (details.AttackSpeed > 0)
        {
            yield return ("Velocidade", $"{details.AttackSpeed}", AgedStats.None);
        }

        if (details.Critical > 0)
        {
            yield return ("Crítico", $"{details.Critical}%", AgedStats.Critical);
        }

        if (details.ShootingRange > 0)
        {
            yield return ("Alcance", $"{details.ShootingRange}", AgedStats.None);
        }

        if (roll.Defence > 0)
        {
            yield return ("Defesa", $"{roll.Defence}", AgedStats.Defence);
        }

        if (roll.Absorb > 0)
        {
            yield return ("Absorção", HudItemDetailTooltip.Tenths(roll.Absorb + ItemAbsorbCorrection.For(item.CatalogKey)), AgedStats.Absorb);
        }

        if (roll.Block > 0)
        {
            yield return ("Bloqueio", $"{roll.Block:0.#}%", AgedStats.Block);
        }

        if (roll.AddLife > 0)
        {
            yield return ("PV", $"+{roll.AddLife}", AgedStats.None);
        }

        if (roll.AddMana > 0)
        {
            yield return ("Mana", $"+{roll.AddMana}", AgedStats.Mana);
        }

        if (roll.RequiredLevel > 0)
        {
            yield return ("Nível", $"{roll.RequiredLevel}", AgedStats.None);
        }

        if (details.Weight > 0)
        {
            yield return ("Peso", $"{details.Weight}", AgedStats.None);
        }
    }

    private static string KindName(ItemKind kind)
    {
        var slot = ItemEquipmentMetadata.SlotOf(kind);
        return kind == ItemKind.Potion ? "Poção" : slot switch
        {
            EquipmentSlot.Weapon => "Arma",
            EquipmentSlot.OffHand => "Escudo",
            EquipmentSlot.Armor => "Armadura",
            EquipmentSlot.Gloves => "Luvas",
            EquipmentSlot.Boots => "Botas",
            EquipmentSlot.Bracelet => "Bracelete",
            EquipmentSlot.Amulet => "Amuleto",
            EquipmentSlot.RingLeft or EquipmentSlot.RingRight => "Anel",
            EquipmentSlot.Sheltom => "Sheltom",
            _ => "Item"
        };
    }

    private void AddAction(LinearLayout row, string label, OwnedItemView item, InventoryAction action, int destination, EquipmentSlot? slot,
        bool prominent)
    {
        var button = AddActionButton(row, label, prominent);
        button.Click += async (_, _) => await ApplyAsync(item, action, destination, slot.GetValueOrDefault());
    }

    private Button AddActionButton(LinearLayout row, string label, bool prominent)
    {
        var button = MobileHudTheme.Button(_activity, label, prominent);
        if (prominent)
        {
            button.Typeface = Typeface.Create("serif", TypefaceStyle.Bold);
        }
        else
        {
            button.SetPadding(Dp(2), Dp(4), Dp(2), Dp(4));
        }

        button.Enabled = !_pending && _owner is not null && _owner.InventoryRevision >= _expectedRevision;
        row.AddView(button, new LinearLayout.LayoutParams(0, Dp(40), 1) { RightMargin = Dp(3) });
        _actions.Add(button);
        return button;
    }

    private void ConfirmCrystal(OwnedItemView item)
    {
        if (_crystalConfirmation is not null || _pending || _panel is null)
        {
            return;
        }

        var dialog = new Dialog(_activity);
        dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);
        dialog.SetCanceledOnTouchOutside(true);
        var plate = new LinearLayout(_activity) { Orientation = Orientation.Vertical, Background = new PlateDrawable(Dp(14)) };
        plate.SetPadding(Dp(18), Dp(14), Dp(18), Dp(16));
        plate.AddView(VulkanPanel.Title(_activity, "Invocar criatura"));
        plate.AddView(VulkanPanel.Divider(_activity),
            new LinearLayout.LayoutParams(-1, Dp(1)) { TopMargin = Dp(10), BottomMargin = Dp(12) });

        var card = new LinearLayout(_activity) { Background = VulkanPanelArt.Inset(_activity) };
        card.SetGravity(GravityFlags.CenterVertical);
        card.SetPadding(Dp(10), Dp(10), Dp(10), Dp(10));
        var picture = new ImageView(_activity) { Background = new VulkanPanelArt.WellDrawable(Dp(8), Dp(1), VulkanHudArt.Edge, selected: false) };
        picture.SetScaleType(ImageView.ScaleType.FitCenter!);
        picture.SetPadding(Dp(6), Dp(6), Dp(6), Dp(6));
        SetItemPicture(picture, item, Dp(28), HudGlyph.Skill);
        card.AddView(picture, new LinearLayout.LayoutParams(Dp(52), Dp(52)) { RightMargin = Dp(12) });
        var details = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        details.AddView(VulkanPanel.Heading(_activity, item.Name, 16));
        const string cost = "A invocação consome uma unidade deste item.";
        details.AddView(VulkanPanel.Body(_activity, cost, 13, VulkanPanel.Muted), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(4) });
        card.AddView(details, new LinearLayout.LayoutParams(0, -2, 1));
        plate.AddView(card, new LinearLayout.LayoutParams(-1, -2));

        var actions = new LinearLayout(_activity);
        actions.SetGravity(GravityFlags.Right);
        var cancel = MobileHudTheme.Button(_activity, "Cancelar");
        var confirm = MobileHudTheme.Button(_activity, "Invocar", prominent: true);
        actions.AddView(cancel, new LinearLayout.LayoutParams(Dp(106), Dp(44)) { RightMargin = Dp(8) });
        actions.AddView(confirm, new LinearLayout.LayoutParams(Dp(106), Dp(44)));
        plate.AddView(actions, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(12) });

        cancel.Click += (_, _) => DismissCrystalConfirmation();
        confirm.Click += async (_, _) =>
        {
            confirm.Enabled = false;
            DismissCrystalConfirmation();
            await UseCrystalAsync(item.InstanceId);
        };
        dialog.DismissEvent += (_, _) =>
        {
            if (_crystalConfirmation == dialog)
            {
                _crystalConfirmation = null;
            }

            dialog.Dispose();
        };

        _crystalConfirmation = dialog;
        dialog.SetContentView(plate);
        dialog.Show();
        dialog.Window?.SetBackgroundDrawableResource(global::Android.Resource.Color.Transparent);
        dialog.Window?.SetLayout(Math.Min(Dp(408), _overlay.Width - Dp(24)), -2);
    }

    private void DismissCrystalConfirmation()
    {
        if (_crystalConfirmation is { } dialog)
        {
            _crystalConfirmation = null;
            dialog.Dismiss();
        }
    }

    private async Task UseCrystalAsync(Guid itemId)
    {
        if (_pending || _panel is null || _session.Owner is not { } owner || owner.InventoryRevision < _expectedRevision)
        {
            return;
        }

        if (OwnedItemView.Find(owner.Items, itemId) is not { InBag: true })
        {
            Toast.MakeText(_activity, ClientText.Get(PristonTale.Contracts.Texts.GameTextId.InventoryChanged), ToastLength.Short)?.Show();
            return;
        }

        _pending = true;
        Render();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var command = new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, itemId);
            var result = await _session.Play.UseItemAsync(command, timeout.Token);
            _expectedRevision = result.Revision;
            var message = result.Ok ? "Cristal usado." : ClientText.Format(result.Message.IsNone ? ItemUseMessages.For(result.Status) : result.Message);
            Toast.MakeText(_activity, message, ToastLength.Long)?.Show();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            Toast.MakeText(_activity, ClientText.Get("ItemUse.ConnectionError"), ToastLength.Long)?.Show();
        }
        finally
        {
            _pending = false;
            Render();
        }
    }

    private async Task ApplyAsync(OwnedItemView item, InventoryAction action, int destination, EquipmentSlot slot)
    {
        if (_pending || _owner is null || _owner.InventoryRevision < _expectedRevision)
        {
            return;
        }

        _pending = true;
        Render();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var command = new InventoryCommand(Guid.NewGuid(), _owner.InventoryRevision, action, item.InstanceId, destination, 0, slot);
            var result = await _session.Play.ApplyInventoryAsync(command, timeout.Token);
            _expectedRevision = result.Revision;
            if (!result.Ok)
            {
                Toast.MakeText(_activity, "O servidor recusou a operação.", ToastLength.Short)?.Show();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            Toast.MakeText(_activity, "Não foi possível atualizar o inventário.", ToastLength.Short)?.Show();
        }
        finally
        {
            _pending = false;
            Render();
        }
    }

    private float Density => _activity.Resources?.DisplayMetrics?.Density ?? 1f;

    private int Dp(int value) => (int)MathF.Round(value * Density);
}
