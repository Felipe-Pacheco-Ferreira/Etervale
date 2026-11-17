namespace PristonTale.Persistence.Abstractions.Accounts;

// What a new account registered on the website carries besides its name and password: the e-mail (stored in lower
// case, one account per address) and the referral code typed at registration (empty when none).
public sealed record AccountContact(string Email, string ReferralCode);

public interface IAccountRepository
{
    Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default);
    Task<AccountRecord> CreateAsync(string userId, string passwordHash, CancellationToken ct = default);
    Task<AccountRecord> CreateAsync(string userId, string passwordHash, AccountContact contact, CancellationToken ct = default);
    Task UpdateLastLoginAsync(long accountId, CancellationToken ct = default);

    // True when an account has this name in any letter case (names of new accounts are ASCII, see AccountRules).
    Task<bool> IsUserIdTakenAsync(string userId, CancellationToken ct = default);

    // True when an account already has this e-mail (compared in lower case).
    Task<bool> IsEmailTakenAsync(string email, CancellationToken ct = default);
}
