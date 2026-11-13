using System.Text;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;
using PristonTale.Simulation.Quests;

// The quest journal, a HUD window (VulkanPanel): Em andamento and Concluídas as pill tabs; on the left a card for each
// entry (the running quest's steps newest first, or the finished quests) with its state and, on the current step, the
// hunt's count as a gauge; on the right the chosen entry: its objectives as a checklist (the steps so far, the hunt and
// the timer), its description from the quest board file and its rewards as gold chips, read from the description's
// "Recompensa" line. The current step has the button that starts or pauses the automatic quest. Read-only: the journal
// shows the server's QuestView. UI thread only.
internal sealed class VulkanQuestJournal(Activity activity, FrameLayout overlay, VulkanWorldSession session)
{
    private const string RewardMark = "Recompensa";

    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private QuestView _view;
    private VulkanPanel _panel;
    private LinearLayout _tabs;
    private LinearLayout _list;
    private ScrollView _listScroll;
    private LinearLayout _detail;
    private Action _startAutoHunt;
    private bool _running = true;
    private int _chosen = -1;

    public bool IsOpen => _panel is not null;

    public void Update(QuestView view)
    {
        // A new step or a quest finished moves the choice back to the current step or the newest quest.
        if (_view?.Progress.Count != view?.Progress.Count || _view?.Finished.Count != view?.Finished.Count)
        {
            _chosen = -1;
        }

        _view = view;
        if (_panel is not null)
        {
            Render();
        }
    }

    // startAutoHunt: what Ir does on the current step; without it, Ir turns the session's quest navigator on.
    public void Open(Action startAutoHunt = null)
    {
        if (_panel is not null)
        {
            return;
        }

        _startAutoHunt = startAutoHunt;
        _running = true;
        _chosen = -1;
        _panel = new VulkanPanel(activity, overlay, "Missões", 800, 380);
        _panel.Closed += () => _panel = null;
        _tabs = new LinearLayout(activity);
        _tabs.SetGravity(GravityFlags.CenterVertical);
        _panel.Content.AddView(_tabs, new LinearLayout.LayoutParams(-1, -2));

        var body = new LinearLayout(activity);
        _panel.Content.AddView(body, new LinearLayout.LayoutParams(-1, 0, 1) { TopMargin = Dp(8) });
        _list = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        _listScroll = new ScrollView(activity) { FillViewport = true, VerticalScrollBarEnabled = false };
        _listScroll.AddView(_list);
        body.AddView(_listScroll, new LinearLayout.LayoutParams(Dp(244), -1) { RightMargin = Dp(10) });
        _detail = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        _detail.SetPadding(Dp(14), Dp(10), Dp(14), Dp(10));
        _detail.Background = VulkanPanelArt.Inset(activity);
        body.AddView(_detail, new LinearLayout.LayoutParams(0, -1, 1));
        Render();
    }

    private void Render()
    {
        var view = _view;
        var entries = (_running ? view?.Progress : view?.Finished) ?? [];
        if (_chosen < 0 || _chosen >= entries.Count)
        {
            _chosen = _running ? entries.Count - 1 : 0;
        }

        RenderTabs(view);
        RenderList(entries, view);
        RenderDetail(entries, view);
    }

    private void RenderTabs(QuestView view)
    {
        _tabs.RemoveAllViews();
        AddTab("Em andamento", running: true, view?.Progress.Count ?? 0);
        AddTab("Concluídas", running: false, view?.Finished.Count ?? 0);
        _tabs.AddView(new View(activity), new LinearLayout.LayoutParams(0, 1, 1));
        if (session.Navigator is { Enabled: true })
        {
            _tabs.AddView(VulkanPanelArt.Chip(activity, "Auto-missão ativa", VulkanPanelArt.Done));
        }

        void AddTab(string name, bool running, int count)
        {
            var selected = running == _running;
            var tab = VulkanPanelArt.Tab(activity, count > 0 ? $"{name}  {count}" : name, selected);
            tab.Click += (_, _) =>
            {
                if (_running != running)
                {
                    _running = running;
                    _chosen = -1;
                    Render();
                    _listScroll.ScrollTo(0, 0);
                }
            };
            _tabs.AddView(tab, new LinearLayout.LayoutParams(-2, Dp(32)) { RightMargin = Dp(8) });
        }
    }

    // The cards: the current step first while running, the newest finished quest first otherwise.
    private void RenderList(IReadOnlyList<QuestBoardEntry> entries, QuestView view)
    {
        _list.RemoveAllViews();
        if (entries.Count == 0)
        {
            var empty = VulkanPanel.Body(activity, _running ? "Nenhuma missão em andamento." : "Nenhuma missão concluída ainda.", 13, VulkanPanel.Muted);
            empty.SetPadding(Dp(6), Dp(10), Dp(6), Dp(10));
            _list.AddView(empty);
            return;
        }

        for (var order = 0; order < entries.Count; order++)
        {
            var index = _running ? entries.Count - 1 - order : order;
            _list.AddView(Card(entries, index, view), new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(6) });
        }
    }

    private LinearLayout Card(IReadOnlyList<QuestBoardEntry> entries, int index, QuestView view)
    {
        var current = _running && index == entries.Count - 1;
        var chosen = index == _chosen;
        var card = new LinearLayout(activity) { Orientation = Orientation.Vertical, Clickable = true, ContentDescription = entries[index].Title };
        card.SetPadding(Dp(10), Dp(8), Dp(10), Dp(9));
        card.Background = VulkanPanelArt.Inset(activity, lit: chosen);
        card.Click += (_, _) =>
        {
            _chosen = index;
            Render();
        };

        var top = new LinearLayout(activity);
        top.SetGravity(GravityFlags.CenterVertical);
        var icon = new ImageView(activity);
        icon.SetImageDrawable(new HudIcon(current ? HudGlyph.Journal : HudGlyph.Run, Dp(18)));
        icon.Alpha = current || chosen ? 1f : .6f;
        top.AddView(icon, new LinearLayout.LayoutParams(Dp(18), Dp(18)) { RightMargin = Dp(8) });
        var title = VulkanPanel.Text(activity, entries[index].Title, 14, chosen || current ? MobileHudTheme.Gold : MobileHudTheme.Ink, serif: true);
        title.SetMaxLines(2);
        title.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        top.AddView(title, new LinearLayout.LayoutParams(0, -2, 1));
        card.AddView(top);

        var bottom = new LinearLayout(activity);
        bottom.SetGravity(GravityFlags.CenterVertical);
        var (state, color) = State(current);
        bottom.AddView(VulkanPanelArt.Chip(activity, state, color), new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(8) });
        if (current && view is { KillsNeeded: > 0 })
        {
            var done = Math.Min(view.Kills, view.KillsNeeded);
            var gauge = new VulkanGaugeView(activity);
            gauge.Set(VulkanHudArt.Life, done / (float)view.KillsNeeded, $"{done}/{view.KillsNeeded}");
            bottom.AddView(gauge, new LinearLayout.LayoutParams(0, Dp(14), 1));
        }

        card.AddView(bottom, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        return card;
    }

    private (string Text, Color Color) State(bool current) => _running
        ? current ? ("Em andamento", VulkanHudArt.EdgeLight) : ("Etapa concluída", VulkanPanelArt.Done)
        : ("Concluída", VulkanPanelArt.Done);

    // The chosen entry: title and state, then in a scroll the objectives, the description and the rewards, and the
    // automatic quest's button under them on the current step.
    private void RenderDetail(IReadOnlyList<QuestBoardEntry> entries, QuestView view)
    {
        _detail.RemoveAllViews();
        if (entries.Count == 0)
        {
            var hint = VulkanPanel.Body(activity, _running ? "Fale com os NPCs das cidades para receber missões." : "As missões que você concluir aparecem aqui.",
                14, VulkanPanel.Muted);
            hint.Gravity = GravityFlags.Center;
            _detail.AddView(hint, new LinearLayout.LayoutParams(-1, -1));
            return;
        }

        var entry = entries[_chosen];
        var current = _running && _chosen == entries.Count - 1;
        var header = new LinearLayout(activity);
        header.SetGravity(GravityFlags.CenterVertical);
        var title = VulkanPanel.Text(activity, entry.Title, 18, MobileHudTheme.Gold, serif: true);
        title.SetMaxLines(2);
        header.AddView(title, new LinearLayout.LayoutParams(0, -2, 1) { RightMargin = Dp(8) });
        var (state, color) = State(current);
        header.AddView(VulkanPanelArt.Chip(activity, state, color));
        _detail.AddView(header);

        var content = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(activity);
        scroll.AddView(content);
        _detail.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1) { TopMargin = Dp(8) });

        content.AddView(VulkanPanelArt.Section(activity, "Objetivos"));
        var objectives = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        objectives.SetPadding(Dp(4), Dp(4), 0, Dp(2));
        content.AddView(objectives);
        if (current)
        {
            for (var step = 0; step < entries.Count; step++)
            {
                Objective(objectives, entries[step].Title, done: step < entries.Count - 1);
            }

            if (view is { KillsNeeded: > 0 })
            {
                var done = Math.Min(view.Kills, view.KillsNeeded);
                var hunt = Objective(objectives, $"Derrote {view.TargetName}", done: done >= view.KillsNeeded);
                var gauge = new VulkanGaugeView(activity);
                gauge.Set(VulkanHudArt.Life, done / (float)view.KillsNeeded, $"{done}/{view.KillsNeeded}");
                hunt.AddView(gauge, new LinearLayout.LayoutParams(Dp(110), Dp(16)) { LeftMargin = Dp(8) });
            }

            if (view is { TimerMs: > 0 })
            {
                Objective(objectives, $"Tempo restante  {TimeSpan.FromMilliseconds(view.TimerMs):hh\\:mm\\:ss}", done: false);
            }
        }
        else
        {
            Objective(objectives, entry.Title, done: true);
        }

        var (description, rewards) = Describe(entry);
        content.AddView(VulkanPanelArt.Section(activity, "Descrição"), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(10) });
        var text = VulkanPanel.Body(activity, description, 14);
        text.SetLineSpacing(Dp(3), 1f);
        text.SetPadding(Dp(4), Dp(6), Dp(4), 0);
        content.AddView(text);
        if (rewards.Count > 0)
        {
            content.AddView(VulkanPanelArt.Section(activity, "Recompensas"), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(10) });
            var row = new LinearLayout(activity);
            row.SetPadding(Dp(4), Dp(6), 0, Dp(2));
            var shelf = new HorizontalScrollView(activity) { HorizontalScrollBarEnabled = false };
            shelf.AddView(row);
            content.AddView(shelf);
            foreach (var reward in rewards)
            {
                var chip = VulkanPanelArt.Chip(activity, reward, VulkanHudArt.EdgeLight);
                chip.TextSize = 12;
                row.AddView(chip, new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(6) });
            }
        }

        if (current && Go(view) is { } go)
        {
            _detail.AddView(go, new LinearLayout.LayoutParams(-1, Dp(42)) { TopMargin = Dp(8) });
        }
    }

    // One objective: a gold diamond while open, a green check once done, and its text.
    private LinearLayout Objective(LinearLayout parent, string text, bool done)
    {
        var row = new LinearLayout(activity);
        row.SetGravity(GravityFlags.CenterVertical);
        var mark = VulkanPanel.Text(activity, done ? "✓" : "◆", 14, done ? VulkanPanelArt.Done : VulkanHudArt.EdgeLight, serif: false);
        mark.Gravity = GravityFlags.Center;
        row.AddView(mark, new LinearLayout.LayoutParams(Dp(20), -2) { RightMargin = Dp(6) });
        var label = VulkanPanel.Body(activity, text, 14, done ? VulkanPanel.Muted : MobileHudTheme.Ink);
        row.AddView(label, new LinearLayout.LayoutParams(0, -2, 1));
        parent.AddView(row, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(5) });
        return row;
    }

    // Ir on the current step while the automatic quest has work (or the hunt has kills left): it starts the automatic
    // quest and closes the journal; while it runs, the button pauses it.
    private Button Go(QuestView view)
    {
        var navigator = session.Navigator;
        if (navigator is { Enabled: true })
        {
            var pause = MobileHudTheme.Button(activity, "Pausar auto-missão", prominent: true);
            MobileHudTheme.Icon(pause, HudGlyph.Auto);
            pause.Click += (_, _) =>
            {
                navigator.SetEnabled(false);
                Render();
            };
            return pause;
        }

        var hunting = _startAutoHunt is not null && view is { TargetQuestCode: > 0 } && view.Kills < view.KillsNeeded;
        if (navigator?.HasWork(view) != true && !hunting)
        {
            return null;
        }

        var go = MobileHudTheme.Button(activity, "Ir  ·  auto-missão", prominent: true);
        MobileHudTheme.Icon(go, HudGlyph.Auto);
        go.Click += (_, _) =>
        {
            if (_startAutoHunt is not null)
            {
                _startAutoHunt();
            }
            else
            {
                navigator?.SetEnabled(true);
            }

            _panel?.Close();
        };
        return go;
    }

    // An entry's description without its reward line, and the rewards that line lists ("Recompensa ...: a, b e c.").
    private (string Description, List<string> Rewards) Describe(QuestBoardEntry entry)
    {
        var path = entry.Description.Replace('\\', '/').ToLowerInvariant();
        if (!_texts.TryGetValue(path, out var raw))
        {
            raw = ReadText(path);
            _texts[path] = raw;
        }

        var rewards = new List<string>();
        var paragraphs = new List<string>();
        var paragraph = new StringBuilder();
        foreach (var source in raw.Replace("\r\n", "\n").Split('\n'))
        {
            var line = source.Trim();
            var mark = line.IndexOf(RewardMark, StringComparison.OrdinalIgnoreCase);
            var colon = mark >= 0 ? line.IndexOf(':', mark) : -1;
            if (colon > 0)
            {
                AddRewards(rewards, line[(colon + 1)..]);
                line = line[..mark].Trim();
            }

            // The original board files wrap lines mid-sentence; a blank line ends a paragraph.
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            paragraph.Append(paragraph.Length > 0 ? " " : "").Append(line);
        }

        Flush();
        return (paragraphs.Count > 0 ? string.Join("\n\n", paragraphs) : "Sem descrição.", rewards);

        void Flush()
        {
            if (paragraph.Length > 0)
            {
                paragraphs.Add(paragraph.ToString());
                paragraph.Clear();
            }
        }
    }

    private static void AddRewards(List<string> rewards, string list)
    {
        foreach (var part in list.Trim().TrimEnd('.').Split([", ", " e "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            rewards.Add(char.ToUpperInvariant(part[0]) + part[1..]);
        }
    }

    // The board files are Windows-1252 (the original ones) or UTF-8 with a byte order mark (Etervale's own).
    private string ReadText(string path)
    {
        try
        {
            using var stream = activity.Assets!.Open($"QuestJournal/{path}");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
        catch (Java.IO.IOException)
        {
            return "Descrição indisponível.";
        }
    }

    private int Dp(int value) => (int)MathF.Round(value * (activity.Resources?.DisplayMetrics?.Density ?? 1f));
}
