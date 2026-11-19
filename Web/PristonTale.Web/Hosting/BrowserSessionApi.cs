using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PristonTale.Application.Services;
using PristonTale.Application.Support;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;

namespace PristonTale.Web.Hosting;

internal static class BrowserSessionApi
{
    public const string Scheme = "Avalon.Browser";
    private const string Path = "/api/play-session";
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public static void Configure(CookieAuthenticationOptions options, SecuritySettings security)
    {
        AccountSession.Configure(options, security);
        options.Cookie.Name = "__Host-pa-play";
        options.ExpireTimeSpan = Lifetime;
        options.SlidingExpiration = false;
    }

    public static void Map(WebApplication app)
    {
        app.MapGet(Path, (Delegate)CurrentAsync);
        app.MapPost(Path, RememberAsync).RequireRateLimiting(WebHosting.FormPostsPolicy);
        app.MapPost(Path + "/ticket", IssueAsync).RequireRateLimiting(WebHosting.FormPostsPolicy);
        app.MapPost(Path + "/attachment/{ticketId:guid}", AttachAsync).RequireRateLimiting(WebHosting.FormPostsPolicy);
        app.MapDelete(Path, (Delegate)ForgetAsync).RequireRateLimiting(WebHosting.FormPostsPolicy);
    }

    internal sealed record Credentials(string? Account, string? Password);

    private static async Task<IResult> CurrentAsync(HttpContext context)
    {
        NoStore(context);
        var session = await context.AuthenticateAsync(Scheme);
        return Results.Json(new { Account = session.Principal?.Identity?.Name });
    }

    private static async Task<IResult> RememberAsync(HttpContext context, Credentials input, AccountAppService accounts,
        LoginThrottle throttle, CancellationToken ct)
    {
        NoStore(context);
        if (!IsSameOrigin(context))
        {
            return Results.Json(new { }, statusCode: StatusCodes.Status403Forbidden);
        }

        var name = input.Account?.Trim() ?? "";
        var password = input.Password ?? "";
        if (name.Length is 0 or > AccountRules.MaxAccountBytes || password.Length is 0 or > AccountRules.MaxPasswordBytes)
        {
            return InvalidCredentials();
        }

        var address = WebHosting.AddressOf(context);
        var now = Environment.TickCount64;
        if (throttle.IsBlocked(address, name, now))
        {
            return Results.Json(new { }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        throttle.RecordFailure(address, name, now);
        var check = await accounts.CheckAsync(name, password, ct);
        if (check.Account is not { } account)
        {
            return InvalidCredentials();
        }

        throttle.RecordSuccess(name);
        await context.SignInAsync(Scheme, AccountSession.PrincipalOf(account), new AuthenticationProperties { IsPersistent = true });
        return Results.Json(new { Account = account.UserId });
    }

    // The game window's attachment (news.js): one image or video for a ticket of the signed-in account, as the site's
    // attachment page takes it.
    private static async Task<IResult> AttachAsync(HttpContext context, Guid ticketId, IAccountRepository accounts, SupportTickets tickets,
        CancellationToken ct)
    {
        NoStore(context);
        if (!IsSameOrigin(context))
        {
            return Results.Json(new { }, statusCode: StatusCodes.Status403Forbidden);
        }

        var session = await context.AuthenticateAsync(Scheme);
        if (session.Principal?.Identity?.Name is not { } name || await accounts.FindByUserIdAsync(name, ct) is not { IsBlocked: false } account
            || !AccountSession.Matches(session.Principal, account))
        {
            return InvalidCredentials();
        }

        if (!tickets.Exists(account.Id, ticketId) || !context.Request.HasFormContentType)
        {
            return Results.NotFound();
        }

        var form = await context.Request.ReadFormAsync(ct);
        if (form.Files is not [{ Length: > 0 and <= SupportTickets.MaximumAttachmentBytes } file])
        {
            return Results.BadRequest();
        }

        await using var content = file.OpenReadStream();
        return await tickets.AttachAsync(account.Id, ticketId, content, SupportTickets.AttachmentExtension(file.FileName), ct)
            ? Results.Json(new { })
            : Results.BadRequest();
    }

    private static async Task<IResult> IssueAsync(HttpContext context, IAccountRepository accounts, IBrowserLoginTickets tickets,
        TimeProvider clock, CancellationToken ct)
    {
        NoStore(context);
        if (!IsSameOrigin(context))
        {
            return Results.Json(new { }, statusCode: StatusCodes.Status403Forbidden);
        }

        var session = await context.AuthenticateAsync(Scheme);
        if (session.Principal?.Identity?.Name is not { } name || await accounts.FindByUserIdAsync(name, ct) is not { } account
            || !AccountSession.Matches(session.Principal, account))
        {
            return InvalidCredentials();
        }

        var ticket = BrowserLoginTicket.Generate();
        var now = clock.GetUtcNow();
        await tickets.IssueAsync(BrowserLoginTicket.Digest(ticket), account.Id, BrowserLoginTicket.Digest(account.PasswordHash),
            now.ToUnixTimeSeconds(), (now + BrowserLoginTicket.Lifetime).ToUnixTimeSeconds(), ct);
        return Results.Json(new { Account = name, Ticket = ticket });
    }

    private static async Task<IResult> ForgetAsync(HttpContext context)
    {
        NoStore(context);
        if (!IsSameOrigin(context))
        {
            return Results.Json(new { }, statusCode: StatusCodes.Status403Forbidden);
        }

        await context.SignOutAsync(Scheme);
        return Results.NoContent();
    }

    private static IResult InvalidCredentials() => Results.Json(new { }, statusCode: StatusCodes.Status401Unauthorized);

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private static bool IsSameOrigin(HttpContext context) =>
        context.Request.IsHttps && context.Request.Headers.Origin.ToString() == $"https://{context.Request.Host}";
}
