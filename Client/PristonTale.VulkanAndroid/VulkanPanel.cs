using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;

// A HUD window in the HUD's own look, in place of Android's white dialogs: the screen dimmed behind it, the gold-edged
// plate with a serif title, a round close button and the content below. A tap outside the plate closes it. The text
// styles every HUD window shares are here too.
internal sealed class VulkanPanel
{
    public static readonly Color Muted = Color.Rgb(168, 199, 193);
    public static readonly Color Row = Color.Argb(60, 255, 214, 140);

    private readonly FrameLayout _overlay;
    private readonly FrameLayout _window;

    public LinearLayout Content { get; }

    public event Action Closed;

    // width, height: the plate's size in dp (-2 wraps the content).
    public VulkanPanel(Activity activity, FrameLayout overlay, string title, int width, int height = -2)
    {
        _overlay = overlay;
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        int Dp(int value) => (int)MathF.Round(value * density);
        _window = new FrameLayout(activity) { Clickable = true };
        _window.SetBackgroundColor(Color.Argb(160, 0, 0, 0));
        _window.Click += (_, _) => Close();

        var plate = new LinearLayout(activity) { Orientation = Orientation.Vertical, Clickable = true, Background = new PlateDrawable(Dp(14)) };
        plate.SetPadding(Dp(18), Dp(12), Dp(18), Dp(14));
        var header = new LinearLayout(activity);
        header.SetGravity(GravityFlags.CenterVertical);
        header.AddView(Title(activity, title), new LinearLayout.LayoutParams(0, -2, 1));
        var close = new VulkanActionOrb(activity, HudGlyph.Exit) { ContentDescription = "Fechar" };
        close.Click += (_, _) => Close();
        header.AddView(close, new LinearLayout.LayoutParams(Dp(40), Dp(40)));
        plate.AddView(header);
        plate.AddView(Divider(activity), new LinearLayout.LayoutParams(-1, Dp(1)) { TopMargin = Dp(8), BottomMargin = Dp(10) });
        Content = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        plate.AddView(Content, new LinearLayout.LayoutParams(-1, height == -2 ? -2 : 0, height == -2 ? 0 : 1));
        _window.AddView(plate, new FrameLayout.LayoutParams(Math.Min(Dp(width), overlay.Width - Dp(24)),
            height == -2 ? -2 : Math.Min(Dp(height), overlay.Height - Dp(20)), GravityFlags.Center));
        overlay.AddView(_window, new FrameLayout.LayoutParams(-1, -1));
    }

    public void Close()
    {
        if (_window.Parent is not null)
        {
            _overlay.RemoveView(_window);
            Closed?.Invoke();
        }
    }

    // A window title: serif, bold, gold, with a dark shadow.
    public static TextView Title(Activity activity, string text) => Text(activity, text, 20, MobileHudTheme.Gold, serif: true);

    // A section heading inside a window.
    public static TextView Heading(Activity activity, string text) => Text(activity, text, 14, MobileHudTheme.Gold, serif: true);

    // Body text: light ink with a soft shadow, readable over the plate and over the world.
    public static TextView Body(Activity activity, string text, float size = 14, Color? color = null) =>
        Text(activity, text, size, color ?? MobileHudTheme.Ink, serif: false);

    public static TextView Text(Activity activity, string text, float size, Color color, bool serif)
    {
        var view = new TextView(activity) { Text = text, TextSize = size };
        view.SetTextColor(color);
        view.Typeface = serif ? Typeface.Create("serif", TypefaceStyle.Bold) : Typeface.Create(Typeface.SansSerif, TypefaceStyle.Normal);
        view.SetShadowLayer(3f, 0f, 1.5f, Color.Argb(220, 0, 0, 0));
        view.SetIncludeFontPadding(false);
        return view;
    }

    // A thin gold line between sections.
    public static View Divider(Activity activity)
    {
        var line = new View(activity);
        line.SetBackgroundColor(Color.Argb(150, 176, 138, 72));
        return line;
    }

    // A row with a label on the left and a value on the right, on a faint band.
    public static LinearLayout LabelValue(Activity activity, string label, string value, Color? valueColor = null)
    {
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        var row = new LinearLayout(activity);
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding((int)(10 * density), (int)(5 * density), (int)(10 * density), (int)(5 * density));
        row.SetBackgroundColor(Row);
        row.AddView(Body(activity, label, 14, Muted), new LinearLayout.LayoutParams(0, -2, 1));
        var shown = Text(activity, value, 15, valueColor ?? Color.White, serif: true);
        row.AddView(shown);
        return row;
    }
}
