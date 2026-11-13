using System.Globalization;
using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Mobile;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Events;
using PristonTale.Simulation.Items;

// Eventos & Recompensas, a HUD window (VulkanPanel): the tabs of the server's reward tracks on the left with a red badge
// of what each has ready, a strip with the time left and the progress, the track's entries (the welcome days as cards,
// the weekly and daily goals as rows, the level race as a timeline) and a footer to claim everything ready. The server
// pushes the window when it changes; while open the window asks for it every RefreshMilliseconds so the counts below
// their targets move too. UI thread only.
internal sealed class VulkanRewardsPanel(Activity activity, FrameLayout overlay, VulkanWorldSession session, VulkanItemIcons icons)
{
    private const int RefreshMilliseconds = 5000;
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Color Ready = Color.Rgb(255, 214, 96);
    private static readonly Color Locked = Color.Rgb(130, 130, 136);
    private static readonly Color Next = Color.Rgb(176, 150, 255);

    private VulkanPanel _panel;
    private RewardEventsInfo _info = RewardEventsInfo.None;
    private long _receivedAt;
    private RewardTrack _track = RewardTrack.Welcome;
    private bool _help;
    private bool _claiming;
    private LinearLayout _tabs;
    private LinearLayout _list;
    private ScrollView _scroll;
    private TextView _clock;
    private TextView _progressText;
    private VulkanGaugeView _progress;
    private TextView _medal;
    private TextView _ready;
    private TextView _note;
    private Button _claimAll;

    public void Open()
    {
        if (_panel is not null)
        {
            return;
        }

        _help = false;
        _panel = new VulkanPanel(activity, overlay, "Eventos & Recompensas", 900, 420);
        _panel.Closed += () => _panel = null;
        var help = new VulkanActionOrb(activity, HudGlyph.Journal) { ContentDescription = "Ajuda" };
        help.SetCaption("?");
        help.Click += (_, _) =>
        {
            _help = !_help;
            Render();
        };
        _panel.AddHeaderButton(help, 40);
        var body = new LinearLayout(activity);
        _panel.Content.AddView(body, new LinearLayout.LayoutParams(-1, 0, 1));
        _tabs = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        body.AddView(_tabs, new LinearLayout.LayoutParams(Dp(160), -1) { RightMargin = Dp(10) });
        var right = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        body.AddView(right, new LinearLayout.LayoutParams(0, -1, 1));
        right.AddView(Strip(), new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(6) });
        _list = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        _scroll = new ScrollView(activity) { FillViewport = true };
        _scroll.AddView(_list);
        right.AddView(_scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        right.AddView(Footer(), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        icons.Loaded += OnIconLoaded;
        _panel.Closed += () => icons.Loaded -= OnIconLoaded;
        Render();
        Ask(_panel);
    }

    // A new window from the server; called on the UI thread when the session holds another one.
    public void Update(RewardEventsInfo info)
    {
        _info = info ?? RewardEventsInfo.None;
        _receivedAt = Environment.TickCount64;
        _claiming = false;
        if (_panel is not null)
        {
            Render();
        }
    }

    public void Result(RewardEventsResultInfo result)
    {
        _claiming = false;
        if (!result.Message.IsNone)
        {
            Toast.MakeText(activity, ClientText.Format(result.Message), ToastLength.Short)?.Show();
        }

        if (_panel is not null)
        {
            Render();
        }
    }

    private void OnIconLoaded()
    {
        if (_panel is not null)
        {
            Render();
        }
    }

    // Asks for the window now and again while that panel stays open.
    private void Ask(VulkanPanel panel)
    {
        if (!ReferenceEquals(_panel, panel))
        {
            return;
        }

        Send(RewardEventsAction.Open, _track, 0);
        _scroll.PostDelayed(() => Ask(panel), RefreshMilliseconds);
    }

    private void Send(RewardEventsAction action, RewardTrack track, int index)
    {
        if (session.Play is not { } play)
        {
            return;
        }

        _claiming |= action != RewardEventsAction.Open;
        _ = SendAsync(play, action, track, index);
    }

    private async Task SendAsync(PristonTale.Client.Net.IPlayChannel play, RewardEventsAction action, RewardTrack track, int index)
    {
        try
        {
            await play.SendRewardEventsAsync(action, track, index);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            activity.RunOnUiThread(() =>
            {
                _claiming = false;
                Toast.MakeText(activity, "Não foi possível falar com o servidor.", ToastLength.Short)?.Show();
            });
        }
    }

    private void Render()
    {
        if (_info.Find(_track) is null && _info.Tracks.Count > 0)
        {
            _track = _info.Tracks[0].Track;
        }

        var track = _info.Find(_track);
        RenderTabs();
        RenderStrip(track);
        RenderFooter(track);
        _list.RemoveAllViews();
        if (_help)
        {
            Note(Help(_track));
        }
        else if (track is null)
        {
            Note("Carregando…");
        }
        else if (track.Track == RewardTrack.Welcome)
        {
            RenderWelcome(track);
        }
        else if (track.Track == RewardTrack.LevelRace)
        {
            RenderLevels(track);
        }
        else
        {
            RenderGoals(track);
        }
    }

    private void RenderTabs()
    {
        _tabs.RemoveAllViews();
        foreach (var track in _info.Tracks)
        {
            var holder = new FrameLayout(activity);
            var tab = VulkanPanelArt.Tab(activity, TabName(track.Track), track.Track == _track);
            tab.SetMaxLines(2);
            var chosen = track.Track;
            tab.Click += (_, _) =>
            {
                (_track, _help) = (chosen, false);
                Render();
                _scroll.ScrollTo(0, 0);
            };
            holder.AddView(tab, new FrameLayout.LayoutParams(-1, Dp(52)) { TopMargin = Dp(6), RightMargin = Dp(6) });
            if (track.Ready > 0)
            {
                var count = VulkanPanel.Text(activity, track.Ready.ToString(CultureInfo.InvariantCulture), 11, Color.White, serif: false);
                count.Gravity = GravityFlags.Center;
                var dot = new GradientDrawable();
                dot.SetShape(ShapeType.Oval);
                dot.SetColor(VulkanHudArt.BadgeRed.ToArgb());
                dot.SetStroke(Dp(1), Color.White);
                count.Background = dot;
                holder.AddView(count, new FrameLayout.LayoutParams(Dp(22), Dp(22), GravityFlags.Top | GravityFlags.Right));
            }

            _tabs.AddView(holder, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(4) });
        }
    }

    private LinearLayout Strip()
    {
        var strip = new LinearLayout(activity);
        strip.SetGravity(GravityFlags.CenterVertical);
        strip.SetPadding(Dp(8), Dp(6), Dp(12), Dp(6));
        strip.Background = VulkanPanelArt.Inset(activity);
        _clock = VulkanPanelArt.Chip(activity, "", MobileHudTheme.Gold);
        _clock.TextSize = 14;
        strip.AddView(_clock, new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(14) });
        var progress = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        var title = new LinearLayout(activity);
        title.AddView(VulkanPanel.Body(activity, "PROGRESSO", 11, VulkanPanel.Muted), new LinearLayout.LayoutParams(0, -2, 1));
        _progressText = VulkanPanel.Text(activity, "", 13, Color.White, serif: true);
        title.AddView(_progressText);
        progress.AddView(title);
        _progress = new VulkanGaugeView(activity);
        progress.AddView(_progress, new LinearLayout.LayoutParams(-1, Dp(10)) { TopMargin = Dp(3) });
        strip.AddView(progress, new LinearLayout.LayoutParams(Dp(220), -2));
        return strip;
    }

    private void RenderStrip(RewardTrackInfo track)
    {
        var reached = track?.Reached ?? 0;
        var total = track?.Entries.Count ?? 0;
        _progressText.Text = $"{reached} / {total}";
        _progress.Set(VulkanHudArt.Experience, total == 0 ? 0f : reached / (float)total, "");
        _clock.Text = track is null ? "…" : Clock(track);
    }

    private LinearLayout Footer()
    {
        var footer = new LinearLayout(activity);
        footer.SetGravity(GravityFlags.CenterVertical);
        footer.SetPadding(Dp(10), Dp(6), Dp(10), Dp(6));
        footer.Background = VulkanPanelArt.Inset(activity);
        _medal = VulkanPanel.Text(activity, "", 18, MobileHudTheme.Gold, serif: true);
        _medal.Gravity = GravityFlags.Center;
        var ring = new GradientDrawable();
        ring.SetShape(ShapeType.Oval);
        ring.SetStroke(Dp(2), MobileHudTheme.Gold);
        _medal.Background = ring;
        footer.AddView(_medal, new LinearLayout.LayoutParams(Dp(40), Dp(40)) { RightMargin = Dp(10) });
        var texts = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        _ready = VulkanPanel.Text(activity, "", 16, MobileHudTheme.Gold, serif: true);
        texts.AddView(_ready);
        _note = VulkanPanel.Body(activity, "", 12, VulkanPanel.Muted);
        texts.AddView(_note, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(2) });
        footer.AddView(texts, new LinearLayout.LayoutParams(0, -2, 1));
        _claimAll = MobileHudTheme.Button(activity, "RECEBER TUDO", prominent: true);
        MobileHudTheme.Icon(_claimAll, HudGlyph.Gift, stacked: false);
        _claimAll.Click += (_, _) => Send(RewardEventsAction.ClaimAll, _track, 0);
        footer.AddView(_claimAll, new LinearLayout.LayoutParams(Dp(190), Dp(48)) { LeftMargin = Dp(10) });
        return footer;
    }

    private void RenderFooter(RewardTrackInfo track)
    {
        var ready = track?.Ready ?? 0;
        _medal.Text = ready > 0 ? "★" : "!";
        _ready.Text = ready switch
        {
            0 => "Nada pronto para receber agora.",
            1 => "1 recompensa pronta",
            _ => $"{ready} recompensas prontas"
        };
        _note.Text = Footnote(_track, track?.MinLevel ?? 0);
        _claimAll.Enabled = ready > 0 && !_claiming;
        _claimAll.Alpha = _claimAll.Enabled ? 1f : .45f;
        VulkanAttention.Gold(_claimAll, _claimAll.Enabled, Dp(10));
    }

    // The seven days side by side; the seventh larger, the final reward.
    private void RenderWelcome(RewardTrackInfo track)
    {
        var row = new LinearLayout(activity);
        for (var index = 0; index < track.Entries.Count; index++)
        {
            var final = index == track.Entries.Count - 1;
            row.AddView(DayCard(track.Entries[index], index, final), new LinearLayout.LayoutParams(0, Dp(250), final ? 1.3f : 1f) { RightMargin = Dp(5) });
        }

        _list.AddView(row, new LinearLayout.LayoutParams(-1, -2));
    }

    private View DayCard(RewardEntryInfo entry, int index, bool final)
    {
        var card = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        card.SetGravity(GravityFlags.CenterHorizontal);
        card.SetPadding(Dp(4), Dp(8), Dp(4), Dp(8));
        card.Background = CardBackground(entry.State, final);
        card.Alpha = entry.State == RewardEntryState.Locked ? .6f : 1f;
        var day = VulkanPanel.Text(activity, $"{(final ? "★ " : "")}DIA {index + 1}", final ? 17 : 16,
            entry.State == RewardEntryState.Locked ? Locked : MobileHudTheme.Gold, serif: true);
        card.AddView(day);
        var item = entry.Items.Count > 0 ? entry.Items[0] : null;
        card.AddView(ItemWell(item, final ? 64 : 52, round: true, claimed: entry.State == RewardEntryState.Claimed),
            new LinearLayout.LayoutParams(Dp(final ? 64 : 52), Dp(final ? 64 : 52)) { TopMargin = Dp(8), BottomMargin = Dp(6) });
        if (final)
        {
            var label = VulkanPanel.Text(activity, "RECOMPENSA FINAL", 11, Ready, serif: false);
            label.Gravity = GravityFlags.Center;
            card.AddView(label);
        }

        var name = VulkanPanel.Body(activity, item?.Name ?? "", 12);
        name.Gravity = GravityFlags.Center;
        card.AddView(name, new LinearLayout.LayoutParams(-1, 0, 1));
        card.AddView(StateChip(entry.State, "Disponível"));
        if (entry.State == RewardEntryState.Ready)
        {
            card.Click += (_, _) =>
            {
                if (!_claiming)
                {
                    Send(RewardEventsAction.Claim, RewardTrack.Welcome, index);
                }
            };
        }

        return card;
    }

    // The weekly or daily goals as rows.
    private void RenderGoals(RewardTrackInfo track)
    {
        for (var index = 0; index < track.Entries.Count; index++)
        {
            var entry = track.Entries[index];
            var row = new LinearLayout(activity);
            row.SetGravity(GravityFlags.CenterVertical);
            var glyph = new ImageView(activity);
            glyph.SetImageDrawable(new HudIcon(GoalGlyph(entry.Goal), Dp(28)));
            glyph.SetPadding(Dp(10), Dp(10), Dp(10), Dp(10));
            glyph.Background = new VulkanPanelArt.WellDrawable(Dp(8), Dp(1), Color.Transparent, selected: false);
            row.AddView(glyph, new LinearLayout.LayoutParams(Dp(52), Dp(52)) { RightMargin = Dp(12) });
            row.AddView(Details(entry, GoalTitle(track.Track, entry.Goal), GoalText(entry.Goal, entry.Target), progress: true),
                new LinearLayout.LayoutParams(0, -2, 1));
            row.AddView(ClaimButton(track.Track, index, entry.State), new LinearLayout.LayoutParams(Dp(130), Dp(46)) { LeftMargin = Dp(10) });
            AddCard(row, entry.State);
        }
    }

    // The level race as a timeline: a diamond marker per level on a line, each beside its card.
    private void RenderLevels(RewardTrackInfo track)
    {
        for (var index = 0; index < track.Entries.Count; index++)
        {
            var entry = track.Entries[index];
            var row = new LinearLayout(activity);
            row.SetGravity(GravityFlags.CenterVertical);
            var marker = new FrameLayout(activity);
            var line = new View(activity);
            line.SetBackgroundColor(VulkanPanelArt.Bronze);
            marker.AddView(line, new FrameLayout.LayoutParams(Dp(2), -1, GravityFlags.CenterHorizontal));
            var diamond = VulkanPanel.Text(activity, $"NÍVEL\n{entry.Target}", 12, entry.State == RewardEntryState.Locked ? Locked : MobileHudTheme.Gold, serif: true);
            diamond.Gravity = GravityFlags.Center;
            diamond.Background = new DiamondDrawable(entry.State == RewardEntryState.Locked ? VulkanPanelArt.Bronze : Ready, Dp(2));
            marker.AddView(diamond, new FrameLayout.LayoutParams(Dp(70), Dp(70), GravityFlags.Center));
            row.AddView(marker, new LinearLayout.LayoutParams(Dp(80), -1) { RightMargin = Dp(8) });
            var card = new LinearLayout(activity);
            card.SetGravity(GravityFlags.CenterVertical);
            card.AddView(Details(entry, $"Alcance o nível {entry.Target}", "Recompensa única de cada personagem.", progress: false),
                new LinearLayout.LayoutParams(0, -2, 1));
            card.AddView(ClaimButton(track.Track, index, entry.State), new LinearLayout.LayoutParams(Dp(130), Dp(46)) { LeftMargin = Dp(10) });
            card.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
            card.Background = CardBackground(entry.State, final: false);
            row.AddView(card, new LinearLayout.LayoutParams(0, -2, 1));
            _list.AddView(row, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        }
    }

    // A goal's or level's title, text, progress (progress) and its rewards as chips.
    private LinearLayout Details(RewardEntryInfo entry, string title, string text, bool progress)
    {
        var details = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        details.AddView(VulkanPanel.Text(activity, title, 17, MobileHudTheme.Gold, serif: true));
        details.AddView(VulkanPanel.Body(activity, text, 13, VulkanPanel.Muted), new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(2) });
        if (progress)
        {
            var bar = new LinearLayout(activity);
            bar.SetGravity(GravityFlags.CenterVertical);
            var gauge = new VulkanGaugeView(activity);
            gauge.Set(VulkanHudArt.Experience, entry.Target == 0 ? 0f : Math.Min(1f, entry.Progress / (float)entry.Target), "");
            bar.AddView(gauge, new LinearLayout.LayoutParams(0, Dp(10), 1) { RightMargin = Dp(10) });
            bar.AddView(VulkanPanel.Text(activity, $"{Number(entry.Progress)} / {Number(entry.Target)}", 13, Color.White, serif: true));
            details.AddView(bar, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        }

        details.AddView(VulkanPanelArt.Section(activity, "RECOMPENSA", size: 11), new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        var chips = new LinearLayout(activity);
        foreach (var item in entry.Items)
        {
            chips.AddView(RewardChip(ItemWell(item, 34, round: false, claimed: false), item.Name), new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(6) });
        }

        if (entry.Gold > 0)
        {
            var coin = new ImageView(activity);
            coin.SetImageDrawable(new VulkanPanelArt.CoinDrawable(Dp(24)));
            coin.SetPadding(Dp(5), Dp(5), Dp(5), Dp(5));
            chips.AddView(RewardChip(coin, $"{Number(entry.Gold)} de ouro"), new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(6) });
        }

        var chipScroll = new HorizontalScrollView(activity) { HorizontalScrollBarEnabled = false };
        chipScroll.AddView(chips);
        details.AddView(chipScroll, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(4) });
        return details;
    }

    private LinearLayout RewardChip(View picture, string name)
    {
        var chip = new LinearLayout(activity);
        chip.SetGravity(GravityFlags.CenterVertical);
        chip.SetPadding(Dp(4), Dp(4), Dp(10), Dp(4));
        chip.Background = VulkanPanelArt.Inset(activity, lit: true);
        chip.AddView(picture, new LinearLayout.LayoutParams(Dp(34), Dp(34)) { RightMargin = Dp(6) });
        chip.AddView(VulkanPanel.Text(activity, name, 13, Color.White, serif: true));
        return chip;
    }

    // An item's sprite in a well with its count at the lower right; claimed, a green check over it.
    private FrameLayout ItemWell(RewardItemInfo item, int size, bool round, bool claimed)
    {
        var well = new FrameLayout(activity);
        if (round)
        {
            var disc = new GradientDrawable(GradientDrawable.Orientation.TopBottom!, [VulkanPanelArt.WellTop.ToArgb(), VulkanPanelArt.WellBottom.ToArgb()]);
            disc.SetShape(ShapeType.Oval);
            disc.SetStroke(Dp(1), VulkanPanelArt.Bronze);
            well.Background = disc;
        }
        else
        {
            well.Background = new VulkanPanelArt.WellDrawable(Dp(6), Dp(1), Color.Transparent, selected: false);
        }

        var picture = new ImageView(activity);
        picture.SetScaleType(ImageView.ScaleType.FitCenter);
        if (item is not null && icons.Get(ItemCodes.KeyOf(item.Code)) is { } icon)
        {
            picture.SetImageBitmap(icon);
        }

        var inset = Dp(size / 6);
        well.AddView(picture, new FrameLayout.LayoutParams(-1, -1) { LeftMargin = inset, TopMargin = inset, RightMargin = inset, BottomMargin = inset });
        if (item is { Count: > 1 })
        {
            var count = VulkanPanel.Text(activity, $"×{item.Count}", 12, Color.White, serif: false);
            well.AddView(count, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Bottom | GravityFlags.Right));
        }

        if (claimed)
        {
            var check = VulkanPanel.Text(activity, "✔", size / 2.4f, VulkanPanelArt.Done, serif: false);
            check.Gravity = GravityFlags.Center;
            var disc = new GradientDrawable();
            disc.SetShape(ShapeType.Oval);
            disc.SetColor(Color.Argb(170, 0, 0, 0).ToArgb());
            disc.SetStroke(Dp(2), VulkanPanelArt.Done);
            check.Background = disc;
            well.AddView(check, new FrameLayout.LayoutParams(-1, -1));
        }

        return well;
    }

    private View ClaimButton(RewardTrack track, int index, RewardEntryState state)
    {
        if (state != RewardEntryState.Ready)
        {
            var chip = StateChip(state, "Receber");
            chip.Gravity = GravityFlags.Center;
            return chip;
        }

        var claim = MobileHudTheme.Button(activity, "Receber", prominent: true);
        claim.Enabled = !_claiming;
        claim.Click += (_, _) => Send(RewardEventsAction.Claim, track, index);
        // Ready to claim: the running gold border.
        VulkanAttention.Gold(claim, true, Dp(10));
        return claim;
    }

    // The state at the bottom of a card: ready (with its word), next, locked or received.
    private TextView StateChip(RewardEntryState state, string readyWord)
    {
        var (text, color) = state switch
        {
            RewardEntryState.Ready => ($"★ {readyWord}", Ready),
            RewardEntryState.Next => ("🔒 Próximo", Next),
            RewardEntryState.Claimed => ("✔ Recebido", VulkanPanelArt.Done),
            _ => ("🔒 Bloqueado", Locked)
        };
        var chip = VulkanPanelArt.Chip(activity, text, color);
        chip.TextSize = 12;
        return chip;
    }

    private void AddCard(View content, RewardEntryState state)
    {
        content.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        content.Background = CardBackground(state, final: false);
        _list.AddView(content, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
    }

    // Ready cards lit gold, the next one with a dashed edge, the others dark; the final reward always gold-edged.
    private Drawable CardBackground(RewardEntryState state, bool final)
    {
        if (state == RewardEntryState.Next)
        {
            var dashed = new GradientDrawable(GradientDrawable.Orientation.TopBottom!,
                [Color.Argb(170, 22, 18, 34).ToArgb(), Color.Argb(170, 12, 12, 18).ToArgb()]);
            dashed.SetCornerRadius(Dp(8));
            dashed.SetStroke(Dp(2), Next, Dp(5), Dp(4));
            return dashed;
        }

        return VulkanPanelArt.Inset(activity, lit: state == RewardEntryState.Ready || final);
    }

    private void Note(string text)
    {
        var note = VulkanPanel.Body(activity, text, 14, VulkanPanel.Muted);
        note.SetPadding(Dp(6), Dp(12), Dp(6), Dp(12));
        _list.AddView(note);
    }

    // The tab's clock: the time left to the campaign's end or the next reset, counted down since the window arrived.
    private string Clock(RewardTrackInfo track)
    {
        if (track.SecondsLeft == RewardTrackView.NoDeadline)
        {
            return "∞  Sem prazo";
        }

        var left = TimeSpan.FromSeconds(track.SecondsLeft) - TimeSpan.FromMilliseconds(Environment.TickCount64 - _receivedAt);
        left = left < TimeSpan.Zero ? TimeSpan.Zero : left;
        var span = left.Days > 0 ? $"{left.Days}d {left.Hours}h" : left.Hours > 0 ? $"{left.Hours}h {left.Minutes}min" : $"{left.Minutes}min";
        return $"⏱  {(track.Track == RewardTrack.Welcome ? "Encerra" : "Reinicia")} em {span}";
    }

    private static string Number(long value) => value.ToString("N0", Portuguese);

    private static string TabName(RewardTrack track) => track switch
    {
        RewardTrack.Welcome => "7 Dias de Boas-vindas",
        RewardTrack.Weekly => "Caçada da Semana",
        RewardTrack.Daily => "Tarefas do Dia",
        _ => "Corrida de Nível"
    };

    private static string Footnote(RewardTrack track, int minLevel) => track switch
    {
        RewardTrack.Welcome => "Não precisa ser em dias seguidos: conta cada dia diferente em que o personagem entrou.",
        RewardTrack.Weekly => $"Reinicia toda segunda às 00:00. As tarefas contam a partir do nível {minLevel}.",
        RewardTrack.Daily => $"Reinicia todo dia às 00:00. As tarefas contam a partir do nível {minLevel}.",
        _ => "Cada nível alcançado dá a sua recompensa uma única vez."
    };

    private static string Help(RewardTrack track) => track switch
    {
        RewardTrack.Welcome => "Entre no jogo em dias diferentes durante a campanha: cada dia libera a recompensa seguinte. "
            + "Toque num dia disponível para recebê-lo ou use Receber tudo. O que não for recebido até o fim da campanha se perde.",
        RewardTrack.LevelRace => "Ao alcançar cada nível, a recompensa fica pronta para este personagem, sem prazo.",
        _ => "As tarefas contam o que você faz no mundo e reiniciam no horário do servidor. "
            + "Receba antes do reinício: o que não for recebido se perde. Itens precisam de espaço na bolsa."
    };

    private static string GoalTitle(RewardTrack track, RewardGoal goal) => goal switch
    {
        RewardGoal.MonsterKills => track == RewardTrack.Daily ? "Caçada diária" : "Caçador incansável",
        RewardGoal.BellatraEntries => "Coragem na Bellatra",
        RewardGoal.MonsterDrops => "Colecionador",
        RewardGoal.Sheltoms => "Coletor",
        RewardGoal.Potions => "Alquimista",
        _ => ""
    };

    private static string GoalText(RewardGoal goal, int target) => goal switch
    {
        RewardGoal.MonsterKills => $"Derrote {Number(target)} monstros em qualquer mapa.",
        RewardGoal.BellatraEntries => $"Entre na Bellatra {Number(target)} vezes.",
        RewardGoal.MonsterDrops => $"Recolha {Number(target)} itens deixados por monstros.",
        RewardGoal.Sheltoms => $"Colete {Number(target)} sheltoms de qualquer tipo.",
        RewardGoal.Potions => $"Beba {Number(target)} poções.",
        _ => ""
    };

    private static HudGlyph GoalGlyph(RewardGoal goal) => goal switch
    {
        RewardGoal.MonsterKills => HudGlyph.Attack,
        RewardGoal.BellatraEntries => HudGlyph.Skill,
        RewardGoal.Potions => HudGlyph.Potion,
        _ => HudGlyph.Bag
    };

    private int Dp(int value) => VulkanPanelArt.Dp(activity, value);

    // A level marker: a diamond outline in the tint on a dark fill.
    private sealed class DiamondDrawable(Color tint, float line) : VulkanPanelArt.TranslucentDrawable
    {
        private readonly Paint _paint = new() { AntiAlias = true };
        private readonly Android.Graphics.Path _shape = new();

        public override void Draw(Canvas canvas)
        {
            var (x, y) = (Bounds.ExactCenterX(), Bounds.ExactCenterY());
            var reach = Math.Min(Bounds.Width(), Bounds.Height()) / 2f - line;
            _shape.Reset();
            _shape.MoveTo(x, y - reach);
            _shape.LineTo(x + reach, y);
            _shape.LineTo(x, y + reach);
            _shape.LineTo(x - reach, y);
            _shape.Close();
            _paint.SetStyle(Paint.Style.Fill);
            _paint.Color = Color.Argb(235, 14, 14, 20);
            canvas.DrawPath(_shape, _paint);
            _paint.SetStyle(Paint.Style.Stroke);
            _paint.StrokeWidth = line;
            _paint.Color = tint;
            canvas.DrawPath(_shape, _paint);
        }
    }
}
