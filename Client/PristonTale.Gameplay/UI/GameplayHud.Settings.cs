using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Simulation.Quests;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI;

// What the settings add to the HUD: the whole-HUD switch for screenshots (F12), the persistent character information
// (Backspace), the minimap guide and the local player's life for the browser page's death and low-life effects.
public sealed partial class GameplayHud
{
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

        return _guidePoint is { } guide && guide.FieldCode == World.FieldCode ? new Vector3(guide.X, 0f, guide.Z) : null;
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
    }
}
