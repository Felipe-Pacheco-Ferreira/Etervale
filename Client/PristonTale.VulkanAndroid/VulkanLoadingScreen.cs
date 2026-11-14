using Android.Animation;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Path = Android.Graphics.Path;

// The loading screen: the game's art drifting slowly in and out under golden embers rising from below, and a gold bar
// with a light sweeping along it. With a progress source (0 to 1, or below 0 while unknown) the bar fills to it and the
// status line ends with the percent; without one, a glowing stretch runs along the bar.
internal sealed class VulkanLoadingScreen : FrameLayout
{
    private const long DriftMs = 16000;
    private const float DriftZoom = .08f;

    private readonly ValueAnimator _drift;
    private readonly string _message;
    private readonly Func<float> _progress;
    private readonly LoadingBar _bar;
    private int _shownPercent = -1;

    public TextView Status { get; }

    public VulkanLoadingScreen(Activity activity, string message, Func<float> progress = null) : base(activity)
    {
        _message = message;
        _progress = progress;
        SetBackgroundColor(Color.Rgb(12, 18, 24));
        var art = new ImageView(activity) { ContentDescription = "Priston Avalon" };
        using (var stream = activity.Assets!.Open("priston-avalon-loading.png"))
        {
            art.SetImageBitmap(BitmapFactory.DecodeStream(stream));
        }

        art.SetScaleType(ImageView.ScaleType.CenterCrop);
        AddView(art, new LayoutParams(-1, -1));
        AddView(new LoadingEmbers(activity), new LayoutParams(-1, -1));

        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        var footer = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        footer.SetGravity(GravityFlags.Center);
        footer.SetPadding(0, (int)(40 * density), 0, (int)(22 * density));
        footer.Background = new Android.Graphics.Drawables.GradientDrawable(Android.Graphics.Drawables.GradientDrawable.Orientation.BottomTop,
            [Color.Argb(210, 0, 0, 0), Color.Transparent]);
        Status = new TextView(activity) { Text = message, TextSize = 16, Gravity = GravityFlags.Center };
        Status.SetTextColor(Color.Rgb(246, 219, 161));
        Status.SetShadowLayer(6f, 0f, 2f, Color.Black);
        footer.AddView(Status);
        _bar = new LoadingBar(activity, progress);
        footer.AddView(_bar, new LinearLayout.LayoutParams((int)(420 * density), (int)(16 * density)) { TopMargin = (int)(10 * density) });
        AddView(footer, new LayoutParams(-1, -2, GravityFlags.Bottom));

        // The art drifts in and out, panning a little, forever.
        _drift = ValueAnimator.OfFloat(0f, 1f);
        _drift.SetDuration(DriftMs);
        _drift.RepeatCount = ValueAnimator.Infinite;
        _drift.RepeatMode = ValueAnimatorRepeatMode.Reverse;
        _drift.Update += (_, args) =>
        {
            var t = (float)args.Animation.AnimatedValue;
            var eased = t * t * (3f - 2f * t);
            art.ScaleX = 1f + DriftZoom * eased;
            art.ScaleY = 1f + DriftZoom * eased;
            art.TranslationX = (eased - .5f) * Width * DriftZoom * .6f;
            if (_progress is not null)
            {
                ShowPercent();
            }
        };
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        _drift.Start();
    }

    protected override void OnDetachedFromWindow()
    {
        _drift.Cancel();
        base.OnDetachedFromWindow();
    }

    // The status line with the percent, rewritten only when the percent changes.
    private void ShowPercent()
    {
        var value = _progress();
        var percent = value < 0f ? -1 : (int)(Math.Clamp(value, 0f, 1f) * 100f);
        if (percent != _shownPercent && Status.Text?.StartsWith(_message, StringComparison.Ordinal) != false)
        {
            _shownPercent = percent;
            Status.Text = percent < 0 ? _message : $"{_message} {percent}%";
        }
    }

    // Golden embers rising over the art, each on its own path, drifting side to side and fading out near the top.
    private sealed class LoadingEmbers : View
    {
        private const int Count = 46;
        private readonly float[] _x = new float[Count];
        private readonly float[] _y = new float[Count];
        private readonly float[] _speed = new float[Count];
        private readonly float[] _size = new float[Count];
        private readonly float[] _phase = new float[Count];
        private readonly Paint _paint = new() { AntiAlias = true };
        private readonly Random _random = new(7);
        private long _lastMs = -1;

        public LoadingEmbers(Context context) : base(context)
        {
            for (var index = 0; index < Count; index++)
            {
                Seed(index, _random.NextSingle());
            }
        }

        private void Seed(int index, float height)
        {
            _x[index] = _random.NextSingle();
            _y[index] = height;
            _speed[index] = .035f + _random.NextSingle() * .07f;
            _size[index] = 1.2f + _random.NextSingle() * 2.6f;
            _phase[index] = _random.NextSingle() * MathF.Tau;
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var now = Java.Lang.JavaSystem.CurrentTimeMillis();
            var seconds = _lastMs < 0 ? 0f : Math.Min(.1f, (now - _lastMs) / 1000f);
            _lastMs = now;
            var density = Resources!.DisplayMetrics!.Density;
            for (var index = 0; index < Count; index++)
            {
                _y[index] -= _speed[index] * seconds;
                if (_y[index] < -.05f)
                {
                    Seed(index, 1.05f);
                }

                var sway = MathF.Sin(now / 900f + _phase[index]) * .012f;
                var fade = Math.Clamp(_y[index] * 1.6f, 0f, 1f);
                var flicker = .65f + .35f * MathF.Sin(now / 160f + _phase[index] * 3f);
                var x = (_x[index] + sway) * Width;
                var y = _y[index] * Height;
                var radius = _size[index] * density;
                _paint.Color = Color.Argb((int)(70 * fade * flicker), 255, 170, 60);
                canvas.DrawCircle(x, y, radius * 3f, _paint);
                _paint.Color = Color.Argb((int)(230 * fade * flicker), 255, 222, 140);
                canvas.DrawCircle(x, y, radius, _paint);
            }

            PostInvalidateOnAnimation();
        }
    }

    // The gold bar: a dark track in a gold frame, filled with a warm gradient up to the progress (or a glowing stretch
    // running along it while the progress is unknown), a light sweeping across the fill and a glow at its leading edge.
    private sealed class LoadingBar : View
    {
        private const long SweepMs = 1600;
        private readonly Func<float> _progress;
        private readonly Paint _track = new() { AntiAlias = true, Color = Color.Argb(200, 14, 12, 10) };
        private readonly Paint _frame = new() { AntiAlias = true, Color = Color.Rgb(196, 152, 72) };
        private readonly Paint _fill = new() { AntiAlias = true };
        private readonly Paint _shine = new() { AntiAlias = true };
        private readonly Paint _glow = new() { AntiAlias = true };
        private readonly RectF _rect = new();
        private readonly Path _clip = new();
        private float _shown;

        public LoadingBar(Context context, Func<float> progress) : base(context)
        {
            _progress = progress;
            _frame.SetStyle(Paint.Style.Stroke);
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            _fill.SetShader(new LinearGradient(0, 0, 0, h, [Color.Rgb(255, 226, 140), Color.Rgb(222, 150, 48), Color.Rgb(150, 82, 20)],
                [0f, .55f, 1f], Shader.TileMode.Clamp));
            _shine.SetShader(new LinearGradient(0, 0, h * 3f, 0, [Color.Transparent, Color.Argb(170, 255, 255, 230), Color.Transparent],
                null, Shader.TileMode.Clamp));
            _glow.SetShader(new RadialGradient(0, 0, h * 1.6f, Color.Argb(200, 255, 214, 120), Color.Transparent, Shader.TileMode.Clamp));
            var inset = h * .12f;
            _frame.StrokeWidth = Math.Max(1f, h * .1f);
            _clip.Reset();
            _clip.AddRoundRect(new RectF(inset, inset, w - inset, h - inset), h / 2f, h / 2f, Path.Direction.Cw!);
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var now = Java.Lang.JavaSystem.CurrentTimeMillis();
            float w = Width;
            float h = Height;
            var radius = h / 2f;
            _rect.Set(0, 0, w, h);
            canvas.DrawRoundRect(_rect, radius, radius, _track);

            var wave = now % SweepMs / (float)SweepMs;
            float from;
            float to;
            var target = _progress?.Invoke() ?? -1f;
            if (target >= 0f)
            {
                // Eases toward the progress so a jump fills smoothly.
                _shown += (Math.Clamp(target, 0f, 1f) - _shown) * .12f;
                from = 0f;
                to = _shown * w;
            }
            else
            {
                var center = (wave * 1.4f - .2f) * w;
                from = Math.Max(0f, center - w * .18f);
                to = Math.Min(w, center + w * .18f);
            }

            canvas.Save();
            canvas.ClipPath(_clip);
            if (to > from)
            {
                _rect.Set(from, 0, to, h);
                canvas.DrawRect(_rect, _fill);
                canvas.Save();
                canvas.ClipRect(from, 0, to, h);
                canvas.Translate(from + (to - from + h * 3f) * wave - h * 3f, 0);
                canvas.DrawRect(0, 0, h * 3f, h, _shine);
                canvas.Restore();
            }

            canvas.Restore();
            if (to > from && target >= 0f)
            {
                canvas.Save();
                canvas.Translate(to, h / 2f);
                var pulse = .8f + .2f * MathF.Sin(now / 140f);
                canvas.Scale(pulse, pulse);
                canvas.DrawCircle(0, 0, h * 1.6f, _glow);
                canvas.Restore();
            }

            var inset = _frame.StrokeWidth / 2f;
            _rect.Set(inset, inset, w - inset, h - inset);
            canvas.DrawRoundRect(_rect, radius, radius, _frame);
            PostInvalidateOnAnimation();
        }
    }
}
