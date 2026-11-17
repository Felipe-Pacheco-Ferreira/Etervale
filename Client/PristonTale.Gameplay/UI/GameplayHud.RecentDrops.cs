using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

public sealed partial class GameplayHud
{
    private const int RecentDropCapacity = 10;
    private readonly Queue<(Guid Id, string Name, int Count)> _recentDrops = new();
    private bool _recentDropsOpen;

    private void ObserveRecentDrops(PresenceUpdate update)
    {
        foreach (var drop in update.GroundAdded)
        {
            if (drop.Gold || string.IsNullOrWhiteSpace(drop.Name) || _recentDrops.Any(entry => entry.Id == drop.Id))
            {
                continue;
            }

            if (_recentDrops.Count == RecentDropCapacity)
            {
                _recentDrops.Dequeue();
            }

            _recentDrops.Enqueue((drop.Id, drop.Name, drop.Amount));
        }
    }

    private bool CloseRecentDrops()
    {
        var wasOpen = _recentDropsOpen;
        _recentDropsOpen = false;
        return wasOpen;
    }

    private static Rectangle DropHistoryBox(HudLayout layout) =>
        layout.PlaceOverlay(new Point((layout.Width - 410) / 2, 120), 410, 288);

    private static Rectangle DropHistoryClose(Rectangle box) => new(box.Right - 32, box.Y + 7, 25, 24);

    private bool RecentDropsCapture(HudLayout layout, Point pointer) => _recentDropsOpen && DropHistoryBox(layout).Contains(pointer);

    private bool ClickRecentDrops(HudLayout layout, Point pointer)
    {
        if (!RecentDropsCapture(layout, pointer))
        {
            return false;
        }

        if (DropHistoryClose(DropHistoryBox(layout)).Contains(pointer))
        {
            CloseRecentDrops();
        }

        return true;
    }

    private void DrawRecentDrops(HudLayout layout)
    {
        if (!_recentDropsOpen)
        {
            return;
        }

        var box = DropHistoryBox(layout);
        var portuguese = ClientText.Language == "pt-BR";
        _painter.ColorBox(box, 156, 116, 53, 255);
        var interior = box;
        interior.Inflate(-1, -1);
        _painter.ColorBox(interior, 27, 21, 14, 248);
        _painter.ColorBox(new Rectangle(interior.X, interior.Y, interior.Width, 35), 57, 43, 22, 255);
        _painter.Text(portuguese ? "Últimos drops avistados" : "Recently seen drops", box.X + 12, box.Y + 11, Color.Gold);
        _painter.Text("X", box.Right - 25, box.Y + 11, Color.Wheat);
        if (_recentDrops.Count == 0)
        {
            _painter.Text(portuguese ? "Nenhum item avistado nesta sessão." : "No items seen this session.", box.X + 12, box.Y + 45, Color.Wheat);
            return;
        }

        var y = box.Y + 44;
        foreach (var entry in _recentDrops.Reverse())
        {
            _painter.Text(_painter.Fit(entry.Name, box.Width - 85), box.X + 12, y, Color.Wheat);
            _painter.TextCentered($"×{entry.Count}", box.Right - 65, box.Right - 10, y, Color.Gold);
            y += 22;
        }
    }
}
