using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.Input;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.Screens.Gameplay;

// The browser's touch HUD in the world: the Android client's gestures (TouchOrbit: joystick, camera drag and pinch) over
// the page's fingers, taps selecting a target as on the phone, and the page's attack and skill buttons. A tap reaches
// the game as the page's mouse click, so the canvas windows take it too.
public sealed partial class GameplayScreen
{
    private TouchOrbit _touch;
    private TouchJoystick _joystick;
    private TouchPortraits _touchPortrait;

    // The touch HUD's two live portraits, made together and released together.
    private sealed class TouchPortraits(GraphicsDevice device) : IDisposable
    {
        public HudTargetPortrait Player { get; } = new(device);
        public HudTargetPortrait Target { get; } = new(device);

        public void Dispose()
        {
            Player.Dispose();
            Target.Dispose();
        }
    }

    // The joystick stops auto-hunt, as a click on the ground does.
    private bool TouchJoystickMoving => _touch is not null && _touch.Movement != Vector2.Zero;

    private bool TouchInterface => WebWorldHud.TouchActive(Context.Presentation.Requested);

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
                _hud.TouchTargetId = 0;
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
        // The target frame's target: the monster the player attacks, or else the one picked.
        _hud.TouchTargetId = _scene.AttackTargetId != 0 ? _scene.AttackTargetId : _scene.SelectedTargetId;
    }

    // The player's and the target's 3D heads for the page's frames, rendered before the world as the HUD's target
    // portrait is (binding another target discards the back buffer).
    private void RenderTouchPortrait()
    {
        if (_touch is null || _hud.HudHidden)
        {
            return;
        }

        _touchPortrait ??= new TouchPortraits(Context.GraphicsDevice);
        var layout = HudLayout.Of(Context.GraphicsDevice.Viewport);
        _touchPortrait.Player.Render(_scene, new HudPortraitSubject(_scene.SelfEntityId, 0, 0), layout);
        var target = _hud.TouchTargetId;
        _touchPortrait.Target.Render(_scene, _hud.Combat.TryGetPortraitSubject(target, out var subject) ? subject : default, layout);
    }

    private void DrawTouch()
    {
        if (_touch is not null && !_hud.HudHidden && _touchPortrait is { } portraits)
        {
            var viewport = Context.GraphicsDevice.Viewport;
            var head = portraits.Player.Texture(_scene.SelfEntityId);
            var target = portraits.Target.Texture(_hud.TouchTargetId);
            Context.SpriteBatch.Begin(blendState: BlendState.AlphaBlend);
            if (head is not null)
            {
                Context.SpriteBatch.Draw(head, TouchPortraitPlacement.Bounds(viewport.Width, viewport.Height), Color.White);
            }

            if (target is not null)
            {
                Context.SpriteBatch.Draw(target, TouchPortraitPlacement.TargetBounds(viewport.Width, viewport.Height), Color.White);
            }

            Context.SpriteBatch.End();
        }

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
