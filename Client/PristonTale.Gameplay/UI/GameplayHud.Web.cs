using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI;

public sealed partial class GameplayHud
{
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
