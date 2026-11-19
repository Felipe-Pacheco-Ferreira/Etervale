using System.Diagnostics;
using System.Runtime.InteropServices;
using PristonTale.Assets;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class PoseMemoizationBenchmark
{
    private const int Frames = 4000;

    public static void Run(string assetRoot)
    {
        var reader = new PristonSmdReader();
        var models = new PristonModel[6];
        for (var index = 0; index < models.Length; index++)
        {
            var name = $"m{index + 1}.preview.smb";
            models[index] = reader.ReadModel(AssetFiles.Resolve(Path.Combine(assetRoot, "char", "tmABCD", name)));
        }
        var uncached = new PristonModelAnimator[models.Length * 2];
        var memoized = new PristonModelAnimator[uncached.Length];
        var durations = models.Select(model => Math.Max(1, model.LastFrame - 160)).ToArray();
        var cache = new PristonPoseMemoization();
        for (var index = 0; index < uncached.Length; index++)
        {
            uncached[index] = new PristonModelAnimator(models[index / 2]);
            memoized[index] = new PristonModelAnimator(models[index / 2]);
            memoized[index].SetMemoization(cache);
        }
        Console.WriteLine($"POSE-MEMO {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; roots={uncached.Length}; bones={models.Sum(model => model.Objects.Count) * 2}");
        Console.WriteLine("POSE-MEMO CPU-only exact body/head sample pairs; excludes actor palettes, scene rendering, transfer and GPU work");
        for (var index = 0; index < 3; index++)
        {
            Measure(uncached, durations, null, frames: 500);
            Measure(memoized, durations, cache, frames: 500);
        }
        for (var run = 0; run < 5; run++)
        {
            var first = run % 2 == 0 ? uncached : memoized;
            var second = run % 2 == 0 ? memoized : uncached;
            var a = Measure(first, durations, ReferenceEquals(first, memoized) ? cache : null, Frames);
            var b = Measure(second, durations, ReferenceEquals(second, memoized) ? cache : null, Frames);
            var baseline = run % 2 == 0 ? a : b;
            var optimized = run % 2 == 0 ? b : a;
            Assert.True(BitConverter.DoubleToInt64Bits(baseline.Checksum) == BitConverter.DoubleToInt64Bits(optimized.Checksum),
                "CPU benchmark outputs differ");
            Console.WriteLine($"POSE-MEMO run={run + 1}; baselineMs/frame={baseline.Milliseconds / Frames:F6}; cachedMs/frame={optimized.Milliseconds / Frames:F6}; "
                + $"baselineBytes/frame={(double)baseline.Bytes / Frames:F2}; cachedBytes/frame={(double)optimized.Bytes / Frames:F2}; checksum={baseline.Checksum:R}");
        }
        foreach (var animator in memoized)
        {
            animator.SetMemoization(null!);
        }
        Assert.True(cache.Count == 0, "benchmark retained skeletons");
    }

    private static Measurement Measure(PristonModelAnimator[] animators, ReadOnlySpan<int> durations, PristonPoseMemoization? cache, int frames)
    {
        var checksum = 0d;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var iteration = 0; iteration < frames; iteration++)
        {
            cache?.Invalidate();
            for (var index = 0; index < animators.Length; index++)
            {
                var frame = 160f + iteration * 80.80808f % durations[index / 2];
                var pose = animators[index].Evaluate(frame);
                var matrix = pose[iteration % pose.Length];
                checksum += matrix.M11 + matrix.M23 + matrix.M32 + matrix.M41 + matrix.M42 + matrix.M43;
            }
        }
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return new(elapsed, GC.GetAllocatedBytesForCurrentThread() - allocated, checksum);
    }

    private readonly record struct Measurement(double Milliseconds, long Bytes, double Checksum);
}
