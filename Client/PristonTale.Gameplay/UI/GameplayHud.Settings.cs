using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Simulation.Quests;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI;

// What the settings add to the HUD: the whole-HUD switch for screenshots (F12), the persistent character information
// (Backspace), the dungeon camera (Shift+Z), the chat's place, the minimap guide and the local player's life for the browser page's death and low-life effects.
public sealed partial class GameplayHud
{
    // The server translates the chat: the settings show the "Translate chat to" row.
    public bool ChatTranslationAvailable
    {
        get => _settings.ChatTranslationAvailable;
        set => _settings.ChatTranslationAvailable = value;
    }

    // F12: nothing of the HUD is drawn until pressed again; not saved.
    public bool HudHidden { get; private set; }

    // The local player's life for the page: -1 before the owner state arrived, 0 while dead, else the life fraction.
    internal double PlayerLife => _state is not { MaxLife: > 0 } state ? -1 : PlayerDead || state.Life <= 0 ? 0 : Math.Min(1.0, (double)state.Life / state.MaxLife);

    private QuestView _guideView;
    private RoutePoint? _guidePoint;

    // The minimap guide's destination in the current field, worked out again only when the tracked quest changes.
    private Vector3? MinimapGuide()
    {
        if (!_context.Presentation.Requested.MinimapGuide)
        {
            return null;
        }

        if (!ReferenceEquals(_questView, _guideView))
        {
            _guideView = _questView;
            _guidePoint = TryJourneyDestination(_questView, _class, out var point) ? point : null;
        }

        return _guidePoint is { } guide && guide.FieldCode == World.FieldCode ? HudMinimap.RenderPoint(guide) : null;
    }

    // The chat's place (ChatOffsetX/Y), held on the screen; while moveChat is on, a drag moves it and the place is kept
    // when the button is let go.
    private void UpdateChatPlace(HudLayout layout, Point pointer, bool pressed)
    {
        var window = _chat.Window;
        var requested = _context.Presentation.Requested;
        window.Movable = requested.MoveChat;
        if (!window.Dragging)
        {
            window.Offset = HudChatWindow.ClampOffset(layout, new Point(requested.ChatOffsetX, requested.ChatOffsetY));
            return;
        }

        if (window.UpdateDrag(layout, pointer, pressed) && (window.Offset.X != requested.ChatOffsetX || window.Offset.Y != requested.ChatOffsetY))
        {
            _context.Presentation.Requested = requested with { ChatOffsetX = window.Offset.X, ChatOffsetY = window.Offset.Y };
        }
    }

    private void UpdateSettingsShortcuts(KeyboardState keyboard)
    {
        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.HideHudShortcut))
        {
            HudHidden = !HudHidden;
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.PersistentInfoShortcut))
        {
            var presentation = _context.Presentation;
            presentation.Requested = presentation.Requested with { PersistentCharacterInfo = !presentation.Requested.PersistentCharacterInfo };
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.DungeonCameraShortcut))
        {
            var presentation = _context.Presentation;
            presentation.Requested = HudSettingsPanel.WithDungeonCameraSwitched(presentation.Requested);
        }

        if (_settings.ShortcutPressed(keyboard, _previousKeyboard, HudSettingsPanel.PvpShortcut))
        {
            RequestPvpToggle();
        }
    }
}
