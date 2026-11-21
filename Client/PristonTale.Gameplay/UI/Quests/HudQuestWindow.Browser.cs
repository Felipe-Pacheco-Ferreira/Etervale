using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Events;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Quests;
public sealed partial class HudQuestWindow
{
    private const int BrowserRows = 7;
    private const float BrowserTextScale = 11f / HudPainter.BakePixels;
    private const float BrowserTitleScale = 14f / HudPainter.BakePixels;
    private static readonly string[] BrowserTabs = ["QuestWindow.Tab.Journey", "QuestWindow.Tab.Daily", "QuestWindow.Tab.Monthly", "QuestWindow.Tab.Completed"];
    private static readonly Color BrowserGold = new(219, 190, 112);
    private static readonly Color BrowserText = new(221, 213, 190);
    private RewardTrackInfo _daily;
    private int _dailySelection;
    private string _browserMessage = "";
    private bool _dailyPending;
    private int _playerLevel;
    private int _detailScroll;
    private int _detailContentHeight;
    private int _browserEntryCount;
    private bool _showHowTo;
    private readonly Dictionary<(string Language, string Text, int Width), string[]> _browserWrapped = new();
    private readonly Dictionary<(string Language, string Text, int Width, float Scale), string> _browserFitted = new();
    private readonly Dictionary<(string Language, string Title), string> _browserTitles = new();
    private string[] _dailyProgress = [];
    private string[] _dailyGold = [];
    private string[][] _dailyItems = [];
    private string _dailyLanguage = "";
    private string _dailyFooter = "";
    private (int First, int Count) _pagerState = (-1, -1);
    private string _pagerText = "";
    private (HudQuestPage Page, int Count, string Language) _captionState = (HudQuestPage.Running, -1, "");
    private string _journalCaption = "";
    private QuestView _objectiveView;
    private string _objectiveLanguage = "";
    private string _objectiveText = "";
    private string _descriptionLanguage = "";
    public bool TrackingEnabled { get; private set; } = true;
    public Action<bool> TrackingChanged { get; set; }
    public bool CanNavigate { get; set; }
    public Action NavigateRequested { get; set; }
    public Action DailyOpened { get; set; }
    public Action<int> ClaimDaily { get; set; }
    public Action ToggleAutoHunt { get; set; }
    public Action ToggleGuide { get; set; }
    public bool AutoHuntEnabled { get; set; }
    public bool GuideEnabled { get; set; }

    public void SetTracking(bool enabled) => TrackingEnabled = enabled;
    private void ToggleTracking()
    {
        TrackingEnabled = !TrackingEnabled;
        TrackingChanged?.Invoke(TrackingEnabled);
    }

    internal static Rectangle BrowserBounds(HudLayout layout) => layout.PlaceOverlay(new Point((layout.Width - 468) / 2, Math.Max(4, (layout.Height - 574) / 2)), 468, 496);
    internal static Rectangle BrowserTab(Rectangle box, int index) => new(box.X + 13 + index * 112, box.Y + 44, 107, 39);
    internal static Rectangle BrowserRow(Rectangle box, int index, HudQuestPage page = HudQuestPage.Running) => new(box.X + 19, box.Y + (page == HudQuestPage.Running ? 162 : page == HudQuestPage.Daily ? 143 : 116) + index * 37, 162, 34);
    internal static Rectangle BrowserClose(Rectangle box) => new(box.Right - 34, box.Top + 10, 19, 19);
    internal static Rectangle BrowserTrack(Rectangle box) => new(box.X + 322, box.Bottom - 48, 79, 27);
    internal static Rectangle BrowserGo(Rectangle box) => new(box.Right - 58, box.Bottom - 48, 34, 27);
    internal static Rectangle BrowserClaim(Rectangle box) => new(box.X + 322, box.Bottom - 48, 121, 27);
    private static Rectangle BrowserPrevious(Rectangle box) => new(box.X + 15, box.Bottom - 43, 28, 27);
    private static Rectangle BrowserNext(Rectangle box) => new(box.X + 151, box.Bottom - 43, 28, 27);
    internal static Rectangle BrowserSwitch(Rectangle box, int index, HudQuestPage page = HudQuestPage.Running) => new(box.X + (index == 1 ? 100 : 19), box.Y + (index == 2 && page == HudQuestPage.Running ? 121 : 101), index == 1 ? 78 : 82, 17);
    internal static Rectangle BrowserHowTo(Rectangle box) => new(box.Right - 100, box.Y + 100, 75, 19);
    private static Rectangle BrowserDetailClip(Rectangle box) => new(box.X + 197, box.Y + 151, box.Width - 222, box.Height - 212);
    public void SetDailyRewards(RewardTrackInfo daily, int level, bool pending, string message)
    {
        if (!ReferenceEquals(_daily, daily) || _dailyLanguage != ClientText.Language || _playerLevel != level)
        {
            _dailyLanguage = ClientText.Language;
            _dailyProgress = daily?.Entries.Select(entry => $"{entry.Progress} / {entry.Target}").ToArray() ?? [];
            _dailyGold = daily?.Entries.Select(entry => entry.Gold > 0 ? $"{entry.Gold:N0} Gold" : "").ToArray() ?? [];
            _dailyItems = daily?.Entries.Select(entry => entry.Items.Take(5).Select(item => $"{item.Count}x {item.Name}").ToArray()).ToArray() ?? [];
            _dailyFooter = daily is not null && level < daily.MinLevel ? ClientText.Format("QuestWindow.AvailableAtLevel", daily.MinLevel) : ClientText.Get("QuestWindow.ResetsDaily");
        }

        _daily = daily;
        _playerLevel = level;
        _dailyPending = pending;
        _browserMessage = message ?? "";
        _dailySelection = Math.Clamp(_dailySelection, 0, Math.Max(0, (daily?.Entries.Count ?? 0) - 1));
    }

    private bool ClickBrowser(HudLayout layout, Point pointer, QuestView view)
    {
        var box = BrowserBounds(layout);
        if (BrowserClose(box).Contains(pointer))
        {
            Close();
            return true;
        }

        for (var tab = 0; tab < 4; tab++)
        {
            if (!BrowserTab(box, tab).Contains(pointer))
            {
                continue;
            }

            Page = tab switch
            {
                0 => HudQuestPage.Running,
                1 => HudQuestPage.Daily,
                2 => HudQuestPage.Monthly,
                _ => HudQuestPage.Finished
            };
            _firstRow = 0;
            _selected = -1;
            _firstLine = 0;
            _detailScroll = 0;
            if (Page == HudQuestPage.Daily)
            {
                DailyOpened?.Invoke();
            }

            return true;
        }

        if (BrowserSwitch(box, 0, Page).Contains(pointer) && CanNavigate && Page == HudQuestPage.Running)
        {
            ToggleGuide?.Invoke();
        }
        else if (Page == HudQuestPage.Running && BrowserSwitch(box, 1, Page).Contains(pointer))
        {
            ToggleAutoHunt?.Invoke();
        }
        else if (Page is HudQuestPage.Running or HudQuestPage.Daily && BrowserSwitch(box, 2, Page).Contains(pointer))
        {
            ToggleTracking();
        }

        if (Page == HudQuestPage.Monthly)
        {
            return true;
        }

        var count = Page == HudQuestPage.Daily ? _daily?.Entries.Count ?? 0 : Entries(view, Page).Count;
        if (BrowserPrevious(box).Contains(pointer))
        {
            _firstRow = Math.Max(0, _firstRow - BrowserRows);
        }
        else if (BrowserNext(box).Contains(pointer) && _firstRow + BrowserRows < count)
        {
            _firstRow += BrowserRows;
        }
        else if (Page == HudQuestPage.Daily)
        {
            for (var row = 0; row < Math.Min(BrowserRows, count - _firstRow); row++)
            {
                if (BrowserRow(box, row, Page).Contains(pointer))
                {
                    _dailySelection = _firstRow + row;
                }
            }

            if (BrowserClaim(box).Contains(pointer) && DailyClaimable)
            {
                ClaimDaily?.Invoke(_dailySelection);
            }
        }
        else
        {
            var entries = Entries(view, Page);
            for (var row = 0; row < Math.Min(BrowserRows, count - _firstRow); row++)
            {
                if (BrowserRow(box, row, Page).Contains(pointer))
                {
                    SelectBrowserEntry(entries, _firstRow + row);
                }
            }

            if (Page == HudQuestPage.Running && entries.Count > 0)
            {
                if (BrowserTrack(box).Contains(pointer))
                {
                    ToggleTracking();
                }
                else if (BrowserGo(box).Contains(pointer) && CanNavigate)
                {
                    NavigateRequested?.Invoke();
                }
            }

        }
        if (BrowserHowTo(box).Contains(pointer))
        {
            _showHowTo = !_showHowTo;
            _detailScroll = 0;
        }

        return true;
    }

    public void Scroll(HudLayout layout, Point pointer, int delta)
    {
        if (!layout.Browser || delta == 0 || !Contains(layout, pointer))
        {
            return;
        }
        var box = BrowserBounds(layout);
        if (pointer.X >= box.X + 195)
        {
            _detailScroll = Math.Clamp(_detailScroll - Math.Sign(delta) * 30, 0, Math.Max(0, _detailContentHeight - BrowserDetailClip(box).Height));
        }
        else
        {
            _firstRow = Math.Clamp(_firstRow - Math.Sign(delta), 0, Math.Max(0, _browserEntryCount - BrowserRows));
        }
    }
    private bool DailyClaimable => !_dailyPending && _daily?.Entries.ElementAtOrDefault(_dailySelection)?.State == RewardEntryState.Ready;

    private void SelectBrowserEntry(IReadOnlyList<QuestBoardEntry> entries, int index)
    {
        _selected = index;
        _firstLine = 0;
            _detailScroll = 0;
        _description = HudQuestDocuments.BoardPath(entries[index].Description);
        _descriptionTitle = DataText.QuestTitle(entries[index].Title);
        _descriptionLanguage = ClientText.Language;
        _showHowTo = false;
    }

    private void DrawBrowser(HudPainter painter, HudLayout layout, SpriteFont font, QuestView view, HudQuestDocuments documents, Point pointer)
    {
        painter.Flush();
        var box = BrowserBounds(layout);
        painter.FramedBox(box, new Color(143, 119, 62), new Color(35, 30, 20), new Color(15, 14, 10), 3);
        painter.RoundedGradient(new Rectangle(box.X + 2, box.Y + 2, box.Width - 4, 34), new Color(56, 46, 27), new Color(31, 27, 18), 1);
        painter.Diamond(box.X + 16, box.Y + 18, 6, BrowserGold);
        DrawBrowserText(painter, ClientText.Get("QuickMenu.Quests"), box.X + 29, box.Y + 9, BrowserGold, BrowserTitleScale);
        var close = BrowserClose(box);
        painter.Disc(close.Center.X, close.Center.Y, 10, new Color(116, 103, 68));
        painter.Disc(close.Center.X, close.Center.Y, 9, close.Contains(pointer) ? new Color(65, 52, 29) : new Color(29, 25, 17));
        BrowserCentered(painter, "x", close.Left, close.Right, close.Top + 3, BrowserGold);
        var active = Page switch { HudQuestPage.Daily => 1, HudQuestPage.Monthly => 2, HudQuestPage.Finished => 3, _ => 0 };
        for (var tab = 0; tab < 4; tab++)
        {
            DrawBrowserTab(painter, BrowserTab(box, tab), ClientText.Get(BrowserTabs[tab]), pointer, tab == active, tab);
        }

        var list = new Rectangle(box.X + 12, box.Y + 96, 177, box.Height - 108);
        painter.FramedBox(list, new Color(81, 70, 43), new Color(17, 17, 12), 3);
        if (Page == HudQuestPage.Running && Entries(view, Page).Count > 0 || Page == HudQuestPage.Daily && _daily?.Entries.Count > 0)
        {
            painter.FramedBox(new Rectangle(box.X + 197, box.Bottom - 56, box.Width - 211, 44), new Color(79, 67, 41), new Color(25, 22, 15), 3);
        }
        if (Page == HudQuestPage.Running)
        {
            DrawSwitch(painter, BrowserSwitch(box, 0, Page), ClientText.Get("QuestWindow.AutoGuide"), GuideEnabled, CanNavigate);
            DrawSwitch(painter, BrowserSwitch(box, 1, Page), ClientText.Get("QuickMenu.AutoHunt"), AutoHuntEnabled);
            DrawSwitch(painter, BrowserSwitch(box, 2, Page), ClientText.Get("QuestWindow.Tracker"), TrackingEnabled);
        }
        else if (Page == HudQuestPage.Daily)
        {
            DrawSwitch(painter, BrowserSwitch(box, 2, Page), ClientText.Get("QuestWindow.Tracker"), TrackingEnabled);
        }

        if (Page == HudQuestPage.Monthly)
        {
            var lines = WrappedBrowser(font, ClientText.Get("QuestWindow.MonthlyNotStarted"), 150);
            for (var line = 0; line < lines.Length; line++)
            {
                BrowserCentered(painter, lines[line], list.Left + 5, list.Right - 5, box.Y + 120 + line * 15, BrowserText);
            }
            BrowserCentered(painter, ClientText.Get("QuestWindow.SelectQuest"), box.X + 197, box.Right - 21, box.Y + 109, BrowserText);
            return;
        }
        if (Page == HudQuestPage.Daily)
        {
            DrawDaily(painter, box, pointer);
        }
        else
        {
            DrawBrowserJournal(painter, box, font, view, documents, pointer);
        }

        _browserEntryCount = Page == HudQuestPage.Daily ? _daily?.Entries.Count ?? 0 : Entries(view, Page).Count;
        if (_browserEntryCount > BrowserRows)
        {
            Button(painter, BrowserPrevious(box), "<", pointer, enabled: _firstRow > 0);
            Button(painter, BrowserNext(box), ">", pointer, enabled: _firstRow + BrowserRows < _browserEntryCount);
            if (_pagerState != (_firstRow, _browserEntryCount))
            {
                _pagerState = (_firstRow, _browserEntryCount);
                _pagerText = $"{_firstRow + 1} / {_browserEntryCount}";
            }
            BrowserCentered(painter, _pagerText, box.X + 46, box.X + 148, box.Bottom - 37, BrowserGold);
        }
        DrawDetailScroll(painter, box);
    }

    private void DrawBrowserJournal(HudPainter painter, Rectangle box, SpriteFont font, QuestView view, HudQuestDocuments documents, Point pointer)
    {
        var entries = Entries(view, Page);
        if (_captionState != (Page, entries.Count, ClientText.Language))
        {
            _captionState = (Page, entries.Count, ClientText.Language);
            _journalCaption = ClientText.Format(Page == HudQuestPage.Finished ? "QuestWindow.CompletedCount" : "QuestWindow.InProgressCount", entries.Count);
        }
        if (Page == HudQuestPage.Running)
        {
            DrawBrowserText(painter, _journalCaption, box.X + 20, box.Y + 147, BrowserGold, 8f / HudPainter.BakePixels);
        }
        if (entries.Count == 0)
        {
            DrawBrowserText(painter, ClientText.Get("QuestWindow.NoQuests"), box.X + 22, box.Y + 177, new Color(150, 143, 121));
            BrowserCentered(painter, ClientText.Get("QuestWindow.SelectQuest"), box.X + 197, box.Right - 21, box.Y + 109, BrowserText);
            return;
        }
        if (_selected < 0 || _selected >= entries.Count || _description != HudQuestDocuments.BoardPath(entries[_selected].Description) || _descriptionLanguage != ClientText.Language)
        {
            SelectBrowserEntry(entries, Math.Clamp(_selected, 0, entries.Count - 1));
        }
        for (var row = 0; row < Math.Min(BrowserRows, entries.Count - _firstRow); row++)
        {
            var index = _firstRow + row;
            var bounds = BrowserRow(box, row, Page);
            if (index == _selected || bounds.Contains(pointer))
            {
                painter.FramedBox(bounds, new Color(170, 144, 73), new Color(47, 42, 23), 3);
            }
            painter.Disc(bounds.X + 16, bounds.Y + 16, 10, new Color(106, 93, 53));
            painter.Disc(bounds.X + 16, bounds.Y + 16, 9, new Color(42, 39, 24));
            painter.Stretch(HudQuestArt.Running, new Rectangle(bounds.X + 10, bounds.Y + 10, 12, 12));
            var key = (ClientText.Language, entries[index].Title);
            if (!_browserTitles.TryGetValue(key, out var title))
            {
                title = DataText.QuestTitle(entries[index].Title);
                _browserTitles[key] = title;
            }
            DrawBrowserText(painter, FitBrowser(painter, title, bounds.Width - 42), bounds.X + 33, bounds.Y + 7, BrowserGold);
            if (Page == HudQuestPage.Running && view is { KillsNeeded: > 0 })
            {
                ProgressBar(painter, new Rectangle(bounds.X + 33, bounds.Y + 23, 55, 4), view.Kills, view.KillsNeeded);
            }
        }

        DrawDetailHeading(painter, box, _descriptionTitle, Page == HudQuestPage.Finished ? ClientText.Get("QuestWindow.Completed") : ClientText.Get("QuestWindow.InProgress"), pointer);
        var clip = BrowserDetailClip(box);
        var x = box.X + 207;
        var source = documents.Wrapped(_description, font, 226, scale: BrowserTextScale);
        var text = source ?? WrappedBrowser(font, ClientText.Get("QuestWindow.Loading"), 226);
        var lines = _showHowTo ? text.Length : Math.Min(2, text.Length);
        var objective = HudQuestDocuments.AvalonObjective(_description);
        if (!ReferenceEquals(view, _objectiveView) || _objectiveLanguage != ClientText.Language)
        {
            _objectiveView = view;
            _objectiveLanguage = ClientText.Language;
            _objectiveText = view is { KillsNeeded: > 0 } ? $"{view.TargetName}   {view.Kills}/{view.KillsNeeded}" : objective;
        }
        var summary = new Rectangle(box.X + 197, box.Y + 151 - _detailScroll, 246, 77);
        ContentCard(painter, summary, clip, ClientText.Get("QuestWindow.Now"));
        var now = WrappedBrowser(font, Page == HudQuestPage.Finished ? ClientText.Get("QuestWindow.QuestCompleted") : _objectiveText, 222);
        for (var line = 0; line < Math.Min(2, now.Length); line++)
        {
            ContentText(painter, now[line], x, summary.Y + 25 + line * 14, BrowserText, clip);
        }
        ContentText(painter, CanNavigate ? ClientText.Get("QuestWindow.ClickGo") : ClientText.Get("QuestWindow.ReadInstructions"),
            x, summary.Y + 59, new Color(169, 161, 122), clip);
        var descriptionY = box.Y + 239 - _detailScroll;
        for (var line = 0; line < lines; line++)
        {
            ContentText(painter, text[line], x, descriptionY + line * 14, BrowserText, clip);
        }
        var objectiveY = box.Y + 239 + lines * 14 + 4 - _detailScroll;
        var objectives = new Rectangle(box.X + 197, objectiveY, 246, 103);
        ContentCard(painter, objectives, clip, ClientText.Get("QuestWindow.Objectives"));
        ContentText(painter, _objectiveText, x, objectives.Y + 35, BrowserText, clip);
        if (Page == HudQuestPage.Running && view is { KillsNeeded: > 0 })
        {
            ContentText(painter, view.Kills >= view.KillsNeeded ? ClientText.Get("QuestWindow.Done") : ClientText.Get("QuestWindow.InProgress"), x, objectives.Y + 57, BrowserGold, clip);
            var bar = new Rectangle(x, objectives.Bottom - 15, 221, 5);
            if (clip.Contains(bar))
            {
                ProgressBar(painter, bar, view.Kills, view.KillsNeeded);
            }
        }
        var reward = new Rectangle(box.X + 197, objectives.Bottom + 8, 246, 73);
        ContentCard(painter, reward, clip, ClientText.Get("QuestWindow.Reward"));
        ContentText(painter, HudQuestDocuments.AvalonReward(_description), x, reward.Y + 34, BrowserGold, clip);
        _detailContentHeight = reward.Bottom + _detailScroll - (box.Y + 151);
        if (Page == HudQuestPage.Running)
        {
            Button(painter, BrowserTrack(box), TrackingEnabled ? ClientText.Get("QuestWindow.Tracking") : ClientText.Get("QuestWindow.Track"), pointer);
            Button(painter, BrowserGo(box), ClientText.Get("QuestWindow.Go"), pointer, enabled: CanNavigate, positive: true);
        }
        if (_browserMessage.Length > 0)
        {
            DrawBrowserText(painter, FitBrowser(painter, _browserMessage, 110), box.X + 207, box.Bottom - 41, BrowserGold, 9f / HudPainter.BakePixels);
        }
    }

    private void DrawDaily(HudPainter painter, Rectangle box, Point pointer)
    {
        if (_daily is null)
        {
            DrawBrowserText(painter, ClientText.Get("QuestWindow.LoadingDaily"), box.X + 207, box.Y + 108, BrowserText);
            return;
        }
        for (var row = 0; row < Math.Min(BrowserRows, _daily.Entries.Count - _firstRow); row++)
        {
            var index = _firstRow + row;
            var bounds = BrowserRow(box, row, Page);
            if (index == _dailySelection || bounds.Contains(pointer))
            {
                painter.FramedBox(bounds, new Color(155, 133, 68), new Color(47, 42, 23), 3);
            }
            DrawBrowserText(painter, FitBrowser(painter, DailyTitle(_daily.Entries[index].Goal), bounds.Width - 12), bounds.X + 6, bounds.Y + 5, BrowserGold);
            DrawBrowserText(painter, _dailyProgress[index], bounds.X + 6, bounds.Y + 20, BrowserText, 9f / HudPainter.BakePixels);
        }
        var selected = _daily.Entries.ElementAtOrDefault(_dailySelection);
        if (selected is null)
        {
            return;
        }
        DrawDetailHeading(painter, box, DailyTitle(selected.Goal), ClientText.Get("QuestWindow.Daily"), pointer);
        var clip = BrowserDetailClip(box);
        var x = box.X + 207;
        var objective = new Rectangle(box.X + 197, box.Y + 151 - _detailScroll, 246, _showHowTo ? 138 : 100);
        ContentCard(painter, objective, clip, ClientText.Get("QuestWindow.Objectives"));
        ContentText(painter, DailyTitle(selected.Goal), x, objective.Y + 33, BrowserText, clip);
        ContentText(painter, _dailyProgress[_dailySelection], x, objective.Y + 54, BrowserGold, clip);
        if (_showHowTo)
        {
            ContentText(painter, ClientText.Get("QuestWindow.CompleteToClaim"), x, objective.Y + 78, BrowserText, clip);
            ContentText(painter, _dailyFooter, x, objective.Y + 95, BrowserText, clip);
        }
        var progress = new Rectangle(x, objective.Bottom - 13, 221, 5);
        if (clip.Contains(progress))
        {
            ProgressBar(painter, progress, selected.Progress, selected.Target);
        }
        var reward = new Rectangle(box.X + 197, objective.Bottom + 8, 246, 74 + _dailyItems[_dailySelection].Length * 17);
        ContentCard(painter, reward, clip, ClientText.Get("QuestWindow.Reward"));
        var y = reward.Y + 32;
        if (selected.Gold > 0)
        {
            ContentText(painter, _dailyGold[_dailySelection], x, y, BrowserGold, clip);
            y += 17;
        }
        foreach (var item in _dailyItems[_dailySelection])
        {
            ContentText(painter, item, x, y, BrowserText, clip);
            y += 17;
        }
        ContentText(painter, _dailyFooter, x, reward.Bottom - 20, BrowserGold, clip);
        _detailContentHeight = reward.Bottom + _detailScroll - (box.Y + 151);
        var claim = selected.State == RewardEntryState.Claimed ? ClientText.Get("QuestWindow.Claimed") : _dailyPending ? ClientText.Get("QuestWindow.Waiting") : ClientText.Get("QuestWindow.Claim");
        Button(painter, BrowserClaim(box), claim, pointer, enabled: DailyClaimable, positive: true);
        if (_browserMessage.Length > 0)
        {
            DrawBrowserText(painter, FitBrowser(painter, _browserMessage, 110), x, box.Bottom - 41, BrowserGold, 9f / HudPainter.BakePixels);
        }
    }

    private void DrawDetailHeading(HudPainter painter, Rectangle box, string title, string state, Point pointer)
    {
        painter.Disc(box.X + 210, box.Y + 109, 12, new Color(115, 98, 56));
        painter.Disc(box.X + 210, box.Y + 109, 11, new Color(50, 44, 27));
        painter.Stretch(HudQuestArt.Running, new Rectangle(box.X + 204, box.Y + 103, 12, 12));
        DrawBrowserText(painter, FitBrowser(painter, title, 138, BrowserTitleScale), box.X + 227, box.Y + 100, BrowserGold, BrowserTitleScale);
        Button(painter, BrowserHowTo(box), ClientText.Get("QuestWindow.HowTo"), pointer, selected: _showHowTo);
        var tag = new Rectangle(box.X + 197, box.Y + 127, 50, 18);
        painter.FramedBox(tag, new Color(115, 99, 58), new Color(51, 44, 24), 8);
        BrowserCentered(painter, Page == HudQuestPage.Daily ? ClientText.Get("QuestWindow.Daily") : "Tutorial", tag.Left, tag.Right, tag.Y + 4, BrowserGold);
        DrawBrowserText(painter, state, box.X + 255, box.Y + 132, new Color(167, 157, 126), 9f / HudPainter.BakePixels);
    }

    private void ContentText(HudPainter painter, string value, int x, int y, Color color, Rectangle clip)
    {
        if (y >= clip.Top && y + 14 <= clip.Bottom)
        {
            DrawBrowserText(painter, FitBrowser(painter, value, clip.Right - x - 8), x, y, color);
        }
    }

    private void ContentCard(HudPainter painter, Rectangle bounds, Rectangle clip, string title)
    {
        var visible = Rectangle.Intersect(bounds, clip);
        if (visible.Height <= 0)
        {
            return;
        }
        painter.FramedBox(visible, new Color(103, 86, 45), new Color(43, 36, 22), new Color(27, 24, 17), 4);
        ContentText(painter, title, bounds.X + 18, bounds.Y + 8, BrowserGold, clip);
        if (bounds.Y + 13 >= clip.Top && bounds.Y + 17 < clip.Bottom)
        {
            painter.Diamond(bounds.X + 10, bounds.Y + 13, 4, BrowserGold);
        }
        var rule = new Rectangle(bounds.X + 9, bounds.Y + 26, bounds.Width - 18, 1);
        if (clip.Contains(rule))
        {
            painter.Fill(rule, new Color(79, 68, 40));
        }
    }

    private static void ProgressBar(HudPainter painter, Rectangle box, int progress, int target)
    {
        painter.RoundedFill(box, new Color(23, 22, 15), 2);
        var width = target > 0 ? (int)(box.Width * Math.Clamp((float)progress / target, 0f, 1f)) : 0;
        if (width > 0)
        {
            painter.RoundedGradient(new Rectangle(box.X, box.Y, width, box.Height), new Color(237, 212, 132), new Color(178, 133, 54), 2);
        }
    }

    private void DrawDetailScroll(HudPainter painter, Rectangle box)
    {
        var clip = BrowserDetailClip(box);
        var maximum = Math.Max(0, _detailContentHeight - clip.Height);
        _detailScroll = Math.Clamp(_detailScroll, 0, maximum);
        if (maximum == 0)
        {
            return;
        }
        var track = new Rectangle(box.Right - 19, box.Y + 107, 5, box.Height - 198);
        painter.RoundedFill(track, new Color(47, 41, 27), 2);
        var thumb = Math.Max(18, track.Height * clip.Height / _detailContentHeight);
        var y = track.Y + (track.Height - thumb) * _detailScroll / maximum;
        painter.RoundedFill(new Rectangle(track.X, y, track.Width, thumb), new Color(154, 130, 67), 2);
    }
    internal static string DailyTitle(RewardGoal goal) => goal switch
    {
        RewardGoal.MonsterKills => ClientText.Get("QuestWindow.Goal.MonsterKills"),
        RewardGoal.Sheltoms => ClientText.Get("QuestWindow.Goal.Sheltoms"),
        RewardGoal.Potions => ClientText.Get("QuestWindow.Goal.Potions"),
        RewardGoal.MonsterDrops => ClientText.Get("QuestWindow.Goal.MonsterDrops"),
        RewardGoal.BellatraEntries => ClientText.Get("QuestWindow.Goal.BellatraEntries"),
        RewardGoal.Level => ClientText.Get("QuestWindow.Goal.Level"),
        _ => ClientText.Get("QuestWindow.Goal.Login")};
    private string[] WrappedBrowser(SpriteFont font, string text, int width)
    {
        if (!_browserWrapped.TryGetValue((ClientText.Language, text, width), out var lines))
        {
            lines = HudTextLayout.Wrap(font, text, width, BrowserTextScale);
            _browserWrapped[(ClientText.Language, text, width)] = lines;
        }

        return lines;
    }

    private string FitBrowser(HudPainter painter, string text, int width, float scale = BrowserTextScale)
    {
        if (!_browserFitted.TryGetValue((ClientText.Language, text, width, scale), out var fitted))
        {
            fitted = HudTextLayout.Fit(text, width, candidate => painter.MeasureAt(candidate, scale));
            _browserFitted[(ClientText.Language, text, width, scale)] = fitted;
        }

        return fitted;
    }

    private static void DrawBrowserText(HudPainter painter, string text, int x, int y, Color color, float scale = BrowserTextScale) => painter.Text(text, x, y, color, scale);
    private static void BrowserCentered(HudPainter painter, string text, int left, int right, int y, Color color) => painter.Text(text, left + (right - left - painter.MeasureAt(text, BrowserTextScale)) / 2, y, color, BrowserTextScale);
    private static void DrawBrowserTab(HudPainter painter, Rectangle box, string text, Point pointer, bool selected, int index)
    {
        painter.FramedBox(box, selected ? new Color(181, 153, 76) : new Color(81, 70, 43),
            selected || box.Contains(pointer) ? new Color(64, 53, 28) : new Color(31, 28, 20), new Color(20, 18, 13), 4);
        var x = box.X + 22;
        var y = box.Center.Y;
        painter.Disc(x, y, 12, new Color(123, 106, 62));
        painter.Disc(x, y, 11, new Color(40, 35, 22));
        if (index == 0)
        {
            painter.Fill(new Rectangle(x - 4, y - 6, 1, 12), BrowserGold);
            painter.Fill(new Rectangle(x - 3, y - 6, 7, 5), BrowserGold);
        }
        else if (index == 1)
        {
            painter.Disc(x, y, 6, BrowserGold);
            painter.Disc(x, y, 5, new Color(40, 35, 22));
            painter.Fill(new Rectangle(x, y - 4, 1, 5), BrowserGold);
            painter.Fill(new Rectangle(x, y, 4, 1), BrowserGold);
        }
        else if (index == 2)
        {
            painter.FramedBox(new Rectangle(x - 6, y - 5, 12, 11), BrowserGold, new Color(40, 35, 22), 1);
            painter.Fill(new Rectangle(x - 5, y - 2, 10, 1), BrowserGold);
            painter.Fill(new Rectangle(x - 3, y + 1, 2, 2), BrowserGold);
            painter.Fill(new Rectangle(x + 1, y + 1, 2, 2), BrowserGold);
        }
        else
        {
            for (var point = 0; point < 4; point++)
            {
                painter.Fill(new Rectangle(x - 5 + point, y + point - 1, 2, 2), BrowserGold);
                painter.Fill(new Rectangle(x - 1 + point, y + 2 - point * 2, 2, 2), BrowserGold);
            }
        }

        if (selected)
        {
            painter.ColorBox(new Rectangle(box.X + 5, box.Bottom - 2, box.Width - 10, 1), 207, 178, 101, 255);
            painter.DownTriangle(box.Center.X, box.Bottom + 3, 5, 4, BrowserGold);
        }

        DrawBrowserText(painter, text, box.X + 42, box.Y + 15, selected ? BrowserGold : new Color(169, 158, 127), 10f / HudPainter.BakePixels);
    }

    private static void DrawSwitch(HudPainter painter, Rectangle box, string label, bool enabled, bool available = true)
    {
        painter.FramedBox(box, new Color(130, 113, 63), new Color(29, 27, 19), 8);
        var toggle = new Rectangle(box.X + 4, box.Center.Y - 5, 17, 10);
        painter.RoundedFill(toggle, new Color(102, 92, 61), 5);
        toggle.Inflate(-1, -1);
        painter.RoundedFill(toggle, enabled ? new Color(63, 91, 47) : new Color(39, 39, 29), 4);
        painter.Disc(enabled ? toggle.Right - 4 : toggle.Left + 4, toggle.Center.Y, 3, available ? enabled ? new Color(212, 198, 136) : new Color(145, 141, 112) : new Color(89, 88, 73));
        DrawBrowserText(painter, label, box.X + 25, box.Y + 4, available ? BrowserText : new Color(126, 121, 102), 8f / HudPainter.BakePixels);
    }

    private static void Button(HudPainter painter, Rectangle bounds, string label, Point pointer, bool selected = false, bool enabled = true, bool positive = false)
    {
        var hover = enabled && bounds.Contains(pointer);
        painter.FramedBox(bounds, new Color(enabled ? 151 : 78, enabled ? 124 : 73, enabled ? 65 : 57), positive && enabled ? new Color(57, 104, 47) : selected || hover ? new Color(85, 66, 31) : new Color(42, 35, 23), new Color(23, 21, 16), 1);
        BrowserCentered(painter, label, bounds.Left + 3, bounds.Right - 3, bounds.Top + (bounds.Height - 14) / 2, enabled ? BrowserGold : new Color(132, 126, 109));
    }
}
