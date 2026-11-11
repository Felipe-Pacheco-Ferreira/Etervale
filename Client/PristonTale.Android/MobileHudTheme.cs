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
    private static readonly Color Panel = Color.Argb(225, 27, 22, 17);
    private static readonly Color Control = Color.Argb(240, 58, 44, 29);
    private static readonly Color Border = Color.Argb(205, 125, 98, 54);

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
        button.Background = Frame(context, prominent ? Color.Argb(245, 85, 56, 27) : Control, prominent ? 32 : 7);
        return button;
    }

    private static GradientDrawable Frame(Context context, Color fill, int radius)
    {
        var frame = new GradientDrawable();
        frame.SetColor(fill);
        frame.SetCornerRadius(Dp(context, radius));
        frame.SetStroke(Dp(context, 1), Border);
        return frame;
    }

    private static int Dp(Context context, int value) => (int)MathF.Round(value * (context.Resources?.DisplayMetrics?.Density ?? 1f));
}
