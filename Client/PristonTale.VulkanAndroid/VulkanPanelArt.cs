using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;
using Path = Android.Graphics.Path;

// The pieces HUD windows (VulkanPanel) build their content from, in VulkanHudArt's gold and dark plate: ornamented section
// headings, inset cards, pill tabs, state chips, item wells, a gauge view and the gold coin. UI thread only.
internal static class VulkanPanelArt
{
    public static readonly Color WellTop = Color.Argb(240, 5, 6, 9);
    public static readonly Color WellBottom = Color.Argb(240, 24, 26, 32);
    public static readonly Color Bronze = Color.Argb(200, 120, 94, 52);
    public static readonly Color Done = Color.Rgb(140, 214, 120);
    public static readonly Color Warning = Color.Rgb(240, 110, 90);
    public static readonly VulkanHudArt.Gauge Overweight = new(Color.Rgb(240, 110, 90), Color.Rgb(130, 30, 24));

    private static readonly Paint Fill = new() { AntiAlias = true };
    private static readonly Paint Stroke = new() { AntiAlias = true };
    private static readonly Path Shape = new();
    private static readonly RectF Box = new();

    static VulkanPanelArt()
    {
        Stroke.SetStyle(Paint.Style.Stroke);
    }

    // A section heading: a gold diamond, the serif title, an optional note on the right and a gold line fading out
    // between them.
    public static LinearLayout Section(Activity activity, string title, TextView note = null)
    {
        var row = new LinearLayout(activity);
        row.SetGravity(GravityFlags.CenterVertical);
        var diamond = new View(activity) { Background = new OrnamentDrawable(diamondOnly: true) };
        row.AddView(diamond, new LinearLayout.LayoutParams(Dp(activity, 10), Dp(activity, 10)) { RightMargin = Dp(activity, 6) });
        row.AddView(VulkanPanel.Heading(activity, title));
        var line = new View(activity) { Background = new OrnamentDrawable(diamondOnly: false) };
        row.AddView(line, new LinearLayout.LayoutParams(0, Dp(activity, 8), 1) { LeftMargin = Dp(activity, 8), RightMargin = Dp(activity, 6) });
        if (note is not null)
        {
            row.AddView(note);
        }

        return row;
    }

    // A recessed card: a dark band with a faint gold edge; lit, a warmer band with a full gold edge.
    public static Drawable Inset(Context context, bool lit = false)
    {
        var shape = new GradientDrawable(GradientDrawable.Orientation.TopBottom!, lit
            ? new[] { Color.Argb(220, 58, 46, 28).ToArgb(), Color.Argb(220, 24, 20, 14).ToArgb() }
            : new[] { Color.Argb(170, 6, 7, 10).ToArgb(), Color.Argb(170, 18, 20, 26).ToArgb() });
        shape.SetCornerRadius(Dp(context, 8));
        shape.SetStroke(Dp(context, lit ? 2 : 1), lit ? VulkanHudArt.EdgeLight : Bronze);
        return shape;
    }

    // A tab as a pill: the chosen one filled gold with dark text, the others dark with a gold edge and gold text.
    public static TextView Tab(Activity activity, string text, bool selected)
    {
        var tab = VulkanPanel.Text(activity, text, 13, selected ? Color.Rgb(40, 26, 10) : MobileHudTheme.Gold, serif: true);
        if (selected)
        {
            tab.SetShadowLayer(0f, 0f, 0f, Color.Transparent);
        }

        tab.Gravity = GravityFlags.Center;
        tab.Clickable = !selected;
        tab.SetPadding(Dp(activity, 16), Dp(activity, 6), Dp(activity, 16), Dp(activity, 6));
        var shape = new GradientDrawable(GradientDrawable.Orientation.TopBottom!, selected
            ? new[] { VulkanHudArt.EdgeLight.ToArgb(), Color.Rgb(176, 122, 40).ToArgb() }
            : new[] { Color.Argb(220, 30, 32, 40).ToArgb(), Color.Argb(220, 10, 12, 17).ToArgb() });
        shape.SetCornerRadius(Dp(activity, 40));
        shape.SetStroke(Dp(activity, 1), selected ? Color.Rgb(80, 52, 16) : VulkanHudArt.Edge);
        tab.Background = shape;
        return tab;
    }

    // A small state label on a tinted pill.
    public static TextView Chip(Activity activity, string text, Color color)
    {
        var chip = VulkanPanel.Text(activity, text, 11, color, serif: false);
        chip.Typeface = Typeface.Create(Typeface.SansSerif, TypefaceStyle.Bold);
        chip.SetPadding(Dp(activity, 8), Dp(activity, 2), Dp(activity, 8), Dp(activity, 3));
        var shape = new GradientDrawable();
        shape.SetColor(Color.Argb(60, color.R, color.G, color.B).ToArgb());
        shape.SetCornerRadius(Dp(activity, 20));
        shape.SetStroke(Dp(activity, 1), Color.Argb(170, color.R, color.G, color.B));
        chip.Background = shape;
        return chip;
    }

    // An item well: a recessed box darker at the top, a glow of the item's tint, its edge in that tint (bronze when it has
    // none) and, when chosen, a bright gold double edge.
    public static void Well(Canvas canvas, RectF bounds, float radius, float line, Color tint, bool selected)
    {
        Fill.SetShader(new LinearGradient(0, bounds.Top, 0, bounds.Bottom, [WellTop.ToArgb(), WellBottom.ToArgb()], null, Shader.TileMode.Clamp!));
        canvas.DrawRoundRect(bounds, radius, radius, Fill);
        Fill.SetShader(null);
        if (tint.A > 0)
        {
            var reach = Math.Max(bounds.Width(), bounds.Height()) * .65f;
            Fill.SetShader(new RadialGradient(bounds.CenterX(), bounds.CenterY(), reach,
                [Color.Argb(90, tint.R, tint.G, tint.B).ToArgb(), Color.Argb(0, tint.R, tint.G, tint.B).ToArgb()], null, Shader.TileMode.Clamp!));
            canvas.DrawRoundRect(bounds, radius, radius, Fill);
            Fill.SetShader(null);
        }

        // The top inner shadow that sinks the well into the plate.
        Stroke.StrokeWidth = line;
        Stroke.Color = Color.Argb(170, 0, 0, 0);
        Box.Set(bounds.Left + line * 1.5f, bounds.Top + line * 1.5f, bounds.Right - line * 1.5f, bounds.Top + line * 1.5f);
        canvas.DrawLine(Box.Left + radius, Box.Top, Box.Right - radius, Box.Top, Stroke);
        Box.Set(bounds.Left + line / 2f, bounds.Top + line / 2f, bounds.Right - line / 2f, bounds.Bottom - line / 2f);
        if (selected)
        {
            Stroke.StrokeWidth = line * 2.4f;
            Stroke.Color = Color.Argb(110, 255, 214, 64);
            canvas.DrawRoundRect(Box, radius, radius, Stroke);
            Stroke.StrokeWidth = line * 1.4f;
            Stroke.Color = VulkanHudArt.EdgeLight;
            canvas.DrawRoundRect(Box, radius, radius, Stroke);
            return;
        }

        Stroke.Color = tint.A > 0 ? Color.Argb(220, tint.R, tint.G, tint.B) : Bronze;
        canvas.DrawRoundRect(Box, radius, radius, Stroke);
    }

    public static int Dp(Context context, int value) => (int)MathF.Round(value * (context.Resources?.DisplayMetrics?.Density ?? 1f));

    // A section heading's ornament: a gold diamond, or a gold line fading to the right from a small diamond.
    private sealed class OrnamentDrawable(bool diamondOnly) : TranslucentDrawable
    {
        public override void Draw(Canvas canvas)
        {
            var bounds = Bounds;
            var middle = bounds.ExactCenterY();
            var size = bounds.Height() / 2f;
            if (!diamondOnly)
            {
                Fill.SetShader(new LinearGradient(bounds.Left, 0, bounds.Right, 0, [VulkanHudArt.Edge.ToArgb(), Color.Argb(0, 176, 138, 72).ToArgb()], null,
                    Shader.TileMode.Clamp!));
                canvas.DrawRect(bounds.Left + size, middle - .75f, bounds.Right, middle + .75f, Fill);
                Fill.SetShader(null);
                size *= .6f;
            }

            var x = bounds.Left + size;
            Shape.Reset();
            Shape.MoveTo(x, middle - size);
            Shape.LineTo(x + size * .75f, middle);
            Shape.LineTo(x, middle + size);
            Shape.LineTo(x - size * .75f, middle);
            Shape.Close();
            Fill.SetShader(new LinearGradient(0, middle - size, 0, middle + size, [VulkanHudArt.EdgeLight.ToArgb(), Color.Rgb(150, 98, 28).ToArgb()], null,
                Shader.TileMode.Clamp!));
            canvas.DrawPath(Shape, Fill);
            Fill.SetShader(null);
        }
    }

    // One item well as a view's background (Well).
    public sealed class WellDrawable(float radius, float line, Color tint, bool selected) : TranslucentDrawable
    {
        private readonly RectF _bounds = new();

        public override void Draw(Canvas canvas)
        {
            _bounds.Set(Bounds);
            Well(canvas, _bounds, radius, line, tint, selected);
        }
    }

    // The bag's empty cells: a well per cell, a gap between them.
    public sealed class GridDrawable(int cell, int columns, int rows, float gap, float line) : TranslucentDrawable
    {
        private readonly RectF _cell = new();

        public override void Draw(Canvas canvas)
        {
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    _cell.Set(column * cell + gap / 2f, row * cell + gap / 2f, (column + 1) * cell - gap / 2f, (row + 1) * cell - gap / 2f);
                    Well(canvas, _cell, gap, line, Color.Transparent, selected: false);
                }
            }
        }
    }

    // The equipment backdrop: a recessed panel with the faint outline of a figure the slots stand around.
    public sealed class FigureDrawable(float radius, float line) : TranslucentDrawable
    {
        private readonly RectF _bounds = new();

        public override void Draw(Canvas canvas)
        {
            _bounds.Set(Bounds);
            Fill.SetShader(new LinearGradient(0, _bounds.Top, 0, _bounds.Bottom, [Color.Argb(170, 4, 5, 8).ToArgb(), Color.Argb(170, 22, 20, 18).ToArgb()],
                null, Shader.TileMode.Clamp!));
            canvas.DrawRoundRect(_bounds, radius, radius, Fill);
            Fill.SetShader(new RadialGradient(_bounds.CenterX(), _bounds.Top + _bounds.Height() * .4f, _bounds.Width() * .7f,
                [Color.Argb(70, 240, 208, 140).ToArgb(), Color.Argb(0, 240, 208, 140).ToArgb()], null, Shader.TileMode.Clamp!));
            canvas.DrawRoundRect(_bounds, radius, radius, Fill);
            Fill.SetShader(null);

            // Head, shoulders and body, then the legs, in a faint gold.
            var x = _bounds.CenterX();
            var top = _bounds.Top;
            var height = _bounds.Height();
            var width = _bounds.Width();
            Fill.Color = Color.Argb(38, 240, 208, 140);
            canvas.DrawCircle(x, top + height * .14f, width * .1f, Fill);
            Shape.Reset();
            Shape.MoveTo(x - width * .22f, top + height * .3f);
            Shape.QuadTo(x, top + height * .22f, x + width * .22f, top + height * .3f);
            Shape.LineTo(x + width * .16f, top + height * .6f);
            Shape.LineTo(x + width * .12f, top + height * .94f);
            Shape.LineTo(x + width * .03f, top + height * .94f);
            Shape.LineTo(x, top + height * .66f);
            Shape.LineTo(x - width * .03f, top + height * .94f);
            Shape.LineTo(x - width * .12f, top + height * .94f);
            Shape.LineTo(x - width * .16f, top + height * .6f);
            Shape.Close();
            canvas.DrawPath(Shape, Fill);
            Stroke.StrokeWidth = line;
            Stroke.Color = Bronze;
            _bounds.Inset(line / 2f, line / 2f);
            canvas.DrawRoundRect(_bounds, radius, radius, Stroke);
        }
    }

    // A gold coin: a domed disc with a dark rim and an inner ring.
    public sealed class CoinDrawable(int size) : TranslucentDrawable
    {
        public override int IntrinsicWidth => size;

        public override int IntrinsicHeight => size;

        public override void Draw(Canvas canvas)
        {
            var x = Bounds.ExactCenterX();
            var y = Bounds.ExactCenterY();
            var radius = Math.Min(Bounds.Width(), Bounds.Height()) / 2f - 1f;
            Fill.SetShader(new RadialGradient(x - radius * .3f, y - radius * .35f, radius * 1.5f,
                [Color.Rgb(255, 246, 200).ToArgb(), Color.Rgb(240, 196, 70).ToArgb(), Color.Rgb(150, 96, 20).ToArgb()], [0f, .4f, 1f], Shader.TileMode.Clamp!));
            canvas.DrawCircle(x, y, radius, Fill);
            Fill.SetShader(null);
            Stroke.StrokeWidth = Math.Max(1f, radius * .14f);
            Stroke.Color = Color.Rgb(90, 56, 12);
            canvas.DrawCircle(x, y, radius - Stroke.StrokeWidth / 2f, Stroke);
            Stroke.StrokeWidth = Math.Max(1f, radius * .08f);
            Stroke.Color = Color.Argb(200, 150, 96, 20);
            canvas.DrawCircle(x, y, radius * .58f, Stroke);
        }
    }

    // The base of these drawables: it paints nothing itself and keeps no alpha or colour filter of its own.
    public class TranslucentDrawable : Drawable
    {
        public override void Draw(Canvas canvas)
        {
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
}

// A HUD gauge as a view (VulkanHudArt.Bar), for windows: the fill up to a fraction with its text centred.
internal sealed class VulkanGaugeView(Context context) : View(context)
{
    private readonly RectF _bounds = new();
    private VulkanHudArt.Gauge _gauge = VulkanHudArt.Experience;
    private float _fraction;
    private string _text = "";

    public void Set(VulkanHudArt.Gauge gauge, float fraction, string text)
    {
        _gauge = gauge;
        _fraction = Math.Clamp(fraction, 0f, 1f);
        _text = text ?? "";
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        _bounds.Set(0, 0, Width, Height);
        VulkanHudArt.Bar(canvas, _bounds, _gauge, _fraction, _fraction, _text, Height * .58f);
    }
}
