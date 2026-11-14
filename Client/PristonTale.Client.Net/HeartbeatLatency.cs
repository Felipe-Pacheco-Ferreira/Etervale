namespace PristonTale.Client.Net;

// The round trip of the heartbeats the server echoes with their counter: the local send time of the last few beats is
// kept by counter, and each echo updates a smoothed latency. Beats are sent from one thread and echoes read on another.
public sealed class HeartbeatLatency
{
    public const int Unknown = -1;

    // Beats in flight that can still be matched; an echo older than this many beats is ignored.
    private const int Window = 8;

    // Weight of the newest round trip in the smoothed value.
    private const double Smoothing = .5;

    private readonly long[] _sentAt = new long[Window];
    private readonly uint[] _sentCounter = new uint[Window];
    private readonly object _gate = new();
    private double _smoothed = Unknown;

    public int LatencyMs => (int)Math.Round(Volatile.Read(ref _smoothed));

    public void Sent(uint counter, long nowMs)
    {
        lock (_gate)
        {
            var slot = (int)(counter % Window);
            _sentCounter[slot] = counter;
            _sentAt[slot] = nowMs;
        }
    }

    // Returns the measured round trip, or Unknown when the echo matches no beat in flight.
    public int Answered(uint counter, long nowMs)
    {
        lock (_gate)
        {
            var slot = (int)(counter % Window);
            if (counter == 0 || _sentCounter[slot] != counter)
            {
                return Unknown;
            }

            _sentCounter[slot] = 0;
            var roundTrip = Math.Max(0, nowMs - _sentAt[slot]);
            var smoothed = _smoothed < 0 ? roundTrip : _smoothed + (roundTrip - _smoothed) * Smoothing;
            Volatile.Write(ref _smoothed, smoothed);
            return (int)roundTrip;
        }
    }
}
