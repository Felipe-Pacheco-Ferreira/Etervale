using PristonTale.Client.Net;

namespace PristonTale.Tests;

internal static class HeartbeatLatencyTests
{
    private const int Unknown = HeartbeatLatency.Unknown;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("latency: an echoed heartbeat gives its round trip, later ones are smoothed", SmoothedAsync);
        yield return ("latency: an echo of no beat in flight, a repeated echo or an overwritten beat is ignored", IgnoredAsync);
    }

    private static Task SmoothedAsync()
    {
        var latency = new HeartbeatLatency();
        Is(Unknown, latency.LatencyMs, "latency before any echo");
        latency.Sent(1, 1_000);
        Is(40, latency.Answered(1, 1_040), "first round trip");
        Is(40, latency.LatencyMs, "latency after the first echo");
        latency.Sent(2, 3_000);
        Is(120, latency.Answered(2, 3_120), "second round trip");
        Is(80, latency.LatencyMs, "latency smoothed over both echoes");
        return Task.CompletedTask;
    }

    private static Task IgnoredAsync()
    {
        var latency = new HeartbeatLatency();
        Is(Unknown, latency.Answered(5, 100), "an echo of a beat never sent");
        latency.Sent(1, 1_000);
        Is(30, latency.Answered(1, 1_030), "the round trip");
        Is(Unknown, latency.Answered(1, 1_500), "the same echo again");
        Is(30, latency.LatencyMs, "latency after an ignored echo");
        // Beat 9 takes beat 1's slot: an echo of 1 arriving that late matches nothing.
        latency.Sent(1, 2_000);
        latency.Sent(9, 2_100);
        Is(Unknown, latency.Answered(1, 2_200), "an echo of an overwritten beat");
        Is(50, latency.Answered(9, 2_150), "the beat that took its slot");
        return Task.CompletedTask;
    }

    private static void Is(int expected, int actual, string what) =>
        Assert.True(expected == actual, $"{what}: expected {expected}, got {actual}");
}
