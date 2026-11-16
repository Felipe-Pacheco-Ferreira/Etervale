using System.Diagnostics;
using System.Text;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

// Where a world frame's time goes on the phone (docs/android.md, "Frame rate"): the game thread's update, the HUD
// portraits, the scene and the rest of the draw, the time left for presenting and waiting on the display, collections, and
// the renderer's own sections (PristonFrameProfiler), on the CPU and, once detailed, on the GPU. The game thread counts;
// once per Period it publishes a Report for the HUD's readout, and, while detailed, writes it to the log (Etervale.Perf).
internal sealed class VulkanFrameMeter
{
    private const long PeriodMs = 1000;
    // The renderer's sections the report lists, slowest first, at most this many.
    private const int ListedSections = 7;

    // A section shown in the report: its name and mean milliseconds on the CPU and the GPU (0 when not timed).
    internal readonly record struct SectionTime(string Name, double CpuMs, double GpuMs);

    // One period's means, in milliseconds per drawn frame.
    internal sealed record Report(
        int Fps,
        double FrameMs,
        double UpdateMs,
        double UpdatesPerFrame,
        double PortraitsMs,
        double SceneMs,
        double DrawRestMs,
        double PresentMs,
        double SceneGpuMs,
        bool GpuTimed,
        int Collections,
        int BlockingCollections,
        IReadOnlyList<SectionTime> Sections)
    {
        // The text of the detailed readout and of the log line.
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append($"quadro {FrameMs:0.0} ms · {UpdatesPerFrame:0.0} upd/quadro\n");
            text.Append($"CPU upd {UpdateMs:0.0} · retr {PortraitsMs:0.0} · cena {SceneMs:0.0} · hud {DrawRestMs:0.0}\n");
            text.Append($"apresentar/espera {PresentMs:0.0}");
            text.Append(GpuTimed ? $" · GPU cena {SceneGpuMs:0.0}\n" : " · GPU --\n");
            text.Append($"GC {Collections}/s ({BlockingCollections} gen1+)\n");
            foreach (var section in Sections)
            {
                text.Append(GpuTimed ? $"{section.Name,-9} c{section.CpuMs,4:0.0} g{section.GpuMs,4:0.0}\n" : $"{section.Name,-9} c{section.CpuMs,4:0.0}\n");
            }

            return text.ToString().TrimEnd();
        }
    }

    private static readonly (RenderSection Section, string Name)[] Shown =
    [
        (RenderSection.SunShadow, "sombra"),
        (RenderSection.LampShadows, "sombra lâmp"),
        (RenderSection.Sky, "céu"),
        (RenderSection.Opaque, "opaco"),
        (RenderSection.Prepass, "pré-passe"),
        (RenderSection.Masked, "recortado"),
        (RenderSection.Transparent, "transp"),
        (RenderSection.Glow, "brilho"),
        (RenderSection.Effects, "efeitos"),
        (RenderSection.Queue, "fila"),
        (RenderSection.Lighting, "luzes"),
        (RenderSection.Update, "anim"),
        (RenderSection.Environment, "ambiente"),
        (RenderSection.Exposure, "exposição"),
        (RenderSection.Bloom, "bloom"),
        (RenderSection.Temporal, "temporal"),
        (RenderSection.DisplayTransform, "tela"),
        (RenderSection.PostAntiAliasing, "AA")
    ];

    private readonly SectionTime[] _sections = new SectionTime[Shown.Length];
    private long _periodStart = -1;
    private long _updateStart;
    private long _drawStart;
    private long _partStart;
    private long _lastDrawEnd = -1;
    private int _frames;
    private int _updates;
    private double _update;
    private double _updateSinceDraw;
    private double _portraits;
    private double _scene;
    private double _draw;
    private double _present;
    private int _gen0;
    private int _gen1;
    private int _detailed;
    private PristonRenderer _timedRenderer;
    private Report _report;

    // Set from the UI thread when the readout opens its details; the GPU timers start then.
    public bool Detailed
    {
        get => Volatile.Read(ref _detailed) != 0;
        set => Volatile.Write(ref _detailed, value ? 1 : 0);
    }

    public Report Latest => Volatile.Read(ref _report);

    public void UpdateBegan() => _updateStart = Stopwatch.GetTimestamp();

    public void UpdateEnded()
    {
        var spent = Stopwatch.GetElapsedTime(_updateStart).TotalMilliseconds;
        _update += spent;
        _updateSinceDraw += spent;
        _updates++;
    }

    public void DrawBegan(PristonRenderer renderer)
    {
        renderer.Profiler.PrepareFrame();
        _drawStart = Stopwatch.GetTimestamp();
        if (_lastDrawEnd >= 0)
        {
            // From the end of the last draw to now, less this frame's updates: presenting and waiting on the display.
            var gap = Stopwatch.GetElapsedTime(_lastDrawEnd, _drawStart).TotalMilliseconds;
            _present += Math.Max(0, gap - _updateSinceDraw);
        }

        _updateSinceDraw = 0;
    }

    public void PartBegan() => _partStart = Stopwatch.GetTimestamp();

    public void PortraitsEnded() => _portraits += Stopwatch.GetElapsedTime(_partStart).TotalMilliseconds;

    public void SceneEnded() => _scene += Stopwatch.GetElapsedTime(_partStart).TotalMilliseconds;

    // A world frame drawn: counts it and, once per Period, publishes the report. Draws before the world have no renderer.
    public void DrawEnded(PristonRenderer renderer, Microsoft.Xna.Framework.Graphics.GraphicsDevice device)
    {
        var end = Stopwatch.GetTimestamp();
        _draw += Stopwatch.GetElapsedTime(_drawStart, end).TotalMilliseconds;
        _lastDrawEnd = end;
        _frames++;
        if (Detailed && renderer is not null && renderer != _timedRenderer)
        {
            _timedRenderer = renderer;
            if (!renderer.Profiler.EnableGpuTimers(device))
            {
                Android.Util.Log.Warn("Etervale.Perf", "this GPU has no timestamp queries: CPU times only");
            }
        }

        var now = System.Environment.TickCount64;
        if (_periodStart < 0)
        {
            Restart(now, renderer);
            return;
        }

        var elapsed = now - _periodStart;
        if (elapsed < PeriodMs || _frames == 0)
        {
            return;
        }

        var frames = (double)_frames;
        var profiler = renderer?.Profiler;
        var gpu = profiler?.GpuTimersAvailable == true;
        for (var index = 0; index < Shown.Length; index++)
        {
            var (section, name) = Shown[index];
            _sections[index] = new SectionTime(name, profiler?.CpuMean(section) ?? 0, gpu ? profiler.GpuMean(section) : 0);
        }

        var listed = _sections.Where(section => section.CpuMs + section.GpuMs >= .05)
            .OrderByDescending(section => Math.Max(section.CpuMs, section.GpuMs))
            .Take(ListedSections)
            .ToArray();
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var seconds = elapsed / 1000d;
        var report = new Report(
            (int)Math.Round(frames / seconds),
            elapsed / frames,
            _update / frames,
            _updates / frames,
            _portraits / frames,
            _scene / frames,
            Math.Max(0, _draw - _portraits - _scene) / frames,
            _present / frames,
            gpu ? profiler.GpuMean(RenderSection.Frame) : 0,
            gpu,
            (int)Math.Round((gen0 - _gen0) / seconds),
            (int)Math.Round((gen1 - _gen1) / seconds),
            listed);
        Volatile.Write(ref _report, report);
        if (Detailed)
        {
            Android.Util.Log.Info("Etervale.Perf", $"{report.Fps} fps\n{report.Describe()}");
        }

        Restart(now, renderer);
    }

    private void Restart(long now, PristonRenderer renderer)
    {
        _periodStart = now;
        _frames = 0;
        _updates = 0;
        _update = 0;
        _portraits = 0;
        _scene = 0;
        _draw = 0;
        _present = 0;
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        renderer?.Profiler.ResetAverages();
    }
}
