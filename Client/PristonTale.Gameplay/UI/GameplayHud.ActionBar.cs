using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI;

public sealed partial class GameplayHud
{
    private bool _actionBarVisible = !WebWorldHud.Enabled;

    private static Rectangle ActionSlot(HudLayout layout, int slot)
    {
        const int step = 45;
        var width = HudSkillBindings.ShortcutCount * step;
        var x = (layout.Width - width) / 2;
        var y = layout.Height - 120;
        if (layout.Browser)
        {
            var besideGauges = layout.BottomBarOffset + 523;
            if (!layout.SidePanelOpen && besideGauges + width <= HudMinimap.Box(layout).Left - 6)
            {
                x = besideGauges;
            }
            else if (layout.SidePanelOpen)
            {
                x = Math.Clamp(layout.BottomBarOffset + 310, 0, Math.Max(0, layout.Width - width));
                y = layout.Height - 232;
            }
            else
            {
                y = layout.Height - 226;
            }
        }

        return new Rectangle(x + slot * step, y, step - 1, 44);
    }

    private bool ActionBarCaptures(HudLayout layout, Point pointer) =>
        _actionBarVisible && !_systemMenu && !_settingsOpen && !ModalOpen &&
        Enumerable.Range(0, HudSkillBindings.ShortcutCount).Any(slot => ActionSlot(layout, slot).Contains(pointer));

    private bool ClickActionBar(HudLayout layout, Point pointer)
    {
        if (!ActionBarCaptures(layout, pointer))
        {
            return false;
        }

        for (var slot = 0; slot < HudSkillBindings.ShortcutCount; slot++)
        {
            if (ActionSlot(layout, slot).Contains(pointer))
            {
                SelectActionSlot(slot);
                return true;
            }
        }

        return false;
    }

    private void SelectActionSlot(int slot)
    {
        var skill = _bindings.Shortcut(slot);
        if (skill.SkillId != 0)
        {
            _bindings.Select(skill.SkillId, skill.Side);
        }
    }

    private void DrawActionBar(HudLayout layout)
    {
        if (!_actionBarVisible || _systemMenu)
        {
            return;
        }

        for (var slot = 0; slot < HudSkillBindings.ShortcutCount; slot++)
        {
            var bounds = ActionSlot(layout, slot);
            _painter.ColorBox(bounds, 151, 113, 52, 255);
            var interior = bounds;
            interior.Inflate(-1, -1);
            _painter.ColorBox(interior, 30, 23, 15, 238);
            _skillHud.DrawActionSlot(_painter, bounds, _bindings.Shortcut(slot).SkillId, Cooldowns, SkillClockMs);
            _painter.Text(_painter.Fit(_settings.ShortcutLabel(HudSettingsPanel.FirstActionBarShortcut + slot), 40),
                bounds.X + 3, bounds.Bottom - 14, Color.Wheat);
        }
    }
}
