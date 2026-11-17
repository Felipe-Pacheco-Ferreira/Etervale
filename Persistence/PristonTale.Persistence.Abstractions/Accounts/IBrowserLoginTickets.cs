namespace PristonTale.Persistence.Abstractions.Accounts;

public interface IBrowserLoginTickets
{
    Task IssueAsync(string hash, long accountId, string passwordStamp, long now, long expires, CancellationToken ct);
    Task<bool> RedeemAsync(string hash, long accountId, string passwordStamp, long now, CancellationToken ct);
}
