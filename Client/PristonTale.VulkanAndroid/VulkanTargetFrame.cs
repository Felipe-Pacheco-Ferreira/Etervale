using Android.Content;
using Android.Graphics;
using Android.Views;

// The target's frame at the top centre while the player is fighting or has picked someone: a round window over the
// target's 3D portrait (drawn by the game at HudPlacement), its name and level, and for monsters the life gauge with the
// percentage left. Placed centred at the top, TargetFrameWidth wide; draws nothing without a target.
internal sealed class VulkanTargetFrame : View
{
    private const float FrameBottom = HudPlacement.TargetPortraitY + HudPlacement.TargetPortraitSize + .025f;

    private readonly float _height;
    private readonly EasedGauge _life = new();
    private readonly RectF _box = new();
    private TargetView _target;
    private string _title = "";
    private string _lifeText = "";
    private long _lastFrame;

    public VulkanTargetFrame(Context context, float screenHeight) : base(context)
    {
        _height = screenHeight;
    }

    public int FrameHeight => (int)(FrameBottom * _height + 2);

    // The target to show, or null for none.
    public void Show(TargetView target)
    {
        if (target == _target)
        {
            return;
        }

        if (target is null || _target is null || target.Id != _target.Id)
        {
            _life.Set(1, 1);
            _life.Step(1f);
        }

        _target = target;
        if (target is not null)
        {
            _title = target.Level > 0 ? $"{target.Name}  ·  Nv. {target.Level}" : target.Name;
            _life.Set(target.Life, target.MaxLife);
            _lifeText = target.MaxLife > 0 ? $"{(int)Math.Ceiling(Math.Clamp(target.Life, 0, target.MaxLife) * 100d / target.MaxLife)}%" : "";
        }

        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (_target is not { } target)
        {
            _lastFrame = 0;
            return;
        }

        var h = _height;
        var now = Android.OS.SystemClock.ElapsedRealtimeNanos();
        var elapsed = _lastFrame == 0 ? 0f : Math.Min(.1f, (now - _lastFrame) / 1e9f);
        _lastFrame = now;
        var moving = _life.Step(elapsed);

        var left = Width / 2f - HudPlacement.TargetFrameWidth / 2f * h;
        var right = Width / 2f + HudPlacement.TargetFrameWidth / 2f * h;
        var size = HudPlacement.TargetPortraitSize * h;
        var centerX = HudPlacement.TargetPortraitLeft(Width, h) + size / 2f;
        var centerY = HudPlacement.TargetPortraitY * h + size / 2f;
        _box.Set(left, HudPlacement.TargetFrameTop * h, right, FrameBottom * h);
        VulkanHudArt.Plate(canvas, _box, .045f * h, centerX, centerY, size / 2f, target.MaxLife > 0 ? Color.Rgb(214, 40, 52) : VulkanHudArt.NpcGem);

        var textLeft = centerX + size / 2f + .03f * h;
        var textRight = right - .03f * h;
        var hostile = target.MaxLife > 0;
        var nameColor = hostile ? Color.Rgb(255, 170, 150) : Color.Rgb(170, 230, 255);
        VulkanHudArt.Label(canvas, _title, textLeft, hostile ? .06f * h : .09f * h, .034f * h, nameColor, Paint.Align.Left!);
        if (hostile)
        {
            _box.Set(textLeft, .088f * h, textRight, .13f * h);
            VulkanHudArt.Bar(canvas, _box, VulkanHudArt.Life, _life.Shown, _life.Trail, _lifeText, .026f * h);
        }

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

// The target as the game sees it this frame: MaxLife 0 for someone that cannot be fought (an NPC).
internal sealed record TargetView(ulong Id, string Name, int Level, int Life, int MaxLife);
