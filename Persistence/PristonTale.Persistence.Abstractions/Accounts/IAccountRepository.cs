namespace PristonTale.Persistence.Abstractions.Accounts;

public interface IAccountRepository
{
    Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default);
    Task<AccountRecord> CreateAsync(string userId, string passwordHash, CancellationToken ct = default);
    Task UpdateLastLoginAsync(long accountId, CancellationToken ct = default);

    // True when an account has this name in any letter case (names of new accounts are ASCII, see AccountRules).
    Task<bool> IsUserIdTakenAsync(string userId, CancellationToken ct = default);
}
