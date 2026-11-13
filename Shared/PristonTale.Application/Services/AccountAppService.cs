using System.Data.Common;
using PristonTale.Contracts.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Security;

namespace PristonTale.Application.Services;

public readonly record struct CredentialCheck(AccountRecord? Account, LoginFailure Failure);

public enum RegistrationResult
{
    Created = 0,
    InvalidName = 1,
    InvalidPassword = 2,
    NameTaken = 3
}

// Account is the new account when Result is Created.
public readonly record struct Registration(RegistrationResult Result, AccountRecord? Account = null);

// Accounts outside a game session: the credential check every login runs (the game's and the website's) and the
// registration of a new account.
public sealed class AccountAppService(IAccountRepository accounts)
{
    // The hash of a password nobody knows, verified for an unknown account so it answers in the time a known one takes.
    private static readonly string UnknownAccountHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N"));

    // Storage's unique name is exact, while names are taken in any letter case: registrations of one process run one
    // at a time, so "Hero" and "hero" sent together cannot both pass the check.
    private static readonly SemaphoreSlim RegistrationGate = new(1, 1);

    // The password is checked before the account's state, so a blocked account is told apart only with its password.
    public async Task<CredentialCheck> CheckAsync(string account, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
        {
            return new CredentialCheck(null, LoginFailure.BadCredentials);
        }

        var record = await accounts.FindByUserIdAsync(account, ct);
        if (!PasswordHasher.Verify(password, record?.PasswordHash ?? UnknownAccountHash) || record is null)
        {
            return new CredentialCheck(null, LoginFailure.BadCredentials);
        }

        return record.IsBlocked ? new CredentialCheck(null, LoginFailure.LoginDenied) : new CredentialCheck(record, LoginFailure.None);
    }

    public async Task<Registration> RegisterAsync(string name, string password, CancellationToken ct = default)
    {
        if (!AccountRules.IsValidName(name))
        {
            return new Registration(RegistrationResult.InvalidName);
        }

        if (!AccountRules.IsValidPassword(password, name))
        {
            return new Registration(RegistrationResult.InvalidPassword);
        }

        var hash = PasswordHasher.Hash(password);
        await RegistrationGate.WaitAsync(ct);
        try
        {
            if (await accounts.IsUserIdTakenAsync(name, ct))
            {
                return new Registration(RegistrationResult.NameTaken);
            }

            return new Registration(RegistrationResult.Created, await accounts.CreateAsync(name, hash, ct));
        }
        catch (DbException) when (!ct.IsCancellationRequested)
        {
            // Another process (the account tool, another site) took the exact name between the check and the insert.
            if (await accounts.IsUserIdTakenAsync(name, ct))
            {
                return new Registration(RegistrationResult.NameTaken);
            }

            throw;
        }
        finally
        {
            RegistrationGate.Release();
        }
    }
}
