using Android.Graphics;
using Android.Graphics.Drawables;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;

internal sealed class VulkanSkillBorder : Drawable
{
    private readonly Paint _paint = new(PaintFlags.AntiAlias);
    private readonly RectF _ring = new();
    private JobTier _tier;
    private int _rank;
    private int _alpha = 255;
    public bool Animated => _tier != 0 && _rank == SkillLearning.MaxRank;

    public VulkanSkillBorder()
    {
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeCap = Paint.Cap.Round;
    }

    public bool SetSkill(JobTier tier, int rank)
    {
        if (_tier == tier && _rank == rank)
        {
            return false;
        }

        _tier = tier;
        _rank = Math.Clamp(rank, 0, SkillLearning.MaxRank);
        InvalidateSelf();
        return true;
    }

    public override void Draw(Canvas canvas)
    {
        var bounds = Bounds;
        DrawRing(canvas, bounds.ExactCenterX(), bounds.ExactCenterY(), Math.Min(bounds.Width(), bounds.Height()) / 2f);
        if (Animated)
        {
            InvalidateSelf();
        }
    }

    public void DrawRing(Canvas canvas, float x, float y, float radius)
    {
        if (_tier == 0 || radius <= 0)
        {
            return;
        }

        var tint = _tier switch
        {
            JobTier.Job1 => Color.Rgb(204, 211, 223),
            JobTier.Job2 => Color.Rgb(108, 231, 162),
            JobTier.Job3 => Color.Rgb(116, 201, 255),
            JobTier.Job4 => Color.Rgb(217, 142, 255),
            _ => Color.Rgb(255, 192, 82)
        };
        var outer = radius * .86f;
        _paint.StrokeWidth = Math.Max(1f, radius * .045f);
        _paint.Color = Color.Argb((_rank > 0 ? 215 : 105) * _alpha / 255, tint.R, tint.G, tint.B);
        canvas.DrawCircle(x, y, outer, _paint);

        var inset = radius * .78f;
        _ring.Set(x - inset, y - inset, x + inset, y + inset);
        _paint.StrokeWidth = Math.Max(1f, radius * .085f);
        const float RankArc = 154f;
        var step = RankArc / SkillLearning.MaxRank;
        for (var mark = 0; mark < SkillLearning.MaxRank; mark++)
        {
            var alpha = mark < _rank ? 255 : 55;
            _paint.Color = Color.Argb(alpha * _alpha / 255, tint.R, tint.G, tint.B);
            canvas.DrawArc(_ring, 90f + RankArc / 2f - (mark + .8f) * step, step * .55f, false, _paint);
        }

        if (Animated)
        {
            var phase = VulkanHudArt.Phase(Android.OS.SystemClock.ElapsedRealtimeNanos());
            VulkanHudArt.GoldOrbit(canvas, x, y, radius * .93f, phase, _alpha);
        }
    }

    public override void SetAlpha(int alpha)
    {
        _alpha = alpha;
        InvalidateSelf();
    }

    public override void SetColorFilter(ColorFilter filter) => _paint.SetColorFilter(filter);
    public override int Opacity => (int)Format.Translucent;
}
