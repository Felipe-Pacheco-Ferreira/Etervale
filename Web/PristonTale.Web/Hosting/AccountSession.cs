using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PristonTale.Persistence.Abstractions.Accounts;

namespace PristonTale.Web.Hosting;

// The signed-in account's session: an encrypted, HTTP-only cookie holding the account's id, name and password stamp.
public static class AccountSession
{
    public const string CookieName = "__Host-pa-session";
    public const string ReturnParameter = "voltar";
    private const string StampClaim = "pa:stamp";
    private const int StampBytes = 16;

    public static void Configure(CookieAuthenticationOptions options, SecuritySettings security)
    {
        options.Cookie.Name = CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(security.SessionMinutes);
        options.SlidingExpiration = true;
        options.LoginPath = SitePaths.SignIn;
        options.LogoutPath = SitePaths.SignOut;
        options.AccessDeniedPath = SitePaths.SignIn;
        options.ReturnUrlParameter = ReturnParameter;
        options.Events.OnValidatePrincipal = ValidateAsync;
    }

    public static ClaimsPrincipal PrincipalOf(AccountRecord account) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, account.Id.ToString(CultureInfo.InvariantCulture)),
        new Claim(ClaimTypes.Name, account.UserId),
        new Claim(StampClaim, Stamp(account.PasswordHash))
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    // A digest of the stored hash: a new password ends the sessions opened with the old one, and the cookie never holds the hash.
    public static string Stamp(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)), 0, StampBytes);

    // True when the session still belongs to the account as stored: same id and password, and not blocked.
    public static bool Matches(ClaimsPrincipal principal, AccountRecord? account) =>
        account is { IsBlocked: false }
        && principal.FindFirstValue(ClaimTypes.NameIdentifier) == account.Id.ToString(CultureInfo.InvariantCulture)
        && principal.FindFirstValue(StampClaim) == Stamp(account.PasswordHash);

    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var name = context.Principal?.Identity?.Name;
        var accounts = context.HttpContext.RequestServices.GetRequiredService<IAccountRepository>();
        var account = name is null ? null : await accounts.FindByUserIdAsync(name, context.HttpContext.RequestAborted);
        if (context.Principal is null || !Matches(context.Principal, account))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
