using System.Globalization;
using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;
using PristonTale.Simulation.Social.Castle;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Spawning;

// The event calendar, a HUD window (VulkanPanel): Agora (what runs now, with a badge of how many, and what starts in the
// next 24 hours) and Semana (the next seven days by day) as tabs on the left; the cards scroll on the right. The server
// sends the calendar in UTC when the window opens (IPlayChannel.RequestEventCalendarAsync); the window shows it in the
// device's time zone and rebuilds its countdowns every RefreshMilliseconds while open. Read-only. UI thread only.
internal sealed class VulkanEventCalendar(Activity activity, FrameLayout overlay, VulkanWorldSession session)
{
    private const int RefreshMilliseconds = 30_000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly string[] WeekDayNames = ["dom.", "seg.", "ter.", "qua.", "qui.", "sex.", "sáb."];
    private static readonly Color Running = Color.Rgb(120, 220, 150);
    private static readonly Color Badge = Color.Rgb(200, 40, 40);

    private VulkanPanel _panel;
    private EventCalendar _calendar;
    private string _message;
    private bool _week;
    private bool _help;
    private LinearLayout _tabs;
    private LinearLayout _list;
    private ScrollView _scroll;

    public void Open()
    {
        if (_panel is not null)
        {
            return;
        }

        _week = false;
        _help = false;
        _calendar = null;
        _message = "Carregando…";
        _panel = new VulkanPanel(activity, overlay, "Calendário de eventos", 880, 400);
        _panel.Closed += () => _panel = null;
        var body = new LinearLayout(activity);
        _panel.Content.AddView(body, new LinearLayout.LayoutParams(-1, 0, 1));
        _tabs = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        body.AddView(_tabs, new LinearLayout.LayoutParams(Dp(150), -1) { RightMargin = Dp(10) });
        _list = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        _scroll = new ScrollView(activity) { FillViewport = true };
        _scroll.AddView(_list);
        body.AddView(_scroll, new LinearLayout.LayoutParams(0, -1, 1));
        var zone = VulkanPanel.Body(activity, $"Horários no seu fuso ({TimeZoneInfo.Local.Id})", 12, VulkanPanel.Muted);
        zone.Gravity = GravityFlags.Center;
        zone.SetPadding(0, Dp(5), 0, Dp(5));
        zone.Background = VulkanPanelArt.Inset(activity);
        _panel.Content.AddView(zone, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });
        Render();
        _ = LoadAsync(_panel);
        _scroll.PostDelayed(Tick, RefreshMilliseconds);
    }

    private async Task LoadAsync(VulkanPanel panel)
    {
        try
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            var calendar = await session.Play.RequestEventCalendarAsync(timeout.Token);
            if (_panel == panel)
            {
                (_calendar, _message) = (calendar, null);
                Render();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException or InvalidOperationException)
        {
            if (_panel == panel)
            {
                _message = "Não foi possível carregar o calendário.";
                Render();
            }
        }
    }

    private void Tick()
    {
        if (_panel is null)
        {
            return;
        }

        Render();
        _scroll.PostDelayed(Tick, RefreshMilliseconds);
    }

    private void Render()
    {
        var now = DateTimeOffset.UtcNow;
        List<CalendarOccurrence> running = _calendar is null ? [] : CalendarPages.Running(_calendar.Occurrences, now);
        RenderTabs(running.Count);
        _list.RemoveAllViews();
        if (_help)
        {
            Help();
        }
        else if (_calendar is null)
        {
            Note(_message);
        }
        else if (_week)
        {
            RenderWeek(now);
        }
        else
        {
            RenderNow(running, now);
        }
    }

    private void RenderTabs(int runningCount)
    {
        _tabs.RemoveAllViews();
        AddTab("Agora", !_week && !_help, runningCount, () => (_week, _help) = (false, false));
        AddTab("Semana", _week && !_help, 0, () => (_week, _help) = (true, false));
        _tabs.AddView(new View(activity), new LinearLayout.LayoutParams(-1, 0, 1));
        AddTab("?  Ajuda", _help, 0, () => _help = true);

        void AddTab(string name, bool selected, int badge, Action choose)
        {
            var holder = new FrameLayout(activity);
            var tab = VulkanPanelArt.Tab(activity, name, selected);
            tab.Click += (_, _) =>
            {
                choose();
                Render();
                _scroll.ScrollTo(0, 0);
            };
            holder.AddView(tab, new FrameLayout.LayoutParams(-1, Dp(44)) { TopMargin = Dp(6) });
            if (badge > 0)
            {
                var count = VulkanPanel.Text(activity, badge.ToString(CultureInfo.InvariantCulture), 11, Color.White, serif: false);
                count.Gravity = GravityFlags.Center;
                var dot = new GradientDrawable();
                dot.SetShape(ShapeType.Oval);
                dot.SetColor(Badge.ToArgb());
                dot.SetStroke(Dp(1), Color.White);
                count.Background = dot;
                holder.AddView(count, new FrameLayout.LayoutParams(Dp(22), Dp(22), GravityFlags.Top | GravityFlags.Right));
            }

            _tabs.AddView(holder, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(4) });
        }
    }

    private void RenderNow(List<CalendarOccurrence> running, DateTimeOffset now)
    {
        _list.AddView(VulkanPanelArt.Section(activity, "Acontecendo agora"));
        if (running.Count == 0)
        {
            Note("Nenhum evento agora.");
        }

        foreach (var occurrence in running)
        {
            AddCard(new CalendarEntry(occurrence.Kind, [occurrence]), now, listBosses: false);
        }

        _list.AddView(VulkanPanelArt.Section(activity, "Nas próximas 24 horas"), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });
        var coming = CalendarPages.Coming(_calendar.Occurrences, now, CalendarPages.ComingWindow);
        if (coming.Count == 0)
        {
            Note("Nada começa nas próximas 24 horas.");
        }

        foreach (var entry in coming)
        {
            AddCard(entry, now, listBosses: true);
        }
    }

    private void RenderWeek(DateTimeOffset now)
    {
        var days = CalendarPages.Days(_calendar.Occurrences, now, TimeZoneInfo.Local, CalendarPages.WeekDays);
        if (days.Count == 0)
        {
            Note("Nenhum evento marcado na semana.");
        }

        var today = CalendarPages.LocalDay(now, TimeZoneInfo.Local);
        for (var index = 0; index < days.Count; index++)
        {
            var heading = VulkanPanelArt.Section(activity, DayName(days[index].Day, today), size: 15);
            _list.AddView(heading, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(index == 0 ? 0 : 8) });
            foreach (var entry in days[index].Entries)
            {
                AddCard(entry, now, listBosses: false);
            }
        }
    }

    // A card: the event's icon and title, its countdown pill, when it runs, what it is, then the boss calls' times as
    // chips and (listBosses) who the first call brings.
    private void AddCard(CalendarEntry entry, DateTimeOffset now, bool listBosses)
    {
        var first = entry.First;
        var running = first.RunningAt(now);
        var card = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        card.SetPadding(Dp(12), Dp(9), Dp(12), Dp(10));
        card.Background = VulkanPanelArt.Inset(activity, lit: running);
        var top = new LinearLayout(activity);
        top.SetGravity(GravityFlags.CenterVertical);
        var icon = new ImageView(activity);
        icon.SetImageDrawable(new HudIcon(Glyph(entry.Kind), Dp(26)));
        icon.SetPadding(Dp(6), Dp(6), Dp(6), Dp(6));
        icon.Background = new VulkanPanelArt.WellDrawable(Dp(6), Dp(1), running ? Running : Color.Transparent, selected: false);
        top.AddView(icon, new LinearLayout.LayoutParams(Dp(40), Dp(40)) { RightMargin = Dp(10) });
        var titles = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        titles.AddView(VulkanPanel.Text(activity, Title(entry.Kind), 17, MobileHudTheme.Gold, serif: true));
        if (!first.Always && (entry.Kind != CalendarEventKind.FieldBosses || entry.Occurrences.Count == 1))
        {
            titles.AddView(VulkanPanel.Body(activity, When(first, now), 13, VulkanPanel.Muted), new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(2) });
        }

        top.AddView(titles, new LinearLayout.LayoutParams(0, -2, 1));
        var pill = VulkanPanelArt.Chip(activity, Countdown(first, now), running ? Running : VulkanHudArt.EdgeLight);
        pill.TextSize = 13;
        top.AddView(pill);
        card.AddView(top);
        if (entry.Kind == CalendarEventKind.FieldBosses && entry.Occurrences.Count > 1)
        {
            card.AddView(Times(entry.Occurrences), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        }

        Line(card, Describe(entry.Kind));
        foreach (var detail in Details(entry, listBosses))
        {
            Line(card, detail);
        }

        _list.AddView(card, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
    }

    // The boss calls' times as chips, wrapping to the card's width.
    private FlowLayout Times(IReadOnlyList<CalendarOccurrence> calls)
    {
        var flow = new FlowLayout(activity, Dp(5));
        foreach (var call in calls)
        {
            flow.AddView(VulkanPanelArt.Chip(activity, Clock(call), VulkanHudArt.EdgeLight));
        }

        return flow;
    }

    private void Line(LinearLayout card, string text)
    {
        var line = VulkanPanel.Body(activity, text, 13);
        card.AddView(line, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(5) });
    }

    private void Note(string text)
    {
        var note = VulkanPanel.Body(activity, text, 13, VulkanPanel.Muted);
        note.SetPadding(Dp(6), Dp(10), Dp(6), Dp(10));
        _list.AddView(note);
    }

    private void Help()
    {
        _list.AddView(VulkanPanelArt.Section(activity, "Sobre o calendário"));
        Line(_list, "Agora mostra os eventos em andamento e os que começam nas próximas 24 horas; Semana, os dos próximos sete dias.");
        Line(_list, "Os horários vêm do servidor e aparecem no fuso deste aparelho. As contagens se atualizam enquanto a janela está aberta.");
        Line(_list, $"Os chefes de campo nascem a cada hora marcada, no minuto do dia, sorteado a cada dia entre :{BossSpawnSchedule.FirstMinute:00} e "
            + $":{BossSpawnSchedule.LastMinute:00}; nos dias seguintes só a hora é conhecida.");
    }

    private IEnumerable<string> Details(CalendarEntry entry, bool listBosses)
    {
        switch (entry.Kind)
        {
            case CalendarEventKind.FieldBosses when listBosses:
                var bosses = _calendar.Bosses;
                for (var index = 0; index < bosses.Count && index < EventSchedule.MaxBosses; index++)
                {
                    if ((entry.First.Bosses & 1UL << index) != 0)
                    {
                        yield return $"{bosses[index].Name} — {bosses[index].FieldName}";
                    }
                }

                break;
            case CalendarEventKind.ExperienceEvent:
                yield return $"Cada monstro abatido dá {_calendar.ExperiencePercent}% da experiência.";
                break;
            case CalendarEventKind.DropEvents:
                foreach (var (drop, name) in DropNames)
                {
                    if ((_calendar.Drops & drop) != 0)
                    {
                        yield return name;
                    }
                }

                break;
        }
    }

    private static readonly (DropEvents Drop, string Name)[] DropNames =
    [
        (DropEvents.Mandoo, "Bolinhos de arroz"),
        (DropEvents.ValentineDay, "Chocolates do Dia dos Namorados"),
        (DropEvents.Watermelon, "Melancias"),
        (DropEvents.Valentine, "Presentes dos namorados"),
        (DropEvents.CandyDays, "Doces"),
        (DropEvents.WhiteDay, "Chocolates do White Day"),
        (DropEvents.Puzzle, "Peças de quebra-cabeça"),
        (DropEvents.GiftBox, "Caixas de presente")
    ];

    private static string Title(CalendarEventKind kind) => kind switch
    {
        CalendarEventKind.FieldBosses => "Chefes de campo",
        CalendarEventKind.Bellatra => "Bellatra",
        CalendarEventKind.CastleSiege => "Cerco ao Bless Castle",
        CalendarEventKind.CrystalEvent => "Evento dos cristais",
        CalendarEventKind.ExperienceEvent => "Evento de experiência",
        CalendarEventKind.DropEvents => "Drops de evento",
        _ => "Evento"
    };

    private static string Describe(CalendarEventKind kind) => kind switch
    {
        CalendarEventKind.FieldBosses => "Os chefes nascem no mapa de cada um, com seus guardas.",
        CalendarEventKind.Bellatra => $"Sobrevivência em equipe contra ondas de monstros, a partir do nível {BellatraRules.MinLevel}. Os bilhetes "
            + $"saem às {BellatraRules.TicketHour}h e {BellatraRules.TicketHour + 1}h do relógio do jogo.",
        CalendarEventKind.CastleSiege => $"Os clãs disputam o castelo por até {CastleRules.SiegeMinutes / 60} horas; quando a Torre de Valhala cai, o clã que mais a feriu fica com ele.",
        CalendarEventKind.CrystalEvent => "Os cristais de monstro chamam seus monstros de evento.",
        CalendarEventKind.ExperienceEvent => "Experiência extra em todos os mapas.",
        CalendarEventKind.DropEvents => "Os monstros deixam itens de evento:",
        _ => ""
    };

    private static HudGlyph Glyph(CalendarEventKind kind) => kind switch
    {
        CalendarEventKind.FieldBosses => HudGlyph.Attack,
        CalendarEventKind.Bellatra => HudGlyph.Skill,
        CalendarEventKind.CastleSiege => HudGlyph.Exit,
        CalendarEventKind.CrystalEvent => HudGlyph.Potion,
        CalendarEventKind.ExperienceEvent => HudGlyph.Run,
        CalendarEventKind.DropEvents => HudGlyph.Bag,
        _ => HudGlyph.Calendar
    };

    private static string Countdown(CalendarOccurrence occurrence, DateTimeOffset now)
    {
        if (occurrence.Always)
        {
            return "O tempo todo";
        }

        return occurrence.RunningAt(now) ? $"Termina em {Span(occurrence.End - now)}" : $"Começa em {Span(occurrence.Start - now)}";
    }

    private static string Span(TimeSpan left)
    {
        if (left.TotalDays >= 1)
        {
            return $"{(int)left.TotalDays}d {left.Hours}h";
        }

        return left.TotalHours >= 1 ? $"{left.Hours}h {left.Minutes}min" : $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}min";
    }

    // "Hoje · 18:00 – 19:00", the end's day named when it is another; a moment has no end.
    private static string When(CalendarOccurrence occurrence, DateTimeOffset now)
    {
        var today = CalendarPages.LocalDay(now, TimeZoneInfo.Local);
        var start = CalendarPages.LocalDay(occurrence.Start, TimeZoneInfo.Local);
        var text = $"{DayName(start, today)} · {Clock(occurrence)}";
        if (occurrence.End <= occurrence.Start)
        {
            return text;
        }

        var end = CalendarPages.LocalDay(occurrence.End, TimeZoneInfo.Local);
        var endClock = TimeZoneInfo.ConvertTime(occurrence.End, TimeZoneInfo.Local).ToString("HH:mm", CultureInfo.InvariantCulture);
        return end == start ? $"{text} – {endClock}" : $"{text} – {DayName(end, today)} {endClock}";
    }

    // A start's local time; a boss call whose minute is not drawn yet shows its hour alone.
    private static string Clock(CalendarOccurrence occurrence)
    {
        var local = TimeZoneInfo.ConvertTime(occurrence.Start, TimeZoneInfo.Local);
        return occurrence.MinuteUnknown ? local.ToString("HH'h'", CultureInfo.InvariantCulture) : local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static string DayName(DateOnly day, DateOnly today) =>
        day == today ? "Hoje"
        : day == today.AddDays(1) ? "Amanhã"
        : $"{WeekDayNames[(int)day.DayOfWeek]}, {day.ToString("dd/MM", CultureInfo.InvariantCulture)}";

    private int Dp(int value) => VulkanPanelArt.Dp(activity, value);

    // Children laid left to right, wrapping to a new row where the width runs out.
    private sealed class FlowLayout(Android.Content.Context context, int gap) : ViewGroup(context)
    {
        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
        {
            var width = MeasureSpec.GetSize(widthMeasureSpec);
            var (x, y, row) = (0, 0, 0);
            for (var index = 0; index < ChildCount; index++)
            {
                var child = GetChildAt(index);
                child.Measure(MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.AtMost), MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
                if (x > 0 && x + child.MeasuredWidth > width)
                {
                    (x, y, row) = (0, y + row + gap, 0);
                }

                x += child.MeasuredWidth + gap;
                row = Math.Max(row, child.MeasuredHeight);
            }

            SetMeasuredDimension(width, y + row);
        }

        protected override void OnLayout(bool changed, int l, int t, int r, int b)
        {
            var width = r - l;
            var (x, y, row) = (0, 0, 0);
            for (var index = 0; index < ChildCount; index++)
            {
                var child = GetChildAt(index);
                if (x > 0 && x + child.MeasuredWidth > width)
                {
                    (x, y, row) = (0, y + row + gap, 0);
                }

                child.Layout(x, y, x + child.MeasuredWidth, y + child.MeasuredHeight);
                x += child.MeasuredWidth + gap;
                row = Math.Max(row, child.MeasuredHeight);
            }
        }
    }
}
