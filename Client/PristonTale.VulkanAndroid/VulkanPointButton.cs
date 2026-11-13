using Android.Content;
using Android.Graphics;
using Android.Views;

// A round "+" that spends a point (an attribute point, a skill rank), or with Minus a "-" that takes a planned one back:
// a gold disc with a dark sign and a breathing glow while it can, a dim dark one otherwise; darker while pressed.
internal sealed class VulkanPointButton : View
{
    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _glow = new() { AntiAlias = true };
    private readonly Paint _cross = new() { AntiAlias = true };
    private readonly RectF _bar = new();

    public VulkanPointButton(Context context) : base(context)
    {
        Clickable = true;
    }

    // A "-" instead of a "+".
    public bool Minus { get; init; }

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        var x = width / 2f;
        var y = height / 2f;
        var radius = Math.Min(width, height) / 2f * .72f;
        _fill.SetShader(new RadialGradient(x, y - radius * .45f, radius * 1.5f, [Color.Rgb(255, 238, 180).ToArgb(), Color.Rgb(222, 166, 64).ToArgb(),
            Color.Rgb(120, 74, 18).ToArgb()], [0f, .45f, 1f], Shader.TileMode.Clamp!));
        _glow.SetShader(new RadialGradient(x, y, Math.Min(width, height) / 2f, [Color.Argb(170, 255, 214, 110).ToArgb(),
            Color.Argb(0, 255, 214, 110).ToArgb()], [.55f, 1f], Shader.TileMode.Clamp!));
    }

    protected override void DrawableStateChanged()
    {
        base.DrawableStateChanged();
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        var x = Width / 2f;
        var y = Height / 2f;
        var radius = Math.Min(Width, Height) / 2f * .72f;
        if (Enabled)
        {
            var pulse = .5f + .5f * MathF.Sin(Android.OS.SystemClock.UptimeMillis() / 260f);
            _glow.Alpha = (int)(110 + 145 * pulse);
            canvas.DrawCircle(x, y, Math.Min(Width, Height) / 2f, _glow);
            _fill.Alpha = Pressed ? 170 : 255;
            canvas.DrawCircle(x, y, radius, _fill);
            VulkanHudArt.Ring(canvas, x, y, radius, Math.Max(1.5f, radius * .12f));
            _cross.Color = Color.Rgb(52, 30, 8);
            PostInvalidateDelayed(VulkanHudArt.AnimationFrameMs);
        }
        else
        {
            VulkanHudArt.Orb(canvas, x, y, radius, enabled: false, pressed: false);
            _cross.Color = Color.Argb(110, 232, 199, 129);
        }

        var arm = radius * .52f;
        var thick = radius * .12f;
        _bar.Set(x - arm, y - thick, x + arm, y + thick);
        canvas.DrawRoundRect(_bar, thick, thick, _cross);
        if (Minus)
        {
            return;
        }

        _bar.Set(x - thick, y - arm, x + thick, y + arm);
        canvas.DrawRoundRect(_bar, thick, thick, _cross);
    }
}
