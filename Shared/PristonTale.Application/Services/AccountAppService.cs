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
    NameTaken = 3,
    InvalidEmail = 4,
    EmailTaken = 5,
    InvalidReferral = 6
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

    public Task<Registration> RegisterAsync(string name, string password, CancellationToken ct = default) =>
        RegisterAsync(name, password, null, ct);

    // contact: the e-mail and referral code of the entry page's registration; null for registrations without them.
    public async Task<Registration> RegisterAsync(string name, string password, AccountContact? contact, CancellationToken ct = default)
    {
        if (!AccountRules.IsValidName(name))
        {
            return new Registration(RegistrationResult.InvalidName);
        }

        if (!AccountRules.IsValidPassword(password, name))
        {
            return new Registration(RegistrationResult.InvalidPassword);
        }

        if (contact is not null && !AccountRules.IsValidEmail(contact.Email))
        {
            return new Registration(RegistrationResult.InvalidEmail);
        }

        if (contact is not null && !AccountRules.IsValidReferralCode(contact.ReferralCode))
        {
            return new Registration(RegistrationResult.InvalidReferral);
        }

        var hash = PasswordHasher.Hash(password);
        await RegistrationGate.WaitAsync(ct);
        try
        {
            if (await accounts.IsUserIdTakenAsync(name, ct))
            {
                return new Registration(RegistrationResult.NameTaken);
            }

            if (contact is not null && await accounts.IsEmailTakenAsync(contact.Email, ct))
            {
                return new Registration(RegistrationResult.EmailTaken);
            }

            var account = contact is null ? await accounts.CreateAsync(name, hash, ct) : await accounts.CreateAsync(name, hash, contact, ct);
            return new Registration(RegistrationResult.Created, account);
        }
        catch (DbException) when (!ct.IsCancellationRequested)
        {
            // Another process (the account tool, another site) took the exact name or e-mail between the check and the
            // insert.
            if (await accounts.IsUserIdTakenAsync(name, ct))
            {
                return new Registration(RegistrationResult.NameTaken);
            }

            if (contact is not null && await accounts.IsEmailTakenAsync(contact.Email, ct))
            {
                return new Registration(RegistrationResult.EmailTaken);
            }

            throw;
        }
        finally
        {
            RegistrationGate.Release();
        }
    }
}
