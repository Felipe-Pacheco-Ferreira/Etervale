using Android.Content;
using Android.Graphics;
using Android.Views;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;

// The player's frame at the top left: a plate with a round window over the 3D portrait the game draws there
// (HudPlacement), the level on a gold badge, the name, and the life, mana and stamina gauges. Placed at the screen's
// top-left corner, FrameWidth by FrameHeight pixels.
internal sealed class VulkanPlayerFrame : View
{
    private const float FrameRight = .66f;
    private const float FrameBottom = HudPlacement.PlayerPortraitY + HudPlacement.PlayerPortraitSize + .02f;
    private const int AnimationFrameMs = 40;
    // The animation phase wraps after this many seconds, so it keeps its precision; the shine crosses a bar every ShineSeconds.
    private const float AnimationLoop = 600f;
    private const float ShineSeconds = 3f;

    private readonly string _name;
    private readonly float _height;
    private readonly EasedGauge _life = new();
    private readonly EasedGauge _mana = new();
    private readonly EasedGauge _stamina = new();
    private readonly RectF _box = new();
    private string _level = "1";
    private int _levelValue = 1;
    private string _lifeText = "";
    private string _manaText = "";
    private string _staminaText = "";
    private long _lastFrame;

    // screenHeight: the screen's height in pixels, the unit of HudPlacement.
    public VulkanPlayerFrame(Context context, string name, float screenHeight) : base(context)
    {
        _name = name;
        _height = screenHeight;
        ContentDescription = $"Retrato de {name}";
    }

    public int FrameWidth => (int)(FrameRight * _height);
    public int FrameHeight => (int)(FrameBottom * _height + 2);

    public void Update(OwnerState owner)
    {
        _level = owner.Level.ToString();
        _levelValue = owner.Level;
        _life.Set(owner.Life, owner.MaxLife);
        _mana.Set(owner.Mana, owner.MaxMana);
        _stamina.Set(owner.Stamina, owner.MaxStamina);
        _lifeText = $"{owner.Life} / {owner.MaxLife}";
        _manaText = $"{owner.Mana} / {owner.MaxMana}";
        _staminaText = $"{owner.Stamina} / {owner.MaxStamina}";
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        var h = _height;
        var now = Android.OS.SystemClock.ElapsedRealtimeNanos();
        var elapsed = _lastFrame == 0 ? 0f : Math.Min(.1f, (now - _lastFrame) / 1e9f);
        _lastFrame = now;
        var moving = _life.Step(elapsed) | _mana.Step(elapsed) | _stamina.Step(elapsed);

        var size = HudPlacement.PlayerPortraitSize * h;
        var centerX = HudPlacement.PlayerPortraitX * h + size / 2f;
        var centerY = HudPlacement.PlayerPortraitY * h + size / 2f;
        _box.Set(.012f * h, .012f * h, FrameRight * h - 2f, FrameBottom * h);
        var phase = now % (long)(AnimationLoop * 1e9f) / 1e9f;
        VulkanHudArt.Plate(canvas, _box, .05f * h, centerX, centerY, size / 2f, crest: true);
        VulkanHudArt.Aura(canvas, centerX, centerY, size / 2f, phase);
        VulkanHudArt.LevelBadge(canvas, centerX - size * .36f, centerY + size * .38f, .03f * h, _level, _levelValue, ExperienceTable.MaximumLevel, phase);

        var left = centerX + size / 2f + .03f * h;
        var right = FrameRight * h - .03f * h;
        VulkanHudArt.Label(canvas, _name, left, .06f * h, .036f * h, VulkanHudArt.EdgeLight, Paint.Align.Left!);
        _box.Set(left, .085f * h, right, .125f * h);
        VulkanHudArt.Bar(canvas, _box, VulkanHudArt.Life, _life.Shown, _life.Trail, _lifeText, .026f * h, phase / ShineSeconds % 1f);
        _box.Set(left, .134f * h, right, .158f * h);
        VulkanHudArt.Bar(canvas, _box, VulkanHudArt.Mana, _mana.Shown, _mana.Trail, _manaText, .019f * h, (phase / ShineSeconds + .33f) % 1f);
        _box.Set(left, .166f * h, right - .08f * h, .186f * h);
        VulkanHudArt.Bar(canvas, _box, VulkanHudArt.Stamina, _stamina.Shown, _stamina.Trail, _staminaText, .016f * h, (phase / ShineSeconds + .66f) % 1f);
        // The aura, the badge and the shine move all the time: smoothly while a gauge eases, at AnimationFrameMs otherwise.
        if (moving)
        {
            PostInvalidateOnAnimation();
        }
        else
        {
            PostInvalidateDelayed(AnimationFrameMs);
        }
    }
}
