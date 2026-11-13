using System.Net;
using System.Threading.RateLimiting;
using PristonTale.Web.Hosting;

namespace PristonTale.Web.Services;

// Registrations one address may attempt per window (Security:RegistrationsPerIp per RegistrationWindowMinutes). Only a
// well-formed form counts, so a typo does not use up an attempt, while trying names that are taken does.
public sealed class RegistrationGate : IDisposable
{
    private readonly PartitionedRateLimiter<IPAddress> _limiter;

    public RegistrationGate(WebSettings settings)
    {
        var security = settings.Security;
        _limiter = PartitionedRateLimiter.Create<IPAddress, IPAddress>(address => RateLimitPartition.GetFixedWindowLimiter(address,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = security.RegistrationsPerIp,
                Window = TimeSpan.FromMinutes(security.RegistrationWindowMinutes),
                QueueLimit = 0
            }));
    }

    public bool TryEnter(IPAddress address)
    {
        using var lease = _limiter.AttemptAcquire(address);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
