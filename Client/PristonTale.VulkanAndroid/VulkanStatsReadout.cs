using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Widget;
using PristonTale.Client.Net;

// The small "60 fps · 34 ms" line: frames drawn per second of the world and the server's heartbeat round trip, the
// latency in green, yellow or red. A tap opens the frame's details below it (VulkanFrameMeter) and another closes them.
// Updated on the UI thread from the HUD's refresh; it recounts once per Period.
internal sealed class VulkanStatsReadout : TextView
{
    private const long PeriodMs = 1000;
    private const int GoodLatencyMs = 80;
    private const int FairLatencyMs = 200;

    private static readonly Color Good = Color.Rgb(120, 214, 120);
    private static readonly Color Fair = Color.Rgb(232, 200, 90);
    private static readonly Color Poor = Color.Rgb(232, 96, 80);
    private static readonly Color DetailsBackground = Color.Argb(180, 0, 0, 0);

    private long _sinceMs = -1;
    private long _sinceFrames;
    private bool _detailed;

    public VulkanStatsReadout(Context context)
        : base(context)
    {
        TextSize = 10;
        SetTextColor(Color.Rgb(222, 222, 214));
        SetShadowLayer(3f, 0f, 1f, Color.Black);
        Typeface = Typeface.Monospace;
        Text = "-- fps · -- ms";
        Click += (_, _) =>
        {
            _detailed = !_detailed;
            SetBackgroundColor(_detailed ? DetailsBackground : Color.Transparent);
            var pad = _detailed ? (int)(6 * Resources!.DisplayMetrics!.Density) : 0;
            SetPadding(pad, pad, pad, pad);
            _sinceMs = -1;
        };
    }

    public void Update(long framesDrawn, int latencyMs, long nowMs, VulkanFrameMeter meter)
    {
        if (meter is not null)
        {
            meter.Detailed = _detailed;
        }

        if (_sinceMs < 0)
        {
            _sinceMs = nowMs;
            _sinceFrames = framesDrawn;
            return;
        }

        var elapsed = nowMs - _sinceMs;
        if (elapsed < PeriodMs)
        {
            return;
        }

        var fps = (int)Math.Round((framesDrawn - _sinceFrames) * 1000d / elapsed);
        _sinceMs = nowMs;
        _sinceFrames = framesDrawn;
        var frames = $"{fps} fps · ";
        var latency = latencyMs == HeartbeatLatency.Unknown ? "-- ms" : $"{latencyMs} ms";
        var details = _detailed && meter?.Latest is { } report ? "\n" + report.Describe() : string.Empty;
        using var line = new SpannableString(frames + latency + details);
        var color = latencyMs == HeartbeatLatency.Unknown ? new Color(CurrentTextColor)
            : latencyMs < GoodLatencyMs ? Good
            : latencyMs < FairLatencyMs ? Fair
            : Poor;
        using var span = new ForegroundColorSpan(color);
        line.SetSpan(span, frames.Length, frames.Length + latency.Length, SpanTypes.ExclusiveExclusive);
        SetText(line, BufferType.Spannable);
    }
}
