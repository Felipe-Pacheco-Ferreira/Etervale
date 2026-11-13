using Android.Content;
using Android.Graphics;
using Android.Views;
using PristonTale.Simulation.Characters;

// The experience bar across the bottom of the screen: a gold-edged trough, the golden fill with a tick every tenth of
// the level, and the level and percentage in the middle.
internal sealed class VulkanExperienceBar : View
{
    private const int Ticks = 10;

    private readonly EasedGauge _experience = new();
    private readonly RectF _box = new();
    private readonly Paint _line = new() { AntiAlias = true };
    private string _text = "";
    private long _lastFrame;

    public VulkanExperienceBar(Context context) : base(context)
    {
    }

    public void Update(OwnerState owner)
    {
        var gained = owner.Experience >= owner.LevelStart ? owner.Experience - owner.LevelStart : 0;
        var needed = owner.NextLevel > owner.LevelStart ? owner.NextLevel - owner.LevelStart : 0;
        _experience.Set(gained, needed);
        _text = $"Nv. {owner.Level}   ·   EXP {(needed > 0 ? (double)gained / needed : 0):P2}";
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        var now = Android.OS.SystemClock.ElapsedRealtimeNanos();
        var elapsed = _lastFrame == 0 ? 0f : Math.Min(.1f, (now - _lastFrame) / 1e9f);
        _lastFrame = now;
        var moving = _experience.Step(elapsed);
        var inset = Height * .18f;
        _box.Set(inset, inset, Width - inset, Height - inset);
        VulkanHudArt.Bar(canvas, _box, VulkanHudArt.Experience, _experience.Shown, _experience.Shown, null, 0f);
        _line.Color = Color.Argb(120, 0, 0, 0);
        _line.StrokeWidth = Math.Max(1f, Height * .06f);
        for (var tick = 1; tick < Ticks; tick++)
        {
            var x = _box.Left + _box.Width() * tick / Ticks;
            canvas.DrawLine(x, _box.Top + inset * .5f, x, _box.Bottom - inset * .5f, _line);
        }

        VulkanHudArt.Label(canvas, _text, Width / 2f, Height / 2f, Height * .5f, Color.White, Paint.Align.Center!);
        if (moving)
        {
            PostInvalidateOnAnimation();
        }
        else
        {
            _lastFrame = 0;
        }
    }
}
