using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Input;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Tests;

internal static class TouchInputTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a finger landing at the lower left is the joystick and a still one there is a tap", JoystickAsync);
        yield return ("a finger dragged elsewhere turns the camera, two pinch its distance, and neither is a tap", CameraAsync);
        yield return ("touch skill buttons keep chosen learned skills and fill the rest with learned skills in order", SkillSlotsAsync);
        yield return ("the interface type picks the touch HUD on touch-only devices when automatic, round-trips, and needs the page's fingers",
            InterfaceModeAsync);
        yield return ("the touch HUD moves the minimap to the upper right corner, clear of the skill wheel, and leaves it in place otherwise", MinimapCornerAsync);
    }

    private static Task JoystickAsync()
    {
        var fingers = new Fingers();
        var orbit = new TouchOrbit(fingers);
        fingers.Set(new TouchFinger(1, .2f, .8f));
        orbit.Update();
        Assert.True(orbit.JoystickActive && orbit.Movement == Vector2.Zero, "a finger at the lower left did not take the joystick");
        fingers.Set(new TouchFinger(1, .3f, .8f));
        orbit.Update();
        Assert.True(orbit.Movement.X > .99f && MathF.Abs(orbit.Movement.Y) < .01f, "dragging the joystick right did not move right");
        fingers.Set();
        orbit.Update();
        Assert.True(!orbit.JoystickActive && orbit.Movement == Vector2.Zero && orbit.Tap is null, "lifting a moved joystick did not stop it");

        fingers.Set(new TouchFinger(2, .2f, .8f));
        orbit.Update();
        fingers.Set();
        orbit.Update();
        Assert.True(orbit.Tap == new Vector2(.2f, .8f), "a still finger in the joystick region was not a tap");

        fingers.Set(new TouchFinger(3, .2f, .8f));
        orbit.Update(allowJoystick: false);
        Assert.True(!orbit.JoystickActive, "the joystick was taken while it was not allowed");
        return Task.CompletedTask;
    }

    private static Task CameraAsync()
    {
        var fingers = new Fingers();
        var orbit = new TouchOrbit(fingers);
        var start = orbit.Offset;
        fingers.Set(new TouchFinger(1, .7f, .3f));
        orbit.Update();
        fingers.Set(new TouchFinger(1, .8f, .3f));
        orbit.Update();
        Assert.True(orbit.CameraActive && !orbit.JoystickActive, "a finger at the upper right did not turn the camera");
        Assert.True(Vector3.Distance(start, orbit.Offset) > 1f && MathF.Abs(start.Length() - orbit.Offset.Length()) < .01f,
            "a drag did not turn the camera around the player");
        fingers.Set();
        orbit.Update();
        Assert.True(orbit.Tap is null, "a drag was taken as a tap");

        var turned = orbit.Offset.Length();
        fingers.Set(new TouchFinger(4, .6f, .3f), new TouchFinger(5, .7f, .3f));
        orbit.Update();
        fingers.Set(new TouchFinger(4, .5f, .3f), new TouchFinger(5, .8f, .3f));
        orbit.Update();
        Assert.True(orbit.Offset.Length() < turned, "spreading two fingers did not bring the camera closer");
        fingers.Set();
        orbit.Update();
        Assert.True(orbit.Tap is null, "a pinch was taken as a tap");
        return Task.CompletedTask;
    }

    private static Task SkillSlotsAsync()
    {
        var usable = SkillRuleCatalog.All.Select(rule => rule.Code).Distinct().Take(5).ToArray();
        Assert.True(usable.Length == 5, "the skill catalog has fewer than five skills");
        OwnedSkillView[] skills = [new(usable[0], 1), new(usable[1], 0), new(usable[2], 1), new(usable[3], 1), new(usable[4], 1)];
        var codes = new uint[TouchSkillSlots.Count];
        var chosen = new bool[TouchSkillSlots.Count];
        codes[1] = usable[3];
        chosen[1] = true;
        codes[2] = usable[1];
        chosen[2] = true;
        TouchSkillSlots.Fill(skills, codes, chosen);
        Assert.True(codes[1] == usable[3] && chosen[1], "a chosen learned skill left its button");
        Assert.True(!chosen[2], "a chosen skill without a rank kept its button");
        Assert.True(codes[0] == usable[0] && codes[2] == usable[2] && codes[3] == usable[4],
            "the other buttons did not take the learned skills in order, skipping the chosen one");
        Assert.True(TouchSkillSlots.ButtonCaption("Golpe Duplo") == "Golpe" && TouchSkillSlots.ButtonCaption("Tornado") == "Tornado"
            && TouchSkillSlots.ButtonCaption("Avalanche") == "Avalan…" && TouchSkillSlots.ButtonCaption("") == "",
            "a skill button's caption is not the first word cut to seven characters");
        return Task.CompletedTask;
    }

    private static Task InterfaceModeAsync()
    {
        Assert.True(new ClientSettings().UsesTouch(true) && !new ClientSettings().UsesTouch(false), "automatic did not follow the device");
        Assert.True(new ClientSettings { InterfaceMode = InterfaceMode.Touch }.UsesTouch(false), "touch was not used where chosen");
        Assert.True(!new ClientSettings { InterfaceMode = InterfaceMode.Pc }.UsesTouch(true), "PC was not kept on a touch device");
        var parsed = ClientSettingsStore.Parse("{\"version\":1,\"interfaceMode\":\"Touch\"}", NullClientLog.Instance);
        Assert.True(parsed?.InterfaceMode == InterfaceMode.Touch, "the document's interface type was not read");
        var saved = ClientSettingsStore.Parse(ClientSettingsStore.Serialize(new ClientSettings { InterfaceMode = InterfaceMode.Pc }), NullClientLog.Instance);
        Assert.True(saved?.InterfaceMode == InterfaceMode.Pc, "the interface type did not round-trip");

        // The canvas leaves out what the page's touch HUD replaces only where the page reports fingers.
        var touch = new ClientSettings { InterfaceMode = InterfaceMode.Touch };
        var (fingers, device) = (WebWorldHud.Fingers, WebWorldHud.TouchDevice);
        try
        {
            WebWorldHud.Fingers = null;
            Assert.True(!WebWorldHud.TouchActive(touch), "the touch HUD was in use without the page's fingers");
            WebWorldHud.Fingers = new Fingers();
            WebWorldHud.TouchDevice = false;
            Assert.True(WebWorldHud.TouchActive(touch) && !WebWorldHud.TouchActive(new ClientSettings()), "the interface type did not decide the touch HUD");
            WebWorldHud.TouchDevice = true;
            Assert.True(WebWorldHud.TouchActive(new ClientSettings()), "automatic did not use the touch HUD on a touch-only device");
        }
        finally
        {
            (WebWorldHud.Fingers, WebWorldHud.TouchDevice) = (fingers, device);
        }

        return Task.CompletedTask;
    }

    private static Task MinimapCornerAsync()
    {
        var layout = HudLayout.Create(844, 390) with { Browser = true };
        var box = HudMinimap.Box(layout with { Touch = true });
        Assert.True(box.Right < layout.Width && box.Top > 0 && box.Bottom < layout.Height / 2 && box.Right > layout.Width - 20,
            "the touch HUD's minimap is not in the upper right corner");
        Assert.True(HudMinimap.Box(layout).Bottom > layout.Height / 2, "the minimap left the lower right corner without the touch HUD");
        return Task.CompletedTask;
    }

    private sealed class Fingers : ITouchFingers
    {
        private TouchFinger[] down = [];

        public int Count => down.Length;

        public void Set(params TouchFinger[] fingers) => down = fingers;

        public bool TryRead(int index, out TouchFinger finger)
        {
            finger = index < down.Length ? down[index] : default;
            return index < down.Length;
        }
    }
}
