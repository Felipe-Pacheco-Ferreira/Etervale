using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

// The character window: equipment around the character on the left, the 12 x 6 bag with each item's own sprite at its
// footprint on the right, the potion belt under it, gold and weight in the header, and the chosen item's sheet with
// what can be done with it. Every change is an inventory command the server validates (InventoryCommand); the window
// redraws from the server's answer.
internal sealed class VulkanInventoryPanel
{
    private static readonly Color Parchment = Color.Rgb(236, 225, 203);
    private static readonly Color SlotFill = Color.Argb(235, 22, 20, 19);
    private static readonly Color SlotEdge = Color.Argb(220, 120, 94, 52);
    private static readonly Color Selected = Color.Rgb(255, 214, 64);
    private static readonly Color Muted = Color.Rgb(140, 128, 108);

    // The equipment slots as they stand around the character: rings and amulet above, the hands and armour across the
    // middle, gloves, bracelet and boots below, the sheltom under them.
    private static readonly (EquipmentSlot Slot, string Label, int Column, int Row)[] Doll =
    [
        (EquipmentSlot.RingLeft, "Anel", 0, 0), (EquipmentSlot.Amulet, "Amuleto", 1, 0), (EquipmentSlot.RingRight, "Anel", 2, 0),
        (EquipmentSlot.Weapon, "Arma", 0, 1), (EquipmentSlot.Armor, "Armadura", 1, 1), (EquipmentSlot.OffHand, "Escudo", 2, 1),
        (EquipmentSlot.Gloves, "Luvas", 0, 2), (EquipmentSlot.Bracelet, "Bracelete", 1, 2), (EquipmentSlot.Boots, "Botas", 2, 2),
        (EquipmentSlot.Sheltom, "Sheltom", 1, 3)
    ];

    private readonly Activity _activity;
    private readonly FrameLayout _overlay;
    private readonly VulkanWorldSession _session;
    private readonly VulkanItemIcons _icons;
    private FrameLayout _window;
    private FrameLayout _equipment;
    private FrameLayout _bag;
    private LinearLayout _belt;
    private LinearLayout _sheet;
    private TextView _gold;
    private TextView _weight;
    private OwnerState _owner;
    private Guid _selected;
    private bool _pending;
    private long _expectedRevision;

    public VulkanInventoryPanel(Activity activity, FrameLayout overlay, VulkanWorldSession session)
    {
        _activity = activity;
        _overlay = overlay;
        _session = session;
        _icons = new VulkanItemIcons(session.AssetRoot, action => activity.RunOnUiThread(action));
        _icons.Loaded += () =>
        {
            if (_window is not null)
            {
                Render();
            }
        };
    }

    public void Toggle()
    {
        if (_window is not null)
        {
            Close();
            return;
        }

        _owner = _session.Owner;
        Open();
        Render();
    }

    public void Update(OwnerState state)
    {
        var changed = _owner?.InventoryRevision != state.InventoryRevision || !ReferenceEquals(_owner?.Items, state.Items)
            || _owner?.Money != state.Money;
        _owner = state;
        if (_window is not null && changed)
        {
            Render();
        }
    }

    private void Open()
    {
        // The dimmed screen behind the window closes it when touched.
        _window = new FrameLayout(_activity) { Clickable = true };
        _window.SetBackgroundColor(Color.Argb(150, 0, 0, 0));
        _window.Click += (_, _) => Close();

        var frame = new LinearLayout(_activity) { Orientation = Orientation.Vertical, Clickable = true };
        frame.SetPadding(Dp(14), Dp(10), Dp(14), Dp(12));
        frame.Background = Panel();
        var width = Math.Min(Dp(820), _overlay.Width - Dp(24));
        var height = Math.Min(Dp(392), _overlay.Height - Dp(20));
        _window.AddView(frame, new FrameLayout.LayoutParams(width, height, GravityFlags.Center));

        var header = new LinearLayout(_activity);
        header.SetGravity(GravityFlags.CenterVertical);
        var title = Label("Inventário", 20, MobileHudTheme.Gold);
        title.Typeface = Typeface.Create("serif", TypefaceStyle.Bold);
        header.AddView(title, new LinearLayout.LayoutParams(0, -2, 1));
        _gold = Label("", 14, Color.Rgb(255, 214, 64));
        header.AddView(_gold, new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(16) });
        _weight = Label("", 14, Parchment);
        header.AddView(_weight, new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(12) });
        var close = MobileHudTheme.Button(_activity, "✕");
        close.ContentDescription = "Fechar inventário";
        close.Click += (_, _) => Close();
        header.AddView(close, new LinearLayout.LayoutParams(Dp(42), Dp(38)));
        frame.AddView(header);
        frame.AddView(Divider(), new LinearLayout.LayoutParams(-1, Dp(1)) { TopMargin = Dp(6), BottomMargin = Dp(8) });

        var body = new LinearLayout(_activity);
        frame.AddView(body, new LinearLayout.LayoutParams(-1, 0, 1));

        var left = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        left.SetGravity(GravityFlags.CenterHorizontal);
        left.AddView(Label("Equipamento", 13, MobileHudTheme.Gold));
        _equipment = new FrameLayout(_activity);
        left.AddView(_equipment, new LinearLayout.LayoutParams(Dp(3 * 58), Dp(4 * 58)) { TopMargin = Dp(4) });
        body.AddView(left, new LinearLayout.LayoutParams(-2, -1) { RightMargin = Dp(14) });

        var right = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        right.AddView(Label("Bolsa", 13, MobileHudTheme.Gold));
        _bag = new FrameLayout(_activity);
        right.AddView(_bag, new LinearLayout.LayoutParams(-1, 0, 1) { TopMargin = Dp(4) });
        var beltRow = new LinearLayout(_activity);
        beltRow.SetGravity(GravityFlags.CenterVertical);
        beltRow.AddView(Label("Cinto", 13, MobileHudTheme.Gold), new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(8) });
        _belt = new LinearLayout(_activity);
        beltRow.AddView(_belt);
        right.AddView(beltRow, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        body.AddView(right, new LinearLayout.LayoutParams(0, -1, 1));

        _sheet = new LinearLayout(_activity) { Orientation = Orientation.Vertical, Clickable = true, Visibility = ViewStates.Gone };
        _sheet.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        _sheet.Background = Panel();
        body.AddView(_sheet, new LinearLayout.LayoutParams(Dp(214), -1) { LeftMargin = Dp(12) });

        _overlay.AddView(_window, new FrameLayout.LayoutParams(-1, -1));
        // The bag's cells take the room the layout gives it.
        _bag.Post(Render);
    }

    private void Render()
    {
        if (_window is null)
        {
            return;
        }

        var items = _owner?.Items ?? [];
        _gold.Text = _owner is null ? "" : $"Ouro {_owner.Money:N0}";
        _weight.Text = _owner is null ? "" : $"Peso {_owner.CarriedWeight}/{_owner.MaxWeight}";
        _weight.SetTextColor(_owner?.Overweight == true ? Color.Rgb(240, 110, 90) : Parchment);
        RenderEquipment(items);
        RenderBag(items);
        RenderBelt(items);
        RenderSheet(OwnedItemView.Find(items, _selected));
    }

    private void RenderEquipment(IReadOnlyList<OwnedItemView> items)
    {
        _equipment.RemoveAllViews();
        var size = Dp(58);
        foreach (var (slot, label, column, row) in Doll)
        {
            var item = OwnedItemView.InSlot(items, (int)slot);
            var cell = Slot(item, label);
            _equipment.AddView(cell, new FrameLayout.LayoutParams(size - Dp(4), size - Dp(4))
            {
                LeftMargin = column * size + Dp(2),
                TopMargin = row * size + Dp(2)
            });
        }
    }

    private void RenderBag(IReadOnlyList<OwnedItemView> items)
    {
        _bag.RemoveAllViews();
        if (_bag.Width == 0 || _bag.Height == 0)
        {
            return;
        }

        var cell = Math.Min(_bag.Width / Inventory.DefaultWidth, _bag.Height / Inventory.DefaultHeight);
        var grid = new View(_activity) { Background = new BagGrid(cell, Inventory.DefaultWidth, Inventory.DefaultHeight, Dp(1)) };
        _bag.AddView(grid, new FrameLayout.LayoutParams(cell * Inventory.DefaultWidth, cell * Inventory.DefaultHeight));
        foreach (var item in items)
        {
            if (!item.InBag || item.Anchor < 0)
            {
                continue;
            }

            var tile = Tile(item, Dp(2));
            _bag.AddView(tile, new FrameLayout.LayoutParams(cell * item.Width, cell * item.Height)
            {
                LeftMargin = item.Anchor % Inventory.DefaultWidth * cell,
                TopMargin = item.Anchor / Inventory.DefaultWidth * cell
            });
        }
    }

    private void RenderBelt(IReadOnlyList<OwnedItemView> items)
    {
        _belt.RemoveAllViews();
        for (var box = 0; box < PotionBelt.Boxes; box++)
        {
            var item = OwnedItemView.InBox(items, box);
            var cell = Slot(item, $"{box + 1}");
            _belt.AddView(cell, new LinearLayout.LayoutParams(Dp(46), Dp(46)) { RightMargin = Dp(6) });
        }
    }

    // An equipment or belt box: the item's sprite and count, or the box's name while empty.
    private View Slot(OwnedItemView item, string label)
    {
        if (item is not null)
        {
            return Tile(item, Dp(3));
        }

        var empty = Label(label, 10, Muted);
        empty.Gravity = GravityFlags.Center;
        empty.Background = Box(SlotFill, SlotEdge, Dp(1));
        return empty;
    }

    // An item's sprite in its box, framed in gold when it is the chosen one, with its count when it stacks.
    private View Tile(OwnedItemView item, int padding)
    {
        var tile = new FrameLayout(_activity) { Clickable = true };
        tile.Background = Box(Color.Argb(150, 40, 34, 28), item.InstanceId == _selected ? Selected : SlotEdge,
            item.InstanceId == _selected ? Dp(2) : Dp(1));
        tile.ContentDescription = item.Name;
        var image = new ImageView(_activity);
        image.SetPadding(padding, padding, padding, padding);
        image.SetScaleType(ImageView.ScaleType.FitCenter!);
        if (_icons.Get(item.CatalogKey) is { } icon)
        {
            image.SetImageBitmap(icon);
        }
        else
        {
            image.SetImageDrawable(new HudIcon(item.Kind == ItemKind.Potion ? HudGlyph.Potion : HudGlyph.Bag, Dp(18)));
        }

        tile.AddView(image, new FrameLayout.LayoutParams(-1, -1));
        if (item.Count > 1)
        {
            var count = Label($"{item.Count}", 10, Color.White);
            count.SetShadowLayer(2f, 0f, 1f, Color.Black);
            tile.AddView(count, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Bottom | GravityFlags.Right) { RightMargin = Dp(3) });
        }

        tile.Click += (_, _) =>
        {
            _selected = _selected == item.InstanceId ? Guid.Empty : item.InstanceId;
            Render();
        };
        return tile;
    }

    // The chosen item's sheet: its sprite and name, its values, and what can be done with it here.
    private void RenderSheet(OwnedItemView item)
    {
        _sheet.RemoveAllViews();
        _sheet.Visibility = item is null ? ViewStates.Gone : ViewStates.Visible;
        if (item is null)
        {
            return;
        }

        var heading = new LinearLayout(_activity);
        heading.SetGravity(GravityFlags.CenterVertical);
        var picture = new ImageView(_activity);
        picture.SetScaleType(ImageView.ScaleType.FitCenter!);
        picture.Background = Box(SlotFill, SlotEdge, Dp(1));
        if (_icons.Get(item.CatalogKey) is { } icon)
        {
            picture.SetImageBitmap(icon);
        }

        heading.AddView(picture, new LinearLayout.LayoutParams(Dp(44), Dp(44)) { RightMargin = Dp(8) });
        var name = Label(item.Name, 14, MobileHudTheme.Gold);
        name.Typeface = Typeface.Create("serif", TypefaceStyle.Bold);
        heading.AddView(name, new LinearLayout.LayoutParams(0, -2, 1));
        _sheet.AddView(heading);

        var lines = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(_activity);
        scroll.AddView(lines);
        _sheet.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1) { TopMargin = Dp(6) });
        foreach (var (label, value) in Values(item))
        {
            var row = new LinearLayout(_activity);
            row.AddView(Label(label, 12, Muted), new LinearLayout.LayoutParams(0, -2, 1));
            row.AddView(Label(value, 12, Parchment));
            lines.AddView(row);
        }

        var actions = new LinearLayout(_activity);
        _sheet.AddView(actions, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        var items = _owner?.Items ?? [];
        var slot = item.Equipped ? (EquipmentSlot?)item.Slot
            : ItemEquipmentMetadata.DefaultSlot(item.Kind, ItemEquipmentMetadata.SlotOf(item.Kind),
                candidate => OwnedItemView.InSlot(items, (int)candidate) is not null);
        if (item.BeltBox >= 0)
        {
            AddAction(actions, "Guardar", item, InventoryAction.BeltTake, -1, slot);
        }
        else if (item.InBag && item.Kind == ItemKind.Potion)
        {
            for (var box = 0; box < PotionBelt.Boxes; box++)
            {
                AddAction(actions, $"Cinto {box + 1}", item, InventoryAction.BeltPut, box, slot);
            }
        }
        else if ((item.Equipped || item.InBag) && slot.HasValue)
        {
            AddAction(actions, item.Equipped ? "Retirar" : "Equipar", item, item.Equipped ? InventoryAction.Unequip : InventoryAction.Equip,
                item.Equipped ? -1 : 0, slot);
        }
    }

    private static IEnumerable<(string Label, string Value)> Values(OwnedItemView item)
    {
        yield return ("Tipo", KindName(item.Kind));
        if (item.Count > 1)
        {
            yield return ("Quantidade", $"{item.Count}");
        }

        if (item.Details is not { } details)
        {
            yield break;
        }

        var roll = details.Roll;
        if (roll.DamageMax > 0)
        {
            yield return ("Dano", $"{roll.DamageMin}–{roll.DamageMax}");
        }

        if (roll.AttackRating > 0)
        {
            yield return ("Precisão", $"{roll.AttackRating}");
        }

        if (details.AttackSpeed > 0)
        {
            yield return ("Velocidade", $"{details.AttackSpeed}");
        }

        if (details.Critical > 0)
        {
            yield return ("Crítico", $"{details.Critical}%");
        }

        if (details.ShootingRange > 0)
        {
            yield return ("Alcance", $"{details.ShootingRange}");
        }

        if (roll.Defence > 0)
        {
            yield return ("Defesa", $"{roll.Defence}");
        }

        if (roll.Absorb > 0)
        {
            yield return ("Absorção", $"{roll.Absorb:0.#}");
        }

        if (roll.Block > 0)
        {
            yield return ("Bloqueio", $"{roll.Block:0.#}%");
        }

        if (roll.AddLife > 0)
        {
            yield return ("PV", $"+{roll.AddLife}");
        }

        if (details.DurabilityMax > 0)
        {
            yield return ("Durabilidade", $"{details.DurabilityCurrent}/{details.DurabilityMax}");
        }

        if (roll.RequiredLevel > 0)
        {
            yield return ("Nível", $"{roll.RequiredLevel}");
        }

        if (details.Weight > 0)
        {
            yield return ("Peso", $"{details.Weight}");
        }

        if (!details.ClassAllowed)
        {
            yield return ("Classe", "não pode usar");
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

    private void AddAction(LinearLayout row, string label, OwnedItemView item, InventoryAction action, int destination, EquipmentSlot? slot)
    {
        var button = MobileHudTheme.Button(_activity, label);
        button.Enabled = !_pending && _owner is not null && _owner.InventoryRevision >= _expectedRevision;
        button.Click += async (_, _) => await ApplyAsync(item, action, destination, slot.GetValueOrDefault());
        row.AddView(button, new LinearLayout.LayoutParams(0, Dp(38), 1) { RightMargin = Dp(3) });
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

    private void Close()
    {
        if (_window is not null)
        {
            _overlay.RemoveView(_window);
        }

        _window = null;
        _selected = Guid.Empty;
    }

    private TextView Label(string text, float size, Color color)
    {
        var label = new TextView(_activity) { Text = text, TextSize = size };
        label.SetTextColor(color);
        return label;
    }

    private View Divider()
    {
        var line = new View(_activity);
        line.SetBackgroundColor(SlotEdge);
        return line;
    }

    private GradientDrawable Panel()
    {
        var shape = new GradientDrawable(GradientDrawable.Orientation.TopBottom!,
            [Color.Argb(245, 34, 30, 27).ToArgb(), Color.Argb(245, 16, 14, 14).ToArgb()]);
        shape.SetCornerRadius(Dp(10));
        shape.SetStroke(Dp(2), Color.Rgb(153, 120, 65));
        return shape;
    }

    private GradientDrawable Box(Color fill, Color edge, int stroke)
    {
        var shape = new GradientDrawable();
        shape.SetColor(fill.ToArgb());
        shape.SetCornerRadius(Dp(3));
        shape.SetStroke(stroke, edge);
        return shape;
    }

    private int Dp(int value) => (int)MathF.Round(value * (_activity.Resources?.DisplayMetrics?.Density ?? 1f));

    // The bag's empty cells: a dark field with a thin line between cells.
    private sealed class BagGrid(int cell, int columns, int rows, int line) : Drawable
    {
        private readonly Paint _fill = new() { Color = SlotFill };
        private readonly Paint _line = new() { Color = Color.Argb(110, 120, 94, 52), StrokeWidth = line };

        public override void Draw(Canvas canvas)
        {
            canvas.DrawRect(0, 0, cell * columns, cell * rows, _fill);
            for (var x = 0; x <= columns; x++)
            {
                canvas.DrawLine(x * cell, 0, x * cell, cell * rows, _line);
            }

            for (var y = 0; y <= rows; y++)
            {
                canvas.DrawLine(0, y * cell, cell * columns, y * cell, _line);
            }
        }

        public override void SetAlpha(int alpha) => _fill.Alpha = alpha;

        public override void SetColorFilter(ColorFilter colorFilter) => _fill.SetColorFilter(colorFilter);

#pragma warning disable CS0672 // Android marks it obsolete, but a Drawable must still answer it.
        public override int Opacity => (int)Format.Translucent;
#pragma warning restore CS0672
    }
}
