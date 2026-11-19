using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.Input;

// Draws the touch joystick where its finger landed while it is held: a rim, a dark well and the knob.
public sealed class TouchJoystick : IDisposable
{
    private const int CircleSize = 64;
    private readonly Texture2D circle;

    public TouchJoystick(GraphicsDevice device)
    {
        circle = new Texture2D(device, CircleSize, CircleSize);
        var pixels = new Color[CircleSize * CircleSize];
        for (var y = 0; y < CircleSize; y++)
        {
            for (var x = 0; x < CircleSize; x++)
            {
                var distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(CircleSize / 2f));
                pixels[y * CircleSize + x] = Color.White * Math.Clamp(CircleSize / 2f - distance, 0f, 1f);
            }
        }
        circle.SetData(pixels);
    }

    // Draws into a batch the caller has begun.
    public void Draw(SpriteBatch batch, TouchOrbit orbit, Viewport viewport)
    {
        if (!orbit.JoystickActive)
        {
            return;
        }

        var center = orbit.JoystickOrigin * new Vector2(viewport.Width, viewport.Height);
        var radius = viewport.Height * .095f;
        Disc(batch, center, radius, new Color(193, 204, 212) * .62f);
        Disc(batch, center, radius * .94f, new Color(19, 25, 33) * .75f);
        Disc(batch, center + orbit.Movement * (radius * .65f), radius * .31f, new Color(222, 206, 162) * .86f);
    }

    public void Dispose() => circle.Dispose();

    private void Disc(SpriteBatch batch, Vector2 center, float radius, Color tint)
    {
        var bounds = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2f), (int)(radius * 2f));
        batch.Draw(circle, bounds, tint);
    }
}
