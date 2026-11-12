using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace PristonTale.Mobile;

internal static class MobileHudTheme
{
    public static readonly Color Gold = Color.Rgb(232, 199, 129);
    public static readonly Color Ink = Color.Rgb(236, 225, 203);
    private static readonly Color Panel = Color.Argb(225, 18, 18, 21);
    private static readonly Color Control = Color.Argb(235, 31, 28, 27);
    private static readonly Color Border = Color.Argb(220, 153, 120, 65);

    public static void Card(View view)
    {
        view.Background = Frame(view.Context!, Panel, 8);
    }

    public static Button Button(Context context, string text, bool prominent = false)
    {
        var button = new Button(context) { Text = text, TextSize = prominent ? 15 : 12 };
        button.SetAllCaps(false);
        button.SetTextColor(Gold);
        button.SetPadding(Dp(context, 10), Dp(context, 4), Dp(context, 10), Dp(context, 4));
        button.SetMinHeight(Dp(context, 38));
        button.SetMinimumHeight(Dp(context, 38));
        button.SetMinWidth(0);
        button.SetMinimumWidth(0);
        var states = new StateListDrawable();
        states.AddState([global::Android.Resource.Attribute.StatePressed], Frame(context, Color.Rgb(117, 81, 42), prominent ? 40 : 8));
        states.AddState([-global::Android.Resource.Attribute.StateEnabled], Frame(context, Color.Argb(165, 16, 16, 20), prominent ? 40 : 8));
        states.AddState([], Frame(context, prominent ? Color.Rgb(85, 48, 31) : Control, prominent ? 40 : 8));
        button.Background = states;
        return button;
    }

    public static void Icon(Button button, HudGlyph glyph, bool stacked = false)
    {
        var context = button.Context!;
        var icon = new HudIcon(glyph, Dp(context, stacked ? 22 : 14));
        button.CompoundDrawablePadding = Dp(context, stacked ? 1 : 4);
        button.SetCompoundDrawablesWithIntrinsicBounds(stacked ? null : icon, stacked ? icon : null, null, null);
        button.Typeface = Typeface.Create("serif", TypefaceStyle.Bold);
    }

    private static GradientDrawable Frame(Context context, Color fill, int radius)
    {
        var frame = new GradientDrawable();
        var highlight = Color.Argb(fill.A, Math.Min(255, fill.R + 24), Math.Min(255, fill.G + 19), Math.Min(255, fill.B + 12));
        frame.SetOrientation(GradientDrawable.Orientation.TopBottom);
        frame.SetColors([highlight.ToArgb(), fill.ToArgb(), Color.Argb(fill.A, 13, 12, 15).ToArgb()]);
        frame.SetCornerRadius(Dp(context, radius));
        frame.SetStroke(Dp(context, 1), Border);
        return frame;
    }

    private static int Dp(Context context, int value) => (int)MathF.Round(value * (context.Resources?.DisplayMetrics?.Density ?? 1f));
}
