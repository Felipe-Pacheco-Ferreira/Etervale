using System.Globalization;
using Android.App;
using Android.Text;
using Android.Widget;
using PristonTale.Gameplay.Combat;
using PristonTale.Simulation.Items;

internal sealed partial class VulkanFreeHuntPanel
{
    private readonly record struct LootCatalogItem(string Code, string Name, LootHuntCategory Category);

    private LootCatalogItem[] itemCatalog;
    private readonly List<(ImageView Icon, string Code)> pendingLootIcons = [];
    private static readonly (LootHuntCategory Kind, string Name)[] Categories =
    [
        (LootHuntCategory.Gold, "Ouro"), (LootHuntCategory.Potions, "Poções"), (LootHuntCategory.Sheltoms, "Sheltoms"),
        (LootHuntCategory.Force, "Force Orbs"), (LootHuntCategory.Crystals, "Cristais de invocação"),
        (LootHuntCategory.Materials, "Materiais de fabricação"), (LootHuntCategory.Runes, "Runas"),
        (LootHuntCategory.Travel, "Núcleos de viagem"), (LootHuntCategory.Premium, "Benefícios Premium"),
        (LootHuntCategory.Castle, "Itens de castelo"), (LootHuntCategory.Masteries, "Mestres de skill e aging"),
        (LootHuntCategory.Events, "Consumíveis de evento"), (LootHuntCategory.OtherUsable, "Outros consumíveis"),
        (LootHuntCategory.Weapons, "Armas"), (LootHuntCategory.Armor, "Armaduras e robes"), (LootHuntCategory.Shields, "Escudos"),
        (LootHuntCategory.Boots, "Botas"), (LootHuntCategory.Gloves, "Luvas"), (LootHuntCategory.Bracelets, "Braceletes e orbes"),
        (LootHuntCategory.Amulets, "Amuletos"), (LootHuntCategory.Rings, "Anéis"), (LootHuntCategory.Other, "Outros itens")
    ];
    private static readonly (string Code, string Name)[] PotionGroups =
    [
        ("@HP", "Poções de vida"), ("@MP", "Poções de mana"), ("@RES", "Poções de estamina")
    ];
    private static readonly LootHuntCategory AllLootCategories = Categories.Aggregate((LootHuntCategory)0, (mask, value) => mask | value.Kind);

    private void LootCategories()
    {
        var card = Card("Categorias");
        var buttons = new List<(LootHuntCategory Kind, TextView Button)>();
        Note(card, "Itens e suprimentos");
        AddTiles(Categories.Where(value => !FreeHuntSettings.IsEquipment(value.Kind)).ToArray());
        Note(card, "Equipamentos");
        AddTiles(Categories.Where(value => FreeHuntSettings.IsEquipment(value.Kind)).ToArray());
        var actions = new LinearLayout(Activity);
        card.AddView(actions);
        Action(actions, "Marcar todas", () => SetExcluded(0));
        Action(actions, "Desmarcar todas", () => SetExcluded(AllLootCategories));
        Note(card, "Itens de missão têm proteção própria. Nunca pegar e as cotas de poções têm prioridade sobre Sempre pegar.");

        void SetExcluded(LootHuntCategory excluded)
        {
            Change(s => s with { Excluded = excluded, HasExpandedLootCategories = true });
            foreach (var (kind, button) in buttons)
            {
                button.Background = Plate((excluded & kind) == 0 ? Blue : null);
            }
        }

        void AddTiles((LootHuntCategory Kind, string Name)[] types)
        {
            const int columns = 3;
            for (var start = 0; start < types.Length; start += columns)
            {
                var row = new LinearLayout(Activity) { WeightSum = columns };
                card.AddView(row);
                for (var i = start; i < Math.Min(start + columns, types.Length); i++)
                {
                    var (kind, name) = types[i];
                    var button = Button(name, (Settings.Excluded & kind) == 0);
                    buttons.Add((kind, button));
                    button.Click += (_, _) => SetExcluded(Settings.Excluded ^ kind);
                    row.AddView(button, new LinearLayout.LayoutParams(0, Dp(45), 1) { RightMargin = Dp(4), BottomMargin = Dp(4) });
                }
            }
        }
    }

    private void LootList(string title, string terms, Action<string> save)
    {
        var card = Card(title);
        var chosen = (terms ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var shown = Column();
        card.AddView(shown);
        var groups = PotionGroups.Concat(Categories.Select(value => (Code: FreeHuntSettings.GroupKey(value.Kind), Name: value.Name))).ToArray();
        var groupPicker = Column();
        card.AddView(groupPicker);
        var search = Editor("", "Buscar item pelo nome ou código...", InputTypes.ClassText);
        card.AddView(search, new LinearLayout.LayoutParams(-1, Dp(40)));
        search.SetSelectAllOnFocus(false);
        var result = Column();
        itemCatalog ??= ReadItemCatalog();
        var category = (LootHuntCategory)0;
        var pageIndex = 0;
        Action(groupPicker, "Adicionar categoria inteira", () =>
        {
            using var dialog = new AlertDialog.Builder(Activity, Android.Resource.Style.ThemeMaterialDialogAlert);
            dialog.SetTitle("Adicionar categoria");
            dialog.SetItems(groups.Select(value => value.Name).ToArray(), (_, e) => Add(groups[e.Which].Code));
            dialog.SetNegativeButton("Fechar", (_, _) => { });
            dialog.Show();
        });
        Choice(card, "Buscar na categoria", ["Todas", .. Categories.Select(value => value.Name)], 0, selected =>
        {
            category = selected == 0 ? 0 : Categories[selected - 1].Kind;
            pageIndex = 0;
            Search();
        });
        card.AddView(result);

        void Add(string code)
        {
            if (!chosen.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                chosen.Add(code);
                save(string.Join(",", chosen));
                RenderSelected();
                Search();
            }
        }

        void RenderSelected()
        {
            shown.RemoveAllViews();
            foreach (var entry in chosen)
            {
                var lookup = entry.TrimStart('#');
                var match = Array.Find(itemCatalog, item => item.Code.Equals(lookup, StringComparison.OrdinalIgnoreCase));
                var group = Array.Find(groups, value => value.Code.Equals(entry, StringComparison.OrdinalIgnoreCase));
                var label = match.Name ?? group.Name ?? $"Nome ou código contém “{entry}”";
                LootOption(shown, $"{label}   ×", match.Code, () =>
                {
                    chosen.Remove(entry);
                    save(string.Join(",", chosen));
                    RenderSelected();
                    Search();
                });
            }
        }

        void Search()
        {
            result.RemoveAllViews();
            pendingLootIcons.RemoveAll(value => !value.Icon.IsAttachedToWindow);
            var query = search.Text?.Trim() ?? "";
            if (query.Length == 0 && category == 0)
            {
                Note(result, "Digite um nome ou escolha uma categoria para explorar o catálogo.");
                return;
            }

            const int pageSize = 6;
            var compare = CultureInfo.CurrentCulture.CompareInfo;
            var options = itemCatalog.Where(item => (category == 0 || item.Category == category)
                && (query.Length == 0 || item.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || compare.IndexOf(item.Name, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                && !chosen.Contains("#" + item.Code, StringComparer.OrdinalIgnoreCase)).ToArray();
            pageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, (options.Length - 1) / pageSize));
            foreach (var item in options.Skip(pageIndex * pageSize).Take(pageSize))
            {
                LootOption(result, $"{item.Name}  ·  {item.Code}", item.Code, () => Add("#" + item.Code));
            }
            if (options.Length > pageSize)
            {
                var move = new LinearLayout(Activity);
                result.AddView(move);
                if (pageIndex > 0)
                {
                    Action(move, "‹ Anteriores", () => { pageIndex--; Search(); });
                }
                if ((pageIndex + 1) * pageSize < options.Length)
                {
                    Action(move, "Mais resultados ›", () => { pageIndex++; Search(); });
                }
            }
            else if (options.Length == 0)
            {
                Note(result, "Nenhum item disponível para esta busca.");
            }
            if (query.Length > 0 && query[0] is not ('#' or '@') && !query.Contains(','))
            {
                Action(result, $"Adicionar regra com “{query}”", () => Add(query));
            }
        }

        search.TextChanged += (_, _) => { pageIndex = 0; Search(); };
        RenderSelected();
        Search();
    }

    private void LootOption(LinearLayout place, string label, string code, Action choose)
    {
        if (string.IsNullOrEmpty(code))
        {
            Action(place, label, choose);
            return;
        }

        var row = new LinearLayout(Activity);
        place.AddView(row);
        var icon = new ImageView(Activity);
        icon.SetScaleType(ImageView.ScaleType.FitCenter!);
        row.AddView(icon, new LinearLayout.LayoutParams(Dp(40), Dp(40)) { RightMargin = Dp(4) });
        if (icons.Get(code) is { } bitmap)
        {
            icon.SetImageBitmap(bitmap);
        }
        else
        {
            pendingLootIcons.Add((icon, code));
        }
        Action(row, label, choose);
    }

    private void RefreshLootIcons()
    {
        for (var i = pendingLootIcons.Count - 1; i >= 0; i--)
        {
            var (icon, code) = pendingLootIcons[i];
            if (!icon.IsAttachedToWindow)
            {
                pendingLootIcons.RemoveAt(i);
            }
            else if (icons.Get(code) is { } bitmap)
            {
                icon.SetImageBitmap(bitmap);
                pendingLootIcons.RemoveAt(i);
            }
        }
    }

    private LootCatalogItem[] ReadItemCatalog()
    {
        using var file = Activity.Assets!.Open("auto-hunt-items.tsv");
        using var text = new StreamReader(file);
        var items = new List<LootCatalogItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (text.ReadLine() is { } line)
        {
            var delimiter = line.IndexOf('\t');
            if (delimiter <= 0)
            {
                continue;
            }
            var key = line[..delimiter];
            if (ItemCodes.TryParse(key, out var code) && seen.Add(key))
            {
                var category = FreeHuntSettings.Category(key, code, code == ItemCodes.GoldCode);
                items.Add(new LootCatalogItem(key, line[(delimiter + 1)..], category));
            }
        }

        return items.ToArray();
    }

    private void ItemLevels()
    {
        var card = Card("Nível dos equipamentos");
        var kinds = Categories.Where(kind => FreeHuntSettings.IsEquipment(kind.Kind)).ToArray();
        var selected = 0;
        var bounds = Column();
        Choice(card, "Categoria", ["Todos", .. kinds.Select(value => value.Name)], selected, value =>
        {
            selected = value;
            BuildBounds();
        });
        card.AddView(bounds);
        BuildBounds();

        void BuildBounds()
        {
            bounds.RemoveAllViews();
            var global = new ItemLevelRange(Settings.MinimumItemLevel, Settings.MaximumItemLevel);
            var kind = selected == 0 ? default : kinds[selected - 1].Kind;
            var levels = selected == 0 ? global : Settings.LevelByCategory.GetValueOrDefault(kind, global);
            var row = new LinearLayout(Activity);
            bounds.AddView(row);
            var start = Column();
            var end = Column();
            row.AddView(start, new LinearLayout.LayoutParams(0, -2, 1) { RightMargin = Dp(6) });
            row.AddView(end, new LinearLayout.LayoutParams(0, -2, 1));
            Limit(start, "A partir do nível (≥)", levels.Minimum, min => SaveLimit(true, min));
            Limit(end, "Até o nível (≤)", levels.Maximum, max => SaveLimit(false, max));
            Note(bounds, "0 = sem limite. Sempre pegar passa por cima do nível e do spec.");
            if (selected != 0)
            {
                Action(bounds, "Usar faixa de Todos", () =>
                {
                    var ranges = new Dictionary<LootHuntCategory, ItemLevelRange>(Settings.LevelByCategory);
                    ranges.Remove(kind);
                    Change(s => s with { LevelByCategory = ranges });
                    BuildBounds();
                });
            }

            void SaveLimit(bool minimum, int value)
            {
                if (kind == 0)
                {
                    Change(s => minimum ? s with { MinimumItemLevel = value } : s with { MaximumItemLevel = value });
                    return;
                }

                Change(s =>
                {
                    var ranges = new Dictionary<LootHuntCategory, ItemLevelRange>(s.LevelByCategory);
                    var before = ranges.GetValueOrDefault(kind, global);
                    ranges[kind] = minimum ? before with { Minimum = value } : before with { Maximum = value };
                    return s with { LevelByCategory = ranges };
                });
            }
        }
    }

    private void PotionLoot(LinearLayout card, string name, PotionResource family)
    {
        Note(card, name);
        itemCatalog ??= ReadItemCatalog();
        var potions = itemCatalog.Where(item => PotionDefinition.FamilyOf(item.Code) == family).ToArray();
        Action(card, "Todas", () =>
        {
            Change(s => s with { ExcludedPotions = string.Join(",", (s.ExcludedPotions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(value => PotionDefinition.FamilyOf(value) != family)) });
            Show(tab);
        });
        for (var first = 0; first < potions.Length; first += 4)
        {
            var row = new LinearLayout(Activity);
            card.AddView(row);
            for (var index = first; index < Math.Min(first + 4, potions.Length); index++)
            {
                var (code, itemName, _) = potions[index];
                var collect = !FreeHuntSettings.Matches(code, Settings.ExcludedPotions);
                var button = Button(itemName, collect);
                row.AddView(button, new LinearLayout.LayoutParams(0, Dp(46), 1) { RightMargin = Dp(3), BottomMargin = Dp(3) });
                button.Click += (_, _) =>
                {
                    Change(s =>
                    {
                        var disabled = (s.ExcludedPotions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                        if (collect)
                        {
                            disabled.Add(code);
                        }
                        else
                        {
                            disabled.RemoveAll(value => value.Equals(code, StringComparison.OrdinalIgnoreCase));
                        }
                        return s with { ExcludedPotions = string.Join(",", disabled) };
                    });
                    collect = !collect;
                    button.Background = Plate(collect ? Blue : null);
                };
            }
        }
    }

    private void Limit(LinearLayout card, string label, int value, Action<int> set)
    {
        Note(card, label);
        var row = new LinearLayout(Activity);
        card.AddView(row);
        var input = Editor(value.ToString(), "", InputTypes.ClassNumber);
        Action(row, "−", () => Adjust(-1));
        row.AddView(input, new LinearLayout.LayoutParams(0, Dp(42), 2) { TopMargin = Dp(3), RightMargin = Dp(4) });
        Action(row, "+", () => Adjust(1));
        var jumps = new LinearLayout(Activity);
        card.AddView(jumps);
        Action(jumps, "Mín", () => input.Text = "0");
        Action(jumps, "−10", () => Adjust(-10));
        Action(jumps, "+10", () => Adjust(10));
        input.TextChanged += (_, _) => set(int.TryParse(input.Text, out var n) ? Math.Clamp(n, 0, 500) : 0);
        void Adjust(int by) => input.Text = Math.Clamp((int.TryParse(input.Text, out var n) ? n : 0) + by, 0, 500).ToString();
    }
}
