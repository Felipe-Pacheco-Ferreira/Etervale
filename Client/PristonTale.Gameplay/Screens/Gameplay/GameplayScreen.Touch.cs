using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.Input;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI;

namespace PristonTale.Gameplay.Screens.Gameplay;

// The browser's touch HUD in the world: the Android client's gestures (TouchOrbit: joystick, camera drag and pinch) over
// the page's fingers, taps selecting a target as on the phone, and the page's attack and skill buttons. A tap reaches
// the game as the page's mouse click, so the canvas windows take it too.
public sealed partial class GameplayScreen
{
    private TouchOrbit _touch;
    private TouchJoystick _joystick;

    // The joystick stops auto-hunt, as a click on the ground does.
    private bool TouchJoystickMoving => _touch is not null && _touch.Movement != Vector2.Zero;

    private bool TouchInterface => WebWorldHud.Fingers is not null && Context.Presentation.Requested.UsesTouch(WebWorldHud.TouchDevice);

    private void AttachTouch()
    {
        _hud.TouchAttackRequested += TouchAttack;
        _hud.TouchSkillRequested += TouchSkill;
    }

    private void UpdateTouch()
    {
        if (!TouchInterface)
        {
            if (_touch is not null)
            {
                _touch = null;
                _scene.TouchMovement = null;
                _scene.TouchCameraOffset = null;
                _scene.SelectCombatTargetOnTap = false;
            }
            return;
        }

        _touch ??= new TouchOrbit(WebWorldHud.Fingers);
        _touch.Update(allowJoystick: !_hud.HoldsPlayer);
        _scene.PointerCaptured |= _touch.JoystickActive || _touch.CameraActive;
        _scene.TouchMovement = _touch.Movement;
        _scene.TouchCameraOffset = _touch.Offset;
        _scene.SelectCombatTargetOnTap = true;
    }

    private void DrawTouch()
    {
        if (_touch is not { JoystickActive: true })
        {
            return;
        }

        _joystick ??= new TouchJoystick(Context.GraphicsDevice);
        Context.SpriteBatch.Begin(blendState: BlendState.AlphaBlend);
        _joystick.Draw(Context.SpriteBatch, _touch, Context.GraphicsDevice.Viewport);
        Context.SpriteBatch.End();
    }

    private void TouchAttack()
    {
        var target = TouchTarget();
        if (target != 0)
        {
            _scene.SetQuestHuntTarget(target, automatic: false);
        }
    }

    private void TouchSkill(uint code)
    {
        var target = TouchTarget();
        if (!_scene.CastSkillAt(code, target) && target == 0)
        {
            _hud.Chat.System(ClientText.Get("Skill.InvalidTarget"));
        }
    }

    // The living target the player selected, or else the nearest living monster, or 0.
    private ulong TouchTarget()
    {
        var selected = _scene.SelectedCombatTarget;
        if (selected != 0 && _scene.TrackQuestMonster(selected, out _))
        {
            return selected;
        }

        var target = 0UL;
        var nearest = float.PositiveInfinity;
        foreach (var entity in _huntMonsters.Keys)
        {
            if (_scene.TrackQuestMonster(entity, out var distance) && distance < nearest)
            {
                nearest = distance;
                target = entity;
            }
        }

        return target;
    }
}
