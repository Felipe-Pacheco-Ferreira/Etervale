using System.Diagnostics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Tests;

internal static class FrameTimingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("presentation telemetry uses wall time and includes all updates before a draw", TimingAsync);
        yield return ("scene bridge telemetry separates synchronous calls and resets frame and rolling samples", SceneBridgeAsync);
    }

    private static Task TimingAsync()
    {
        var timing = new FrameTiming();
        var now = Stopwatch.Frequency * 10;
        var frameTicks = Stopwatch.Frequency / 120;
        var millisecond = Stopwatch.Frequency / 1000;
        for (var index = 0; index < 500; index++)
        {
            now += frameTicks;
            timing.RecordUpdate(millisecond, 16);
            timing.RecordUpdate(millisecond, 32);
            timing.RecordDraw(now - millisecond, now, 64);
            timing.RecordPresentation(5 * millisecond, 8);
            timing.RecordPacing(2 * millisecond);
        }

        Assert.True(timing.TrySample(now, out var sample), "no frame timing sample");
        Assert.True(Math.Abs(sample.FramesPerSecond - 120) < 0.1 && sample.FramesOver60HzBudget == 0, "FPS is not based on draw intervals");
        Assert.True(Math.Abs(sample.WorkP95Ms - 3) < 0.01, "multiple updates before one draw were lost");
        Assert.True(Math.Abs(sample.UpdateP95Ms - 2) < 0.01 && Math.Abs(sample.DrawP95Ms - 1) < 0.01,
            "Update and Draw phases were not measured separately");
        Assert.True(Math.Abs(sample.PacingP95Ms - 2) < 0.01, "pacing was lost or added to Update/Draw work");
        Assert.True(sample.AllocatedBytesPerFrame == 120 && sample.FrameP99Ms >= sample.FrameP95Ms, "allocation accounting or percentile order changed");
        Assert.True(Math.Abs(sample.PresentP95Ms - 5) < 0.01 && sample.PresentP99Ms >= sample.PresentP95Ms,
            "presentation wait was lost or counted as Update/Draw work");
        for (var index = 0; index < 360; index++)
        {
            now += Stopwatch.Frequency / 30;
            timing.RecordDraw(now - millisecond, now);
        }

        Assert.True(timing.TrySample(now, out sample) && Math.Abs(sample.FramesPerSecond - 30) < 0.1
            && sample.FramesOver60HzBudget == 360 && sample.FrameP95Ms > 33 && sample.AllocatedBytesPerFrame == 0,
            "rolling window hid slow frames or retained stale samples");
        Assert.True(sample.PresentP95Ms == 0 && sample.PresentP99Ms == 0, "rolling window retained stale presentation waits");
        Assert.True(sample.PacingP95Ms == 0, "rolling window retained stale pacing waits");
        Assert.True(sample.UpdateP95Ms == 0 && Math.Abs(sample.DrawP95Ms - 1) < 0.01,
            "rolling window retained an update from a previous draw");
        return Task.CompletedTask;
    }

    private static Task SceneBridgeAsync()
    {
        var timing = new FrameTiming();
        var millisecond = Stopwatch.Frequency / 1000;
        var now = Stopwatch.Frequency * 10;
        timing.RecordDraw(now, now);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 360; index++)
        {
            timing.RecordUpdate(2 * millisecond);
            timing.RecordUpdate(4 * millisecond);
            timing.RecordSceneBridge(millisecond);
            timing.RecordSceneBridge(2 * millisecond);
            now += 20 * millisecond;
            timing.RecordDraw(now - 5 * millisecond, now);
            timing.RecordPresentation(7 * millisecond);
        }
        var recordingAllocations = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(recordingAllocations == 0, "frame/scene bridge recording allocated");
        Assert.True(timing.TrySample(now, out var sample), "no scene bridge sample");
        Assert.True(Math.Abs(sample.UpdateP95Ms - 6) < 0.01 && Math.Abs(sample.DrawP95Ms - 5) < 0.01
            && Math.Abs(sample.WorkP95Ms - 11) < 0.01 && Math.Abs(sample.PresentP95Ms - 7) < 0.01,
            "bridge time was added to work a second time or phase boundaries changed");
        Assert.True(Math.Abs(sample.SceneBridgeP95Ms - 3) < 0.01 && sample.SceneBridgeCallsPerFrame == 2
            && sample.SceneBridgeCalls == 720 && Math.Abs(sample.SceneBridgeTotalMs - 1080) < 0.01,
            "synchronous scene calls were not accumulated within each draw");

        for (var index = 0; index < 360; index++)
        {
            now += 20 * millisecond;
            timing.RecordDraw(now - millisecond, now);
        }
        Assert.True(timing.TrySample(now, out sample) && sample.SceneBridgeP95Ms == 0
            && sample.SceneBridgeCallsPerFrame == 0 && sample.SceneBridgeCalls == 720,
            "rolling scene bridge window retained pending calls or lost cumulative counts");

        timing = new FrameTiming(); // Browser Enable/Reset creates a fresh diagnostics instance.
        timing.RecordDraw(now, now);
        now += Stopwatch.Frequency;
        timing.RecordSceneBridge(-millisecond);
        timing.RecordDraw(now - millisecond, now);
        Assert.True(timing.TrySample(now, out sample) && sample.UpdateP95Ms == 0
            && sample.SceneBridgeP95Ms == 0 && sample.SceneBridgeCallsPerFrame == 1
            && sample.SceneBridgeCalls == 1 && sample.SceneBridgeTotalMs == 0,
            "reset retained prior phase/bridge samples or a negative duration escaped clamping");
        return Task.CompletedTask;
    }
}
