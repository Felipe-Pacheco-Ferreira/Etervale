using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Path = Android.Graphics.Path;

// What calls the player's eye to a part of the HUD, drawn over it as its foreground: a gold border that breathes with a
// bright light running round it (something done: a reward to claim, a quest completed), or a red dot with a count at its
// upper right (something new: an item in the bag, points to spend). Called on the UI thread.
internal static class VulkanAttention
{
    // The running gold border over view while on; radius: its corners, in pixels.
    public static void Gold(View view, bool on, float radius)
    {
        if (!on)
        {
            if (view.Foreground is GoldFrame)
            {
                view.Foreground = null;
            }

            return;
        }

        if (view.Foreground is not GoldFrame)
        {
            view.Foreground = new GoldFrame(radius, view.Resources?.DisplayMetrics?.Density ?? 1f);
        }
    }

    // The red dot with count over view's upper right corner; 0 removes it.
    public static void Dot(View view, int count)
    {
        if (count <= 0)
        {
            if (view.Foreground is RedDot)
            {
                view.Foreground = null;
            }

            return;
        }

        if (view.Foreground is RedDot dot)
        {
            dot.Count = count;
            return;
        }

        view.Foreground = new RedDot(view.Resources?.DisplayMetrics?.Density ?? 1f) { Count = count };
    }

    private sealed class GoldFrame(float radius, float density) : Drawable
    {
        private const float LoopSeconds = 2.2f;
        private const float RunShare = .22f;
        private readonly Paint _edge = new() { AntiAlias = true };
        private readonly Paint _run = new() { AntiAlias = true };
        private readonly Path _outline = new();
        private readonly Path _segment = new();
        private readonly PathMeasure _measure = new();
        private readonly RectF _box = new();

        public override void Draw(Canvas canvas)
        {
            var bounds = Bounds;
            var inset = 2f * density;
            _box.Set(bounds.Left + inset, bounds.Top + inset, bounds.Right - inset, bounds.Bottom - inset);
            _outline.Reset();
            _outline.AddRoundRect(_box, radius, radius, Path.Direction.Cw!);
            var seconds = Android.OS.SystemClock.ElapsedRealtime() % 600_000 / 1000f;
            var breath = .5f + .5f * MathF.Sin(seconds * 4f);
            _edge.SetStyle(Paint.Style.Stroke);
            _edge.StrokeWidth = 2.5f * density;
            _edge.Color = Color.Argb((int)(140 + 100 * breath), 255, 206, 90);
            _edge.SetShadowLayer(6f * density * (.5f + breath), 0f, 0f, Color.Argb(220, 255, 190, 60));
            canvas.DrawPath(_outline, _edge);
            _measure.SetPath(_outline, true);
            var length = _measure.Length;
            var head = seconds / LoopSeconds % 1f * length;
            _segment.Reset();
            var tail = head - length * RunShare;
            if (tail < 0f)
            {
                _measure.GetSegment(tail + length, length, _segment, true);
                _measure.GetSegment(0f, head, _segment, true);
            }
            else
            {
                _measure.GetSegment(tail, head, _segment, true);
            }

            _run.SetStyle(Paint.Style.Stroke);
            _run.StrokeWidth = 3.5f * density;
            _run.StrokeCap = Paint.Cap.Round;
            _run.Color = Color.Argb(255, 255, 248, 210);
            _run.SetShadowLayer(8f * density, 0f, 0f, Color.Argb(255, 255, 220, 120));
            canvas.DrawPath(_segment, _run);
            InvalidateSelf();
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

    private sealed class RedDot(float density) : Drawable
    {
        private int _count;

        public int Count
        {
            get => _count;
            set
            {
                if (value != _count)
                {
                    _count = value;
                    InvalidateSelf();
                }
            }
        }

        public override void Draw(Canvas canvas)
        {
            var bounds = Bounds;
            var radius = 8f * density;
            var text = _count > VulkanHudArt.MostBadge ? VulkanHudArt.MostBadgeText : _count.ToString();
            VulkanHudArt.RedBadge(canvas, bounds.Right - radius * .9f, bounds.Top + radius * .9f, radius, text);
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
