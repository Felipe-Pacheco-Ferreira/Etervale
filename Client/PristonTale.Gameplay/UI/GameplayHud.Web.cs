using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI;

public sealed partial class GameplayHud
{
    // News & Support, which the browser page draws as HTML.
    internal HudNews News => _news;

    // What waits on the News & Support shortcut: 1 for a news post not seen, plus each answered ticket not read.
    internal int NewsUnread => _news.Unread;

    // The players online for the browser page's counter: -1 while the server has not said or Settings hide it.
    internal int OnlinePlayers => _context.Presentation.Requested.HideOnlineCount ? -1 : Math.Max(-1, _context.Backend.OnlinePlayers);

    internal string WorldActions()
    {
        RefreshQuickActions();
        return string.Join(',', _quickActions.Select(action => action.Id));
    }

    internal bool RunWorldAction(string action)
    {
        if (ModalOpen)
        {
            return false;
        }

        if (RunTouchAction(action))
        {
            return true;
        }

        switch (action)
        {
            case "news":
                OpenNews();
                return true;
            case "auto-hunt":
                ToggleQuickHunt();
                return true;
            case "character":
                PressButton(HudMenuButton.Status);
                return true;
            case "inventory":
                PressButton(HudMenuButton.Inventory);
                return true;
            case "skills":
                PressButton(HudMenuButton.Skills);
                return true;
            case "quests":
                PressButton(HudMenuButton.Quest);
                return true;
            case "party":
                PressButton(HudMenuButton.Party);
                return true;
            case "lobby":
                LobbyRequested?.Invoke(false);
                return true;
        }

        RefreshQuickActions();
        var item = _quickActions.Find(entry => entry.Id == action);
        if (item is null)
        {
            return false;
        }

        item.Run();
        return true;
    }
}
