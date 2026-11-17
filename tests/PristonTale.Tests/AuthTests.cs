using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Security;

namespace PristonTale.Tests;

internal static class AuthTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("server login requires an existing account and a valid password", LoginAsync);
        yield return ("credential check: a blocked account is told apart only with its password", BlockedAsync);
        yield return ("account rules: names are 4 to 16 ASCII letters and digits from a letter; passwords 8 to 64 within the login field",
            RulesAsync);
        yield return ("registration creates a hashed account and refuses bad names, weak passwords and names taken in any case", RegisterAsync);
        yield return ("the entry page's registration keeps the e-mail in lower case once per account and an optional referral code",
            RegisterContactAsync);
    }

    private static async Task RegisterContactAsync()
    {
        var accounts = new AccountRepository();
        var service = new AccountAppService(accounts);
        Assert.True((await service.RegisterAsync("Hero", "segredo123", new AccountContact("sem-arroba", ""))).Result == RegistrationResult.InvalidEmail,
            "an address without @");
        Assert.True((await service.RegisterAsync("Hero", "segredo123", new AccountContact("hero@avalon.gg", "com espaço"))).Result
            == RegistrationResult.InvalidReferral, "a referral code with a space");
        Assert.True(accounts.CreateCalls == 0, "a refused registration reached storage");

        var created = await service.RegisterAsync("Hero", "segredo123", new AccountContact("Hero@Avalon.GG", "AMIGO_1"));
        Assert.True(created.Result == RegistrationResult.Created && accounts.Contacts[created.Account!.Id] == new AccountContact("hero@avalon.gg", "AMIGO_1"),
            "the account keeps its e-mail in lower case and its referral code");
        Assert.True((await service.RegisterAsync("Other", "segredo123", new AccountContact("HERO@avalon.gg", ""))).Result == RegistrationResult.EmailTaken
            && accounts.CreateCalls == 1, "an e-mail already used, in any case");
        Assert.True((await service.RegisterAsync("Third", "segredo123", new AccountContact("third@avalon.gg", ""))).Result == RegistrationResult.Created,
            "the referral code is optional");
    }

    private static async Task LoginAsync()
    {
        var accounts = new AccountRepository();
        var auth = new AuthAppService(new SessionRegistry(), accounts);
        var unknown = await auth.LoginAsync("missing", "password");
        Assert.True(!unknown.Ok && accounts.CreateCalls == 0 && accounts.LoginUpdates == 0,
            "unknown login created an account or session");

        accounts.Records.Add(new AccountRecord(1, "player", PasswordHasher.Hash("correct"), false, false));
        var wrong = await auth.LoginAsync("player", "wrong");
        Assert.True(!wrong.Ok && accounts.LoginUpdates == 0, "wrong password updated the account login");

        accounts.Records[0] = accounts.Records[0] with { IsBlocked = true };
        var blocked = await auth.LoginAsync("player", "correct");
        Assert.True(!blocked.Ok && blocked.Failure == LoginFailure.LoginDenied && accounts.LoginUpdates == 0, "blocked account logged in");

        accounts.Records[0] = accounts.Records[0] with { IsBlocked = false };
        var valid = await auth.LoginAsync("player", "correct");
        Assert.True(valid.Ok && valid.SessionToken.Length > 0 && accounts.LoginUpdates == 1,
            "valid account did not receive a session");
    }

    private static async Task BlockedAsync()
    {
        var accounts = new AccountRepository();
        accounts.Records.Add(new AccountRecord(1, "player", PasswordHasher.Hash("correct"), true, false));
        var service = new AccountAppService(accounts);
        var guessed = await service.CheckAsync("player", "wrong");
        Assert.True(guessed.Account is null && guessed.Failure == LoginFailure.BadCredentials, "a wrong password revealed the block");
        var known = await service.CheckAsync("player", "correct");
        Assert.True(known.Account is null && known.Failure == LoginFailure.LoginDenied, "the right password did not tell the block");
        var empty = await service.CheckAsync(" ", "correct");
        Assert.True(empty.Account is null && empty.Failure == LoginFailure.BadCredentials && accounts.Finds == 2, "an empty name reached storage");
    }

    private static Task RulesAsync()
    {
        Assert.True(AccountRules.IsValidName("Hero") && AccountRules.IsValidName("a234567890123456"), "the shortest and longest names");
        Assert.True(!AccountRules.IsValidName("abc") && !AccountRules.IsValidName("a2345678901234567") && !AccountRules.IsValidName(null),
            "names out of length");
        Assert.True(!AccountRules.IsValidName("1hero") && !AccountRules.IsValidName("he ro") && !AccountRules.IsValidName("héro")
            && !AccountRules.IsValidName("hero_1"), "names with a digit first, a space, an accent or a symbol");
        Assert.True(AccountRules.IsValidPassword("segredo!", "hero") && AccountRules.IsValidPassword(new string('é', 64), "hero"),
            "passwords of 8 characters and of 64 two-byte characters");
        Assert.True(!AccountRules.IsValidPassword("1234567", "hero") && !AccountRules.IsValidPassword(new string('a', 65), "hero")
            && !AccountRules.IsValidPassword(new string('€', 43), "hero"), "passwords too short, too long or past the login field");
        Assert.True(!AccountRules.IsValidPassword("HeroHero", "herohero") && !AccountRules.IsValidPassword("pass\tword", "hero"),
            "a password equal to the name or with a control character");
        return Task.CompletedTask;
    }

    private static async Task RegisterAsync()
    {
        var accounts = new AccountRepository();
        var service = new AccountAppService(accounts);
        Assert.True((await service.RegisterAsync("x", "segredo123")).Result == RegistrationResult.InvalidName, "a short name");
        Assert.True((await service.RegisterAsync("Hero", "short")).Result == RegistrationResult.InvalidPassword, "a short password");
        Assert.True(accounts.CreateCalls == 0, "a refused registration reached storage");

        Assert.True((await service.RegisterAsync("Hero", "segredo123")).Result == RegistrationResult.Created, "a valid registration");
        var created = accounts.Records.Single();
        Assert.True(created.UserId == "Hero" && created.PasswordHash != "segredo123" && PasswordHasher.Verify("segredo123", created.PasswordHash),
            "the account keeps its name and only a hash of its password");
        Assert.True((await service.RegisterAsync("hERO", "outrasenha")).Result == RegistrationResult.NameTaken && accounts.CreateCalls == 1,
            "the same name in another case");
        Assert.True((await service.CheckAsync("Hero", "segredo123")).Account is not null, "the new account logs in");

        accounts.RaceOnCreate = true;
        Assert.True((await service.RegisterAsync("Racer", "segredo123")).Result == RegistrationResult.NameTaken, "a name taken while creating");

        accounts.RaceOnCreate = false;
        var twins = await Task.WhenAll(service.RegisterAsync("Twin", "segredo123"), service.RegisterAsync("tWIN", "segredo123"));
        Assert.True(twins.Count(static twin => twin.Result == RegistrationResult.Created) == 1, "two cases of one name registered together");
    }

    private sealed class AccountRepository : IAccountRepository
    {
        public List<AccountRecord> Records { get; } = [];
        public int CreateCalls { get; private set; }
        public int LoginUpdates { get; private set; }
        public int Finds { get; private set; }

        // The name is taken by another request between the check and the insert, which storage refuses.
        public bool RaceOnCreate { get; set; }

        public Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default)
        {
            Finds++;
            return Task.FromResult(Records.FirstOrDefault(record => record.UserId == userId));
        }

        public Task<AccountRecord> CreateAsync(string userId, string passwordHash, CancellationToken ct = default)
        {
            CreateCalls++;
            var record = new AccountRecord(Records.Count + 1, userId, passwordHash, false, false);
            Records.Add(record);
            if (RaceOnCreate)
            {
                throw new FakeDbException();
            }

            return Task.FromResult(record);
        }

        public Dictionary<long, AccountContact> Contacts { get; } = [];

        public async Task<AccountRecord> CreateAsync(string userId, string passwordHash, AccountContact contact, CancellationToken ct = default)
        {
            var record = await CreateAsync(userId, passwordHash, ct);
            Contacts[record.Id] = contact with { Email = contact.Email.ToLowerInvariant() };
            return record;
        }

        public Task<bool> IsEmailTakenAsync(string email, CancellationToken ct = default) =>
            Task.FromResult(Contacts.Values.Any(contact => contact.Email == email.ToLowerInvariant()));

        public Task UpdateLastLoginAsync(long accountId, CancellationToken ct = default)
        {
            LoginUpdates++;
            return Task.CompletedTask;
        }

        // Yields first, as storage does, so registrations sent together interleave here.
        public async Task<bool> IsUserIdTakenAsync(string userId, CancellationToken ct = default)
        {
            await Task.Yield();
            return Records.Any(record => string.Equals(record.UserId, userId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class FakeDbException() : System.Data.Common.DbException("unique constraint");
}
