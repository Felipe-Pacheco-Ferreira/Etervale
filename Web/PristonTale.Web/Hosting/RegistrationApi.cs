using PristonTale.Application.Services;
using PristonTale.Contracts.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Web.Pages;
using PristonTale.Web.Services;

namespace PristonTale.Web.Hosting;

// The entry page's "Criar conta" panel (main.js): a JSON post from the site's own pages. It checks what the panel shows
// (fields, accepted terms, Turnstile when configured), counts against the per-address registration limit, and answers
// with the field to fix and a message in Portuguese.
public static class RegistrationApi
{
    public const string Path = "/api/conta";

    public sealed record Request(string? Account, string? Email, string? Password, string? Confirmation, string? Referral, bool Terms,
        string? Turnstile, string? Site);

    public sealed record Answer(bool Ok, string Field, string Message);

    public static void Map(WebApplication app) =>
        app.MapPost(Path, HandleAsync).RequireRateLimiting(WebHosting.FormPostsPolicy).DisableAntiforgery();

    private static async Task<IResult> HandleAsync(HttpContext context, Request request, AccountAppService service, RegistrationGate gate,
        Turnstile turnstile, ILogger<Request> logger, CancellationToken ct)
    {
        // Only the site's own pages may post here: the browser sends their origin, which must be this host's.
        var origin = context.Request.Headers.Origin.ToString();
        if (origin != $"{context.Request.Scheme}://{context.Request.Host}")
        {
            return Results.Json(new Answer(false, "", "Pedido recusado."), statusCode: StatusCodes.Status403Forbidden);
        }

        var account = request.Account?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        var referral = request.Referral?.Trim() ?? "";
        var password = request.Password ?? "";
        if (!string.IsNullOrEmpty(request.Site))
        {
            return Refused("", "Não foi possível criar a conta.");
        }

        if (!AccountRules.IsValidName(account))
        {
            return Refused("account", CadastroModel.NameRule);
        }

        if (!AccountRules.IsValidEmail(email))
        {
            return Refused("email", "Digite um e-mail válido.");
        }

        if (!AccountRules.IsValidPassword(password, account))
        {
            return Refused("password", CadastroModel.PasswordRule);
        }

        if (!string.Equals(password, request.Confirmation, StringComparison.Ordinal))
        {
            return Refused("confirmation", "As senhas não são iguais.");
        }

        if (!AccountRules.IsValidReferralCode(referral))
        {
            return Refused("referral", "O código de indicação tem só letras, números, - e _.");
        }

        if (!request.Terms)
        {
            return Refused("terms", "Leia e aceite os Termos de Uso.");
        }

        var address = WebHosting.AddressOf(context);
        if (!await turnstile.VerifyAsync(request.Turnstile, address, ct))
        {
            return Refused("turnstile", "Confirme a verificação de segurança.");
        }

        if (!gate.TryEnter(address))
        {
            return Results.Json(new Answer(false, "", "Muitas contas criadas a partir desta conexão. Tente de novo mais tarde."),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var registration = await service.RegisterAsync(account, password, new AccountContact(email, referral), ct);
        switch (registration.Result)
        {
            case RegistrationResult.Created:
                logger.LogInformation("Account {Account} registered on the entry page", account);
                return Results.Json(new Answer(true, "", "Conta criada! Entre com a sua senha."));
            case RegistrationResult.NameTaken:
                return Refused("account", "Este nome de conta já existe. Escolha outro.");
            case RegistrationResult.EmailTaken:
                return Refused("email", "Este e-mail já tem uma conta.");
            default:
                return Refused("", "Não foi possível criar a conta. Confira os dados e tente de novo.");
        }
    }

    private static IResult Refused(string field, string message) =>
        Results.Json(new Answer(false, field, message), statusCode: StatusCodes.Status400BadRequest);
}
