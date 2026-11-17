using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

public sealed partial class GameplayHud
{
    private sealed record QuickAction(string Label, int Width, ulong Icon, Action Run);

    internal bool AutoHuntEnabled { get; set; }
    internal int AutoHuntQuestCode => _questView is { TargetQuestCode: > 0, KillsNeeded: > 0 } quest && quest.Kills < quest.KillsNeeded ? quest.TargetQuestCode : 0;
    private bool _quickMenuOpen;
    private bool _trackerCollapsed;
    private readonly List<QuickAction> _quickActions = [];

    private void RefreshQuickActions()
    {
        _quickActions.Clear();
        var portuguese = ClientText.Language == "pt-BR";
        var settings = _context.Presentation.Requested;
        var site = DesktopCommunityLinks.Parse(settings.NewsEndpoint);

        Link("Calendário", "Calendar", 110, 0x007E5A7E427E2400UL, site is null ? null : new Uri(site, "/eventos"));
        Link("Leilão", "Auction", 88, 0x000C1E3C18087E00UL, DesktopCommunityLinks.Parse(settings.AuctionUrl));
        Link("Segurança", "Security", 106, 0x003C343C243C1800UL, site is null ? null : new Uri(site, "/conta#seguranca"));
        Add("Últimos drops", "Recent drops", 124, 0x003C3C7E7E3C1800UL, () => _recentDropsOpen = true);
        Link("Histórico de Coins", "Coin history", 150, 0x003C42423C423C00UL, DesktopCommunityLinks.Parse(settings.CoinHistoryUrl));

        Add("Eventos", "Events", 110, 0x007E5A5A7E5A2400UL, OpenRewards);
        Link("Lar do Eragon", "Eragon", 132, 0x00247E5A5A423C00UL, DesktopCommunityLinks.Parse(settings.EragonUrl));
        Link("Ranking", "Rankings", 93, 0x003C18183C667E00UL, site is null ? null : new Uri(site, "/rankings"));
        Add("Distribuidor", "Item distributor", 125, 0x007E42665A427E00UL, () => _mail.Open(_chat, distributor: true));
        Link("Loja de Itens", "Item Shop", 116, 0x003C3C7E663C1800UL, DesktopCommunityLinks.Parse(settings.ItemShopUrl));
        Add("Bônus globais", "Global bonuses", 145, 0x000818387C3E1C00UL, () =>
        {
            _globalBonusesOpen = true;
            _nextBonusRefresh = 0;
        });
        Add("Correios", "Mail", 89, 0x007E42665A427E00UL, () => _mail.Open(_chat));
        Add("Lojas de jogadores", "Player shops", 145, 0x000818387C3E1C00UL, () => _shopDirectoryOpen = true);

        void Add(string pt, string en, int width, ulong icon, Action run) => _quickActions.Add(new QuickAction(portuguese ? pt : en, width, icon, run));

        void Link(string pt, string en, int width, ulong icon, Uri address)
        {
            if (address is not null)
            {
                Add(pt, en, width, icon, () => DesktopCommunityLinks.Open(address));
            }
        }
    }

    private static Rectangle NewsBounds(HudLayout layout) => new(Math.Max(4, layout.Width - 270), 12, 178, 25);
    private static Rectangle HuntBounds(HudLayout layout) => new(NewsBounds(layout).X, 40, 113, 25);
    private static Rectangle QuickMenuBounds(HudLayout layout) => new(NewsBounds(layout).X + 117, 40, 61, 25);
    private static int QuickRows(HudLayout layout) => Math.Max(1, (layout.Height - 137) / 27);
    private Rectangle QuickMenuEntry(HudLayout layout, int index)
    {
        var width = _quickActions[index].Width;
        var right = QuickMenuBounds(layout).Right - index / QuickRows(layout) * 174;
        return new Rectangle(right - width, 67 + index % QuickRows(layout) * 27, width, 24);
    }

    private int QuickMenuCount => _quickActions.Count;

    private Rectangle TrackerBounds(HudLayout layout) =>
        new(layout.Width - 230, 202 + (_quickMenuOpen ? Math.Min(QuickMenuCount, QuickRows(layout)) * 27 : 0), 214, _trackerCollapsed ? 24 : 64);

    private bool QuickMenuCaptures(HudLayout layout, Point pointer) =>
        NewsBounds(layout).Contains(pointer) || HuntBounds(layout).Contains(pointer) || QuickMenuBounds(layout).Contains(pointer) ||
        (_quickMenuOpen && QuickEntryAt(layout, pointer) >= 0) ||
        (HasTrackedHunt && TrackerBounds(layout).Contains(pointer));

    private bool HasTrackedHunt => !_context.Presentation.Requested.HideQuestTracker && _questView is { KillsNeeded: > 0 };

    private int QuickEntryAt(HudLayout layout, Point pointer)
    {
        for (var index = 0; index < QuickMenuCount; index++)
        {
            if (QuickMenuEntry(layout, index).Contains(pointer))
            {
                return index;
            }
        }

        return -1;
    }

    private bool ClickQuickMenu(HudLayout layout, Point pointer)
    {
        if (!QuickMenuCaptures(layout, pointer))
        {
            return false;
        }

        if (NewsBounds(layout).Contains(pointer))
        {
            OpenNews();
            _quickMenuOpen = false;
        }
        else if (HuntBounds(layout).Contains(pointer))
        {
            ToggleQuickHunt();
        }
        else if (QuickMenuBounds(layout).Contains(pointer))
        {
            _quickMenuOpen = !_quickMenuOpen;
            if (_quickMenuOpen)
            {
                RefreshQuickActions();
            }
        }
        else if (_quickMenuOpen && QuickEntryAt(layout, pointer) is >= 0 and var entry)
        {
            _quickMenuOpen = false;
            _quickActions[entry].Run();
        }
        else if (HasTrackedHunt && TrackerBounds(layout).Contains(pointer))
        {
            var bounds = TrackerBounds(layout);
            if (pointer.X >= bounds.Right - 28 && pointer.Y < bounds.Top + 24)
            {
                _trackerCollapsed = !_trackerCollapsed;
            }
            else
            {
                ToggleQuestWindow();
            }
        }

        return true;
    }

    private void ToggleQuickHunt()
    {
        AutoHuntEnabled = !AutoHuntEnabled && !InTown && AutoHuntQuestCode != 0;
        if (!AutoHuntEnabled && (InTown || AutoHuntQuestCode == 0))
        {
            _chat.Assist(ClientText.Language == "pt-BR" ? "A auto-caça precisa de uma missão de caça ativa fora da cidade." :
                "Auto-hunt requires an active hunt quest outside town.");
        }
    }

    private void DrawQuickIcon(int x, int y, ulong icon)
    {
        var gold = new Color(224, 181, 103);
        for (var row = 0; row < 8; row++)
        {
            var pixels = (byte)(icon >> (row * 8));
            for (var column = 0; column < 8; column++)
            {
                if ((pixels & (1 << (7 - column))) != 0)
                {
                    _painter.Fill(new Rectangle(x + column, y + row, 1, 1), gold);
                }
            }
        }
    }

    private void DrawQuickMenu(HudLayout layout, Point pointer)
    {
        var portuguese = ClientText.Language == "pt-BR";
        Plaque(NewsBounds(layout), portuguese ? "Novidades & Suporte" : "News & Support");
        var huntLabel = portuguese ? "Auto-caça" : "Auto-hunt";
        Plaque(HuntBounds(layout), huntLabel + (AutoHuntEnabled ? "  II" : "  >"));
        Plaque(QuickMenuBounds(layout), _quickMenuOpen ? (portuguese ? "Mais  ^" : "More  ^") : (portuguese ? "Mais  v" : "More  v"));
        if (_quickMenuOpen)
        {
            for (var index = 0; index < QuickMenuCount; index++)
            {
                var row = QuickMenuEntry(layout, index);
                Frame(row);
                DrawQuickIcon(row.Left + 5, row.Center.Y - 4, _quickActions[index].Icon);
                _painter.Text(_painter.Fit(_quickActions[index].Label, row.Width - 25), row.X + 20, row.Y + 5, Color.Wheat);
                if (_quickActions[index].Run == (Action)OpenRewards && _rewardBoard.Ready > 0)
                {
                    _painter.Disc(row.Right - 13, row.Center.Y, 8, new Color(137, 58, 44));
                    var count = Math.Min(_rewardBoard.Ready, 99).ToString();
                    _painter.TextCentered(count, row.Right - 21, row.Right - 5, row.Y + 5, Color.Wheat);
                }
            }
        }

        if (!HasTrackedHunt)
        {
            return;
        }

        var bounds = TrackerBounds(layout);
        Frame(bounds);
        _painter.Text(portuguese ? "Missões" : "Quests", bounds.X + 8, bounds.Y + 5, Color.Gold);
        Plaque(new Rectangle(bounds.Right - 24, bounds.Y + 3, 20, 17), _trackerCollapsed ? "+" : "-");
        if (!_trackerCollapsed)
        {
            var target = _questView.TargetName;
            var progress = $"{_questView.Kills}/{_questView.KillsNeeded}";
            _painter.Text(_painter.Fit(target ?? "", bounds.Width - 70), bounds.X + 8, bounds.Y + 24, Color.Wheat);
            _painter.TextCentered(progress, bounds.Right - 56, bounds.Right - 7, bounds.Y + 24, Color.Gold);
            var track = new Rectangle(bounds.X + 8, bounds.Bottom - 11, bounds.Width - 16, 4);
            var fraction = Math.Clamp((float)_questView.Kills / _questView.KillsNeeded, 0f, 1f);
            _painter.ColorBox(track, 78, 63, 41, 255);
            if (fraction > 0)
            {
                _painter.ColorBox(new Rectangle(track.X, track.Y, Math.Max(1, (int)(track.Width * fraction)), track.Height),
                    217, 175, 72, 255);
            }
        }

        void Plaque(Rectangle row, string title)
        {
            Frame(row);
            _painter.TextCentered(title, row.Left, row.Right, row.Top + (row.Height - 13) / 2, Color.Wheat);
        }

        void Frame(Rectangle row)
        {
            _painter.FramedBox(row, new Color(143, 106, 62), new Color(52, 40, 24), new Color(26, 19, 11), 3);
            var hover = row.Contains(pointer);
            row.Inflate(-1, -1);
            _painter.ColorBox(row, hover ? 78 : 19, hover ? 55 : 16, hover ? 28 : 12, 210);
            _painter.ColorBox(new Rectangle(row.X, row.Y, row.Width, 1), hover ? 154 : 98, hover ? 117 : 77, 41, 255);
        }
    }
}
