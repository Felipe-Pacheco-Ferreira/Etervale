using Android.Graphics;
using Path = Android.Graphics.Path;

// The drawing the HUD frames share: the dark plate with its gold edge (and a round window for a 3D portrait), the
// gauges with a gloss and a fading trail where they just dropped, and outlined text. Called on the UI thread only.
internal static class VulkanHudArt
{
    public static readonly Color PlateTop = Color.Argb(232, 30, 34, 42);
    public static readonly Color PlateBottom = Color.Argb(236, 10, 12, 17);
    public static readonly Color Edge = Color.Argb(235, 176, 138, 72);
    public static readonly Color EdgeLight = Color.Argb(255, 240, 208, 140);
    public static readonly Color Trough = Color.Argb(235, 6, 7, 10);
    // The gem studding an NPC's portrait bezel.
    public static readonly Color NpcGem = Color.Rgb(70, 160, 255);

    // Animated HUD art redraws every AnimationFrameMs; its phase, in seconds, wraps after AnimationLoop so it keeps its precision.
    public const int AnimationFrameMs = 40;
    private const float AnimationLoop = 600f;

    public static readonly Gauge Life = new(Color.Rgb(236, 74, 62), Color.Rgb(138, 18, 22));
    public static readonly Gauge Mana = new(Color.Rgb(84, 156, 255), Color.Rgb(24, 58, 150));
    public static readonly Gauge Stamina = new(Color.Rgb(140, 214, 86), Color.Rgb(42, 112, 38));
    public static readonly Gauge Experience = new(Color.Rgb(250, 214, 110), Color.Rgb(176, 112, 24));

    private static readonly Paint Fill = new() { AntiAlias = true };
    private static readonly Paint Stroke = new() { AntiAlias = true };
    private static readonly Paint Text = new() { AntiAlias = true, FakeBoldText = true };
    private static readonly Path Shape = new();
    private static readonly RectF Box = new();

    static VulkanHudArt()
    {
        Stroke.SetStyle(Paint.Style.Stroke);
        Text.SetTypeface(Typeface.Create(Typeface.SansSerif, TypefaceStyle.Bold));
    }

    // The animation phase, in seconds, at this SystemClock.ElapsedRealtimeNanos time.
    public static float Phase(long nanos) => nanos % (long)(AnimationLoop * 1e9f) / 1e9f;

    // The top and bottom colours of a gauge's fill.
    public sealed record Gauge(Color Top, Color Bottom);

    // A rounded plate; with holeRadius > 0 it leaves a round window centred at (holeX, holeY) in an ornate bezel whose
    // studs hold gems of the given colour; with crest, the player's rays, laurels and crown around it (Crest).
    public static void Plate(Canvas canvas, RectF bounds, float radius, float holeX = 0f, float holeY = 0f, float holeRadius = 0f, Color gem = default,
        bool crest = false)
    {
        Shape.Reset();
        Shape.SetFillType(Path.FillType.EvenOdd!);
        Shape.AddRoundRect(bounds, radius, radius, Path.Direction.Cw!);
        if (holeRadius > 0f)
        {
            Shape.AddCircle(holeX, holeY, holeRadius, Path.Direction.Cw!);
        }

        Fill.SetShader(new LinearGradient(0, bounds.Top, 0, bounds.Bottom, [PlateTop.ToArgb(), PlateBottom.ToArgb()], null, Shader.TileMode.Clamp!));
        canvas.DrawPath(Shape, Fill);
        Fill.SetShader(null);
        var edge = Math.Max(1f, radius * .08f);
        Stroke.StrokeWidth = edge;
        Stroke.Color = Edge;
        Box.Set(bounds.Left + edge / 2f, bounds.Top + edge / 2f, bounds.Right - edge / 2f, bounds.Bottom - edge / 2f);
        canvas.DrawRoundRect(Box, radius, radius, Stroke);
        if (holeRadius > 0f)
        {
            if (crest)
            {
                Crest(canvas, holeX, holeY, holeRadius, gem == default ? Color.Rgb(214, 40, 52) : gem);
            }

            OrnateRing(canvas, holeX, holeY, holeRadius, gem);
        }
    }

    // A bezel around a round window of this radius: a dark backing band, a broad gold ring with a light inner line and
    // notches all round, and diamond studs set with gems at the top, right, bottom and left.
    public static void OrnateRing(Canvas canvas, float x, float y, float radius, Color gem)
    {
        var band = Math.Max(3f, radius * .16f);
        Stroke.StrokeWidth = band * 1.35f;
        Stroke.Color = Color.Argb(235, 8, 6, 4);
        canvas.DrawCircle(x, y, radius + band * .6f, Stroke);
        Ring(canvas, x, y, radius, band * .82f);
        Stroke.StrokeWidth = Math.Max(1f, radius * .025f);
        Stroke.Color = EdgeLight;
        canvas.DrawCircle(x, y, radius + Stroke.StrokeWidth / 2f, Stroke);
        Stroke.StrokeWidth = Math.Max(1f, radius * .022f);
        Stroke.Color = Color.Argb(170, 70, 44, 12);
        for (var notch = 0; notch < Notches; notch++)
        {
            if (notch % (Notches / Studs) == 0)
            {
                continue;
            }

            var angle = notch * MathF.Tau / Notches;
            var (sin, cos) = MathF.SinCos(angle);
            canvas.DrawLine(x + cos * (radius + band * .22f), y + sin * (radius + band * .22f),
                x + cos * (radius + band * .68f), y + sin * (radius + band * .68f), Stroke);
        }

        var stone = gem == default ? Color.Rgb(214, 40, 52) : gem;
        for (var stud = 0; stud < Studs; stud++)
        {
            var (sin, cos) = MathF.SinCos(stud * MathF.Tau / Studs - MathF.PI / 2f);
            Diamond(canvas, x + cos * (radius + band * .42f), y + sin * (radius + band * .42f), band * .95f, stone);
        }
    }

    private const int Notches = 32;
    private const int Studs = 4;
    private const int Rays = 16;
    private const int Leaves = 5;

    // The player's crest around a portrait bezel of this radius: gold rays fanning out behind it, a laurel branch up each
    // side and a crown with a gem on top. Drawn before the bezel (OrnateRing), which covers the rays' roots.
    public static void Crest(Canvas canvas, float x, float y, float radius, Color gem)
    {
        Shape.Reset();
        Shape.SetFillType(Path.FillType.Winding!);
        for (var ray = 0; ray < Rays; ray++)
        {
            var angle = (ray + .5f) * MathF.Tau / Rays;
            var (sin, cos) = MathF.SinCos(angle);
            var (sinLeft, cosLeft) = MathF.SinCos(angle - .1f);
            var (sinRight, cosRight) = MathF.SinCos(angle + .1f);
            var reach = radius * (ray % 2 == 0 ? 1.42f : 1.3f);
            Shape.MoveTo(x + cosLeft * radius, y + sinLeft * radius);
            Shape.LineTo(x + cos * reach, y + sin * reach);
            Shape.LineTo(x + cosRight * radius, y + sinRight * radius);
            Shape.Close();
        }

        Fill.SetShader(new RadialGradient(x, y, radius * 1.42f, [EdgeLight.ToArgb(), Edge.ToArgb(), Color.Argb(0, 120, 78, 24).ToArgb()],
            [.6f, .85f, 1f], Shader.TileMode.Clamp!));
        canvas.DrawPath(Shape, Fill);
        Fill.SetShader(null);

        // Laurel leaves climb from the bottom of the bezel up each side.
        Fill.SetShader(new LinearGradient(x, y - radius, x, y + radius, [Color.Rgb(250, 226, 150).ToArgb(), Color.Rgb(150, 104, 36).ToArgb()], null,
            Shader.TileMode.Clamp!));
        Stroke.StrokeWidth = Math.Max(1f, radius * .025f);
        Stroke.Color = Color.Argb(220, 50, 32, 10);
        for (var side = -1; side <= 1; side += 2)
        {
            for (var leaf = 0; leaf < Leaves; leaf++)
            {
                var degrees = 100f + leaf * 17f;
                var (sin, cos) = MathF.SinCos(degrees * MathF.PI / 180f);
                var leafX = x + side * -cos * radius * 1.22f;
                var leafY = y + sin * radius * 1.22f;
                var saved = canvas.Save();
                canvas.Rotate(side * (degrees - 90f + 35f), leafX, leafY);
                Box.Set(leafX - radius * .07f, leafY - radius * .17f, leafX + radius * .07f, leafY + radius * .17f);
                canvas.DrawOval(Box, Fill);
                canvas.DrawOval(Box, Stroke);
                canvas.RestoreToCount(saved);
            }
        }

        Fill.SetShader(null);

        // The crown: a band with three points over the top of the bezel.
        var crownBase = y - radius * 1.1f;
        var width = radius * .5f;
        Shape.Reset();
        Shape.MoveTo(x - width, crownBase);
        Shape.LineTo(x - width * 1.05f, crownBase - radius * .24f);
        Shape.LineTo(x - width * .5f, crownBase - radius * .1f);
        Shape.LineTo(x, crownBase - radius * .32f);
        Shape.LineTo(x + width * .5f, crownBase - radius * .1f);
        Shape.LineTo(x + width * 1.05f, crownBase - radius * .24f);
        Shape.LineTo(x + width, crownBase);
        Shape.Close();
        Fill.SetShader(new LinearGradient(x, crownBase - radius * .32f, x, crownBase, [EdgeLight.ToArgb(), Color.Rgb(150, 98, 28).ToArgb()], null,
            Shader.TileMode.Clamp!));
        canvas.DrawPath(Shape, Fill);
        Fill.SetShader(null);
        canvas.DrawPath(Shape, Stroke);
        Diamond(canvas, x, crownBase - radius * .1f, radius * .09f, gem);
    }

    // A gold diamond with a gem in it.
    private static void Diamond(Canvas canvas, float x, float y, float size, Color gem)
    {
        DiamondPath(x, y, size);
        Fill.SetShader(new LinearGradient(x, y - size, x, y + size, [EdgeLight.ToArgb(), Color.Rgb(120, 78, 24).ToArgb()], null, Shader.TileMode.Clamp!));
        canvas.DrawPath(Shape, Fill);
        Fill.SetShader(null);
        Stroke.StrokeWidth = Math.Max(1f, size * .12f);
        Stroke.Color = Color.Rgb(40, 26, 10);
        canvas.DrawPath(Shape, Stroke);
        DiamondPath(x, y, size * .55f);
        Fill.SetShader(new RadialGradient(x - size * .15f, y - size * .2f, size * .7f,
            [Color.White.ToArgb(), gem.ToArgb(), Color.Argb(255, gem.R / 3, gem.G / 3, gem.B / 3).ToArgb()], [0f, .45f, 1f], Shader.TileMode.Clamp!));
        canvas.DrawPath(Shape, Fill);
        Fill.SetShader(null);
    }

    private static void DiamondPath(float x, float y, float size)
    {
        Shape.Reset();
        Shape.SetFillType(Path.FillType.Winding!);
        Shape.MoveTo(x, y - size);
        Shape.LineTo(x + size * .7f, y);
        Shape.LineTo(x, y + size);
        Shape.LineTo(x - size * .7f, y);
        Shape.Close();
    }

    // The attack button's face: a red glow behind a steel disc in an ornate bezel, with two crossed swords on it. Darker
    // and smaller while pressed, faded while disabled.
    public static void AttackFace(Canvas canvas, float x, float y, float radius, bool enabled, bool pressed)
    {
        var face = radius * (pressed ? .8f : .84f);
        Fill.SetShader(new RadialGradient(x, y, radius, [Color.Argb(150, 255, 90, 40).ToArgb(), Color.Argb(0, 255, 60, 20).ToArgb()],
            [.7f, 1f], Shader.TileMode.Clamp!));
        canvas.DrawCircle(x, y, radius, Fill);
        Fill.SetShader(new RadialGradient(x, y - face * .5f, face * 1.6f,
            [Color.Rgb(pressed ? 54 : 92, pressed ? 58 : 100, pressed ? 70 : 118).ToArgb(), Color.Rgb(12, 14, 22).ToArgb()], null, Shader.TileMode.Clamp!));
        Fill.Alpha = enabled ? 255 : 140;
        canvas.DrawCircle(x, y, face, Fill);
        Fill.SetShader(null);
        Fill.Alpha = 255;
        OrnateRing(canvas, x, y, face * .86f, Color.Rgb(255, 120, 40));
        Sword(canvas, x, y, face * .86f, 45f, enabled);
        Sword(canvas, x, y, face * .86f, -45f, enabled);
    }

    // A sword across a disc of this radius, turned by degrees, its tip up before the turn.
    private static void Sword(Canvas canvas, float x, float y, float radius, float degrees, bool enabled)
    {
        var s = radius;
        var saved = canvas.Save();
        canvas.Translate(x, y);
        canvas.Rotate(degrees);
        Shape.Reset();
        Shape.SetFillType(Path.FillType.Winding!);
        Shape.MoveTo(0, -.78f * s);
        Shape.LineTo(.09f * s, -.6f * s);
        Shape.LineTo(.09f * s, .2f * s);
        Shape.LineTo(-.09f * s, .2f * s);
        Shape.LineTo(-.09f * s, -.6f * s);
        Shape.Close();
        Shape.AddRoundRect(-.3f * s, .2f * s, .3f * s, .3f * s, .05f * s, .05f * s, Path.Direction.Cw!);
        Shape.AddRect(-.05f * s, .3f * s, .05f * s, .56f * s, Path.Direction.Cw!);
        Shape.AddCircle(0, .63f * s, .08f * s, Path.Direction.Cw!);
        Fill.SetShader(new LinearGradient(-.1f * s, 0, .1f * s, 0, [Color.Rgb(255, 250, 235).ToArgb(), Color.Rgb(196, 158, 86).ToArgb()], null,
            Shader.TileMode.Clamp!));
        Fill.Alpha = enabled ? 255 : 130;
        canvas.DrawPath(Shape, Fill);
        Fill.SetShader(null);
        Fill.Alpha = 255;
        Stroke.StrokeWidth = Math.Max(1.5f, s * .03f);
        Stroke.Color = Color.Argb(230, 30, 18, 8);
        canvas.DrawPath(Shape, Stroke);
        canvas.RestoreToCount(saved);
    }

    // A gold ring, lighter at the top.
    public static void Ring(Canvas canvas, float x, float y, float radius, float width)
    {
        Stroke.StrokeWidth = width;
        Stroke.SetShader(new LinearGradient(0, y - radius, 0, y + radius, [EdgeLight.ToArgb(), Edge.ToArgb()], null, Shader.TileMode.Clamp!));
        canvas.DrawCircle(x, y, radius + width / 2f, Stroke);
        Stroke.SetShader(null);
    }

    // A glow that breathes around a portrait bezel of this radius and three sparks that circle it; phase: seconds.
    public static void Aura(Canvas canvas, float x, float y, float radius, float phase)
    {
        var breath = .5f + .5f * MathF.Sin(phase * 2.2f);
        var outer = radius * (1.5f + .06f * breath);
        Fill.SetShader(new RadialGradient(x, y, outer, [Color.Argb(0, 255, 210, 120).ToArgb(), Color.Argb((int)(70 + 60 * breath), 255, 196, 90).ToArgb(),
            Color.Argb(0, 255, 160, 40).ToArgb()], [.62f, .78f, 1f], Shader.TileMode.Clamp!));
        canvas.DrawCircle(x, y, outer, Fill);
        Fill.SetShader(null);
        var orbit = radius * 1.2f;
        for (var spark = 0; spark < AuraSparks; spark++)
        {
            for (var tail = 0; tail < AuraTail; tail++)
            {
                var angle = phase * 1.4f + spark * MathF.Tau / AuraSparks - tail * .07f;
                var (sin, cos) = MathF.SinCos(angle);
                var fade = 1f - tail / (float)AuraTail;
                Fill.Color = Color.Argb((int)(230 * fade), 255, 240, 190);
                canvas.DrawCircle(x + cos * orbit, y + sin * orbit, Math.Max(1f, radius * .05f * fade), Fill);
            }
        }
    }

    private const int AuraSparks = 3;
    private const int AuraTail = 8;

    // The level on a medallion in the HUD's own make: a dark plate in a gold ring with a gold numeral. The stone it is set
    // with climbs with the level against the highest one (amber, emerald, sapphire, crimson), as an inner line and a
    // breathing glow; the highest level burns, its flames flickering with phase (seconds).
    public static void LevelBadge(Canvas canvas, float x, float y, float radius, string text, int level, int highest, float phase)
    {
        var top = level >= highest;
        var share = level / (float)Math.Max(1, highest);
        var (light, dark) = top ? (Color.Rgb(255, 236, 140), Color.Rgb(200, 60, 10))
            : share >= .75f ? (Color.Rgb(255, 130, 120), Color.Rgb(120, 10, 20))
            : share >= .5f ? (Color.Rgb(150, 190, 255), Color.Rgb(30, 50, 150))
            : share >= .25f ? (Color.Rgb(140, 240, 170), Color.Rgb(16, 110, 60))
            : (EdgeLight, Color.Rgb(120, 78, 24));
        if (top)
        {
            Flames(canvas, x, y, radius, phase);
        }
        else if (share >= .25f)
        {
            var pulse = .5f + .5f * MathF.Sin(phase * 3f);
            Fill.SetShader(new RadialGradient(x, y, radius * 1.7f, [Color.Argb((int)(120 * pulse), light.R, light.G, light.B).ToArgb(),
                Color.Argb(0, light.R, light.G, light.B).ToArgb()], [.55f, 1f], Shader.TileMode.Clamp!));
            canvas.DrawCircle(x, y, radius * 1.7f, Fill);
            Fill.SetShader(null);
        }

        var band = Math.Max(2f, radius * .2f);
        var inner = radius - band;
        Stroke.StrokeWidth = band * .7f;
        Stroke.Color = Color.Argb(235, 8, 6, 4);
        canvas.DrawCircle(x, y, radius + band * .2f, Stroke);
        Fill.SetShader(new RadialGradient(x, y - inner * .5f, inner * 1.6f, [Color.Argb(250, dark.R / 3 + 24, dark.G / 3 + 22, dark.B / 3 + 26).ToArgb(),
            PlateBottom.ToArgb()], null, Shader.TileMode.Clamp!));
        canvas.DrawCircle(x, y, inner, Fill);
        Fill.SetShader(null);
        Ring(canvas, x, y, inner, band);
        Stroke.StrokeWidth = Math.Max(1f, radius * .07f);
        Stroke.Color = Color.Argb(230, light.R, light.G, light.B);
        canvas.DrawCircle(x, y, inner - Stroke.StrokeWidth, Stroke);
        Fill.Color = Color.Argb(40, 255, 240, 200);
        Box.Set(x - inner * .7f, y - inner * .88f, x + inner * .7f, y - inner * .15f);
        canvas.DrawOval(Box, Fill);
        Label(canvas, text, x, y, inner * (text.Length > 2 ? 1.05f : 1.3f), top ? Color.Rgb(255, 246, 210) : EdgeLight, Paint.Align.Center!);
    }

    // Tongues of fire around a circle, each rising and swaying on its own beat.
    private static void Flames(Canvas canvas, float x, float y, float radius, float phase)
    {
        for (var layer = 0; layer < 2; layer++)
        {
            var reach = radius * (layer == 0 ? 2.1f : 1.6f);
            Shape.Reset();
            Shape.SetFillType(Path.FillType.Winding!);
            for (var tongue = 0; tongue < FlameTongues; tongue++)
            {
                var beat = phase * (5f + tongue % 3) + tongue * 1.7f + layer;
                var lift = reach * (.75f + .25f * MathF.Sin(beat));
                var angle = -MathF.PI / 2f + (tongue - (FlameTongues - 1) / 2f) * .42f + .08f * MathF.Sin(beat * 1.3f);
                var (sin, cos) = MathF.SinCos(angle);
                var (sinLeft, cosLeft) = MathF.SinCos(angle - .5f);
                var (sinRight, cosRight) = MathF.SinCos(angle + .5f);
                var sway = radius * .25f * MathF.Sin(beat * .9f);
                Shape.MoveTo(x + cosLeft * radius * .9f, y + sinLeft * radius * .9f);
                Shape.QuadTo(x + cos * lift * .6f - sin * sway, y + sin * lift * .6f + cos * sway, x + cos * lift, y + sin * lift);
                Shape.QuadTo(x + cos * lift * .6f + sin * sway, y + sin * lift * .6f - cos * sway, x + cosRight * radius * .9f, y + sinRight * radius * .9f);
                Shape.Close();
            }

            int[] colors = layer == 0
                ? [Color.Argb(230, 255, 120, 20).ToArgb(), Color.Argb(160, 220, 40, 10).ToArgb(), Color.Argb(0, 160, 20, 0).ToArgb()]
                : [Color.Argb(255, 255, 250, 200).ToArgb(), Color.Argb(230, 255, 200, 60).ToArgb(), Color.Argb(0, 255, 140, 20).ToArgb()];
            Fill.SetShader(new RadialGradient(x, y, reach, colors, [.35f, .7f, 1f], Shader.TileMode.Clamp!));
            canvas.DrawPath(Shape, Fill);
            Fill.SetShader(null);
        }

        for (var ember = 0; ember < FlameEmbers; ember++)
        {
            var rise = (phase * .8f + ember / (float)FlameEmbers) % 1f;
            var drift = MathF.Sin(phase * 2f + ember * 2.3f) * radius * .8f;
            Fill.Color = Color.Argb((int)(220 * (1f - rise)), 255, 200 - (int)(120 * rise), 60);
            canvas.DrawCircle(x + drift, y - radius - rise * radius * 2.4f, Math.Max(1f, radius * .09f * (1f - rise)), Fill);
        }
    }

    private const int FlameTongues = 7;
    private const int FlameEmbers = 6;

    // A gauge: the trough, the trail (where the value was a moment ago) and the fill up to fraction, glassy (a bright
    // top edge, a soft gloss, a lit tip) with the text centred. shine, from 0 to 1, sweeps a band of light along the fill;
    // a negative shine draws none.
    public static void Bar(Canvas canvas, RectF bounds, Gauge gauge, float fraction, float trail, string text, float textSize, float shine = -1f)
    {
        var radius = bounds.Height() / 2f;
        Fill.Color = Trough;
        canvas.DrawRoundRect(bounds, radius, radius, Fill);
        var inner = Math.Max(1f, bounds.Height() * .12f);
        var width = bounds.Width() - inner * 2f;
        if (trail > fraction)
        {
            Fill.Color = Color.Argb(210, 255, 236, 190);
            Box.Set(bounds.Left + inner, bounds.Top + inner, bounds.Left + inner + width * Math.Clamp(trail, 0f, 1f), bounds.Bottom - inner);
            canvas.DrawRoundRect(Box, radius, radius, Fill);
        }

        if (fraction > 0f)
        {
            Box.Set(bounds.Left + inner, bounds.Top + inner, bounds.Left + inner + Math.Max(width * Math.Clamp(fraction, 0f, 1f), radius), bounds.Bottom - inner);
            var lit = Color.Rgb(Math.Min(255, gauge.Top.R + 70), Math.Min(255, gauge.Top.G + 70), Math.Min(255, gauge.Top.B + 70));
            Fill.SetShader(new LinearGradient(0, Box.Top, 0, Box.Bottom, [lit.ToArgb(), gauge.Top.ToArgb(), gauge.Bottom.ToArgb(), gauge.Top.ToArgb()],
                [0f, .3f, .8f, 1f], Shader.TileMode.Clamp!));
            canvas.DrawRoundRect(Box, radius, radius, Fill);
            var fillLeft = Box.Left;
            var fillRight = Box.Right;
            var fillTop = Box.Top;
            var fillBottom = Box.Bottom;
            Box.Set(fillLeft + radius * .4f, fillTop + Box.Height() * .08f, fillRight - radius * .4f, fillTop + (fillBottom - fillTop) * .45f);
            Fill.SetShader(new LinearGradient(0, Box.Top, 0, Box.Bottom, [Color.Argb(150, 255, 255, 255).ToArgb(), Color.Argb(10, 255, 255, 255).ToArgb()], null,
                Shader.TileMode.Clamp!));
            canvas.DrawRoundRect(Box, radius * .6f, radius * .6f, Fill);
            if (shine >= 0f)
            {
                var band = (fillBottom - fillTop) * 2.5f;
                var at = fillLeft - band + (fillRight - fillLeft + band * 2f) * shine;
                var saved = canvas.Save();
                Box.Set(fillLeft, fillTop, fillRight, fillBottom);
                canvas.ClipRect(Box);
                Fill.SetShader(new LinearGradient(at - band, 0, at + band, 0, [Color.Argb(0, 255, 255, 255).ToArgb(), Color.Argb(120, 255, 255, 255).ToArgb(),
                    Color.Argb(0, 255, 255, 255).ToArgb()], null, Shader.TileMode.Clamp!));
                canvas.DrawRect(Box, Fill);
                canvas.RestoreToCount(saved);
            }

            Fill.SetShader(new RadialGradient(fillRight - radius * .5f, (fillTop + fillBottom) / 2f, radius * 1.6f,
                [Color.Argb(200, 255, 255, 240).ToArgb(), Color.Argb(0, lit.R, lit.G, lit.B).ToArgb()], null, Shader.TileMode.Clamp!));
            canvas.DrawCircle(fillRight - radius * .5f, (fillTop + fillBottom) / 2f, radius * 1.6f, Fill);
            Fill.SetShader(null);
        }

        Stroke.StrokeWidth = Math.Max(1f, inner * .6f);
        Stroke.Color = Color.Argb(150, 176, 138, 72);
        canvas.DrawRoundRect(bounds, radius, radius, Stroke);
        if (!string.IsNullOrEmpty(text))
        {
            Label(canvas, text, bounds.CenterX(), bounds.CenterY(), textSize, Color.White, Paint.Align.Center!);
        }
    }

    // Text with a dark outline, its middle at y.
    public static void Label(Canvas canvas, string text, float x, float y, float size, Color color, Paint.Align align)
    {
        Text.TextSize = size;
        Text.TextAlign = align;
        var baseline = y - (Text.Descent() + Text.Ascent()) / 2f;
        Text.SetStyle(Paint.Style.Stroke);
        Text.StrokeWidth = Math.Max(2f, size * .16f);
        Text.Color = Color.Argb(color.A * 220 / 255, 0, 0, 0);
        canvas.DrawText(text, x, baseline, Text);
        Text.SetStyle(Paint.Style.Fill);
        Text.Color = color;
        canvas.DrawText(text, x, baseline, Text);
    }

    // A red badge shows counts up to MostBadge, then MostBadgeText.
    public const int MostBadge = 9;
    public const string MostBadgeText = "9+";
    public static readonly Color BadgeRed = Color.Rgb(200, 40, 40);

    // A round red badge with a white edge and its count: something waits for the player.
    public static void RedBadge(Canvas canvas, float x, float y, float radius, string text)
    {
        Fill.Color = BadgeRed;
        canvas.DrawCircle(x, y, radius, Fill);
        Stroke.StrokeWidth = Math.Max(1.5f, radius * .14f);
        Stroke.Color = Color.White;
        canvas.DrawCircle(x, y, radius, Stroke);
        Label(canvas, text, x, y, radius * 1.1f, Color.White, Paint.Align.Center!);
    }

    // A round badge filled gold, with its text.
    public static void Badge(Canvas canvas, float x, float y, float radius, string text)
    {
        Fill.SetShader(new RadialGradient(x, y - radius * .4f, radius * 1.4f, [EdgeLight.ToArgb(), Color.Rgb(120, 78, 24).ToArgb()], null, Shader.TileMode.Clamp!));
        canvas.DrawCircle(x, y, radius, Fill);
        Fill.SetShader(null);
        Stroke.StrokeWidth = Math.Max(1.5f, radius * .12f);
        Stroke.Color = Color.Rgb(40, 26, 10);
        canvas.DrawCircle(x, y, radius, Stroke);
        Label(canvas, text, x, y, radius * 1.05f, Color.White, Paint.Align.Center!);
    }

    // A round button face: a dark sphere, dimmed while disabled, darker while pressed, in a gold ring.
    public static void Orb(Canvas canvas, float x, float y, float radius, bool enabled, bool pressed)
    {
        var light = Color.Rgb(58, 66, 82);
        var dark = Color.Rgb(10, 12, 18);
        if (pressed)
        {
            (light, dark) = (dark, light);
        }

        Fill.SetShader(new RadialGradient(x, y - radius * .45f, radius * 1.5f, [light.ToArgb(), dark.ToArgb()], null, Shader.TileMode.Clamp!));
        Fill.Alpha = enabled ? 235 : 130;
        canvas.DrawCircle(x, y, radius, Fill);
        Fill.SetShader(null);
        Fill.Alpha = 255;
        Ring(canvas, x, y, radius - Math.Max(2f, radius * .07f), Math.Max(2f, radius * .07f));
    }
}

// The HUD plate as a view's background (the quest card, the menu).
internal sealed class PlateDrawable(float radius) : Android.Graphics.Drawables.Drawable
{
    private readonly RectF _bounds = new();

    public override void Draw(Canvas canvas)
    {
        _bounds.Set(Bounds);
        VulkanHudArt.Plate(canvas, _bounds, radius);
    }

    public override void SetAlpha(int alpha)
    {
    }

    public override void SetColorFilter(ColorFilter colorFilter)
    {
    }

#pragma warning disable CS0672 // Android marks it obsolete, but a Drawable must still answer it.
    public override int Opacity => (int)Format.Translucent;
#pragma warning restore CS0672
}

// A gauge value that eases toward the value it is given, with a trail that follows a drop more slowly.
internal sealed class EasedGauge
{
    private const float Ease = 10f;
    private const float TrailEase = 2.5f;
    private float _target;

    public float Shown { get; private set; }
    public float Trail { get; private set; }

    public void Set(double value, double max)
    {
        _target = max > 0 ? (float)Math.Clamp(value / max, 0d, 1d) : 0f;
    }

    // Moves by elapsed seconds; true while still moving.
    public bool Step(float elapsed)
    {
        Shown += (_target - Shown) * Math.Min(1f, Ease * elapsed);
        Trail = _target >= Trail ? Shown : Trail + (_target - Trail) * Math.Min(1f, TrailEase * elapsed);
        if (Math.Abs(_target - Shown) < .001f && Math.Abs(_target - Trail) < .001f)
        {
            Shown = _target;
            Trail = _target;
            return false;
        }

        return true;
    }
}
