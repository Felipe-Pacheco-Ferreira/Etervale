using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

public sealed partial class GameplayHud
{
    private static readonly string[] GlobalBonusLabels = ["GlobalBonuses.ExpRate", "GlobalBonuses.ExpEvent", "GlobalBonuses.AgingCost", "GlobalBonuses.MixCost"];
    private bool _globalBonusesOpen;
    private long _nextBonusRefresh;
    private string[] _globalBonusValues = [];

    private void RefreshGlobalBonuses()
    {
        if (_globalBonusesOpen && Environment.TickCount64 >= _nextBonusRefresh)
        {
            _nextBonusRefresh = Environment.TickCount64 + 10000;
            _ = _chat.Send?.Invoke("/bonusglobais", false);
        }
    }

    private bool ReceiveGlobalBonuses(string text)
    {
        const string prefix = "GlobalBonus> ";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var values = text[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length == 4 && values.All(value => int.TryParse(value, out _)))
        {
            _globalBonusValues = values;
        }

        return true;
    }

    private static Rectangle GlobalBonusesBox(HudLayout layout) =>
        layout.PlaceOverlay(new Point((layout.Width - 384) / 2, 125), 384, 178);

    private bool GlobalBonusesCapture(HudLayout layout, Point pointer) =>
        _globalBonusesOpen && GlobalBonusesBox(layout).Contains(pointer);

    private bool CloseGlobalBonuses()
    {
        var wasOpen = _globalBonusesOpen;
        _globalBonusesOpen = false;
        return wasOpen;
    }

    private bool ClickGlobalBonuses(HudLayout layout, Point pointer)
    {
        if (!GlobalBonusesCapture(layout, pointer))
        {
            return false;
        }

        if (DropHistoryClose(GlobalBonusesBox(layout)).Contains(pointer))
        {
            CloseGlobalBonuses();
        }
        return true;
    }

    private void DrawGlobalBonuses(HudLayout layout)
    {
        if (!_globalBonusesOpen)
        {
            return;
        }

        var box = GlobalBonusesBox(layout);
        _painter.ColorBox(box, 155, 114, 52, 255);
        var interior = box;
        interior.Inflate(-1, -1);
        _painter.ColorBox(interior, 30, 23, 15, 250);
        _painter.Text(ClientText.Get("GlobalBonuses.Title"), box.X + 12, box.Y + 11, Color.Gold);
        _painter.Text("X", box.Right - 25, box.Y + 11, Color.Wheat);
        if (_globalBonusValues.Length == 0)
        {
            _painter.Text(ClientText.Get("GlobalBonuses.Contacting"), box.X + 12, box.Y + 50, Color.Wheat);
            return;
        }

        for (var index = 0; index < GlobalBonusLabels.Length; index++)
        {
            var value = _globalBonusValues[index] + (index == 0 ? "x" : "%");
            var y = box.Y + 44 + index * 26;
            _painter.Text(ClientText.Get(GlobalBonusLabels[index]), box.X + 12, y, Color.Wheat);
            _painter.TextCentered(value, box.Right - 102, box.Right - 10, y, Color.Gold);
        }
    }
}
