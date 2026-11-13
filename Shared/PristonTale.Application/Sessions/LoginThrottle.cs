using System.Net;

namespace PristonTale.Application.Sessions;

// Failed logins per account and per address in fixed windows. Blocked attempts are refused before the password
// hash is computed, which bounds both guessing and the PBKDF2 cost an attacker can cause.
public sealed class LoginThrottle(int perAccount, int perAddress, long windowMs)
{
    private const int MaxTrackedKeys = 65536;
    private readonly Dictionary<string, FailureWindow> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<IPAddress, FailureWindow> _addresses = new();
    private readonly object _gate = new();

    private sealed class FailureWindow
    {
        public long StartMs;
        public int Failures;
    }

    public bool IsBlocked(IPAddress? address, string account, long nowMs)
    {
        lock (_gate)
        {
            return Exceeded(_accounts, account, perAccount, nowMs) || address is not null && Exceeded(_addresses, address, perAddress, nowMs);
        }
    }

    public void RecordFailure(IPAddress? address, string account, long nowMs)
    {
        lock (_gate)
        {
            Count(_accounts, account, perAccount, nowMs);
            if (address is not null)
            {
                Count(_addresses, address, perAddress, nowMs);
            }
        }
    }

    public void RecordSuccess(string account)
    {
        lock (_gate)
        {
            _accounts.Remove(account);
        }
    }

    // Accounts whose logins are refused now, with the time left until their window ends.
    public IReadOnlyList<(string Account, long RemainingMs)> BlockedAccounts(long nowMs)
    {
        lock (_gate)
        {
            return _accounts.Where(pair => Exceeded(_accounts, pair.Key, perAccount, nowMs))
                .Select(pair => (pair.Key, windowMs - (nowMs - pair.Value.StartMs)))
                .ToArray();
        }
    }

    // Forgets every failure, of accounts and addresses; returns how many accounts were blocked.
    public int Clear(long nowMs)
    {
        lock (_gate)
        {
            var blocked = _accounts.Keys.Count(key => Exceeded(_accounts, key, perAccount, nowMs));
            _accounts.Clear();
            _addresses.Clear();
            return blocked;
        }
    }

    // Forgets an account's failures; true when it was blocked.
    public bool Release(string account, long nowMs)
    {
        lock (_gate)
        {
            var blocked = Exceeded(_accounts, account, perAccount, nowMs);
            _accounts.Remove(account);
            return blocked;
        }
    }

    public void Prune(long nowMs)
    {
        lock (_gate)
        {
            Prune(_accounts, nowMs);
            Prune(_addresses, nowMs);
        }
    }

    private bool Exceeded<TKey>(Dictionary<TKey, FailureWindow> windows, TKey key, int limit, long nowMs) where TKey : notnull
    {
        return limit > 0 && windows.TryGetValue(key, out var window) && nowMs - window.StartMs < windowMs && window.Failures >= limit;
    }

    private void Count<TKey>(Dictionary<TKey, FailureWindow> windows, TKey key, int limit, long nowMs) where TKey : notnull
    {
        if (limit <= 0)
        {
            return;
        }

        if (!windows.TryGetValue(key, out var window))
        {
            if (windows.Count >= MaxTrackedKeys)
            {
                Prune(windows, nowMs);
                if (windows.Count >= MaxTrackedKeys)
                {
                    return;
                }
            }

            window = new FailureWindow { StartMs = nowMs };
            windows.Add(key, window);
        }
        else if (nowMs - window.StartMs >= windowMs)
        {
            window.StartMs = nowMs;
            window.Failures = 0;
        }

        window.Failures++;
    }

    private void Prune<TKey>(Dictionary<TKey, FailureWindow> windows, long nowMs) where TKey : notnull
    {
        foreach (var (key, window) in windows)
        {
            if (nowMs - window.StartMs >= windowMs)
            {
                windows.Remove(key);
            }
        }
    }
}
