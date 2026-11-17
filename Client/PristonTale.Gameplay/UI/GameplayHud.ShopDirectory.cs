using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

public sealed partial class GameplayHud
{
    private bool _shopDirectoryOpen;

    private static Rectangle ShopDirectoryBox(HudLayout layout) =>
        layout.PlaceOverlay(new Point((layout.Width - 410) / 2, 120), 410, 284);

    private IEnumerable<KeyValuePair<ulong, string>> NearbyShops() =>
        ShopSigns.Where(entry => entry.Key != SelfId).OrderBy(entry => entry.Key).Take(8);

    private static Rectangle ShopDirectoryRow(Rectangle box, int index) =>
        new(box.X + 10, box.Y + 40 + index * 28, box.Width - 20, 25);

    private bool CloseShopDirectory()
    {
        var wasOpen = _shopDirectoryOpen;
        _shopDirectoryOpen = false;
        return wasOpen;
    }

    private bool ShopDirectoryCaptures(HudLayout layout, Point pointer) =>
        _shopDirectoryOpen && ShopDirectoryBox(layout).Contains(pointer);

    private bool ClickShopDirectory(HudLayout layout, Point pointer)
    {
        if (!ShopDirectoryCaptures(layout, pointer))
        {
            return false;
        }

        var box = ShopDirectoryBox(layout);
        if (DropHistoryClose(box).Contains(pointer))
        {
            return CloseShopDirectory();
        }

        var index = 0;
        foreach (var entry in NearbyShops())
        {
            if (ShopDirectoryRow(box, index++).Contains(pointer))
            {
                CloseShopDirectory();
                ViewPersonalShop(entry.Key);
                break;
            }
        }

        return true;
    }

    private void DrawShopDirectory(HudLayout layout)
    {
        if (!_shopDirectoryOpen)
        {
            return;
        }

        var box = ShopDirectoryBox(layout);
        var portuguese = ClientText.Language == "pt-BR";
        _painter.ColorBox(box, 155, 114, 52, 255);
        var interior = box;
        interior.Inflate(-1, -1);
        _painter.ColorBox(interior, 30, 23, 15, 250);
        _painter.Text(portuguese ? "Lojas de jogadores" : "Player shops", box.X + 12, box.Y + 11, Color.Gold);
        _painter.Text("X", box.Right - 25, box.Y + 11, Color.Wheat);
        var index = 0;
        foreach (var entry in NearbyShops())
        {
            var row = ShopDirectoryRow(box, index++);
            _painter.ColorBox(row, 64, 47, 23, 255);
            _painter.Text(_painter.Fit(entry.Value, row.Width - 14), row.X + 7, row.Y + 5, Color.Wheat);
        }

        if (index == 0)
        {
            _painter.Text(portuguese ? "Nenhuma loja avistada." : "No shops in view.", box.X + 12, box.Y + 44, Color.Wheat);
        }
    }
}
