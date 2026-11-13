using System.Net;

namespace PristonTale.Server.Net;

// Fixed-window packet counter of one connection; called only from that connection's receive loop.
internal sealed class PacketRateLimiter(int limit, long windowMs)
{
    private long _windowStartMs;
    private int _count;
    private bool _started;

    public bool Admit(long nowMs)
    {
        if (limit <= 0 || windowMs <= 0)
        {
            return true;
        }

        if (!_started || nowMs - _windowStartMs >= windowMs)
        {
            _started = true;
            _windowStartMs = nowMs;
            _count = 0;
        }

        _count++;
        return _count <= limit;
    }
}

// Single-threaded token bucket; capacity 0 disables it.
internal sealed class TokenBucket(int capacity, int perSecond)
{
    private double _tokens = capacity;
    private long _lastMs;
    private bool _started;

    public bool TryTake(long nowMs)
    {
        if (capacity <= 0)
        {
            return true;
        }

        if (_started)
        {
            _tokens = Math.Min(capacity, _tokens + (Math.Max(0, nowMs - _lastMs) * perSecond / 1000.0));
        }

        _started = true;
        _lastMs = nowMs;
        if (_tokens < 1.0)
        {
            return false;
        }

        _tokens -= 1.0;
        return true;
    }
}

// Open connections per remote address, shared by the accept loop and the session cleanup.
internal sealed class ConnectionLimiter(int maxPerAddress)
{
    private readonly Dictionary<IPAddress, int> _counts = new();
    private readonly object _gate = new();

    public bool TryAcquire(IPAddress? address)
    {
        if (maxPerAddress <= 0 || address is null)
        {
            return true;
        }

        lock (_gate)
        {
            var count = _counts.GetValueOrDefault(address);
            if (count >= maxPerAddress)
            {
                return false;
            }

            _counts[address] = count + 1;
            return true;
        }
    }

    public void Release(IPAddress? address)
    {
        if (maxPerAddress <= 0 || address is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!_counts.TryGetValue(address, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                _counts.Remove(address);
            }
            else
            {
                _counts[address] = count - 1;
            }
        }
    }
}

// At most one log line per interval for a noisy category, carrying the count of lines it replaced.
internal sealed class LogSampler(long intervalMs)
{
    private readonly object _gate = new();
    private long _nextMs;
    private bool _started;
    private int _suppressed;

    public bool TryEnter(long nowMs, out int suppressed)
    {
        lock (_gate)
        {
            if (_started && nowMs < _nextMs)
            {
                _suppressed++;
                suppressed = 0;
                return false;
            }

            _started = true;
            _nextMs = nowMs + intervalMs;
            suppressed = _suppressed;
            _suppressed = 0;
            return true;
        }
    }
}
