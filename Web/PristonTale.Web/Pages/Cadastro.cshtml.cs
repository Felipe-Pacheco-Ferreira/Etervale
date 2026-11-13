using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using PristonTale.Application.Services;
using PristonTale.Contracts.Sessions;
using PristonTale.Web.Hosting;
using PristonTale.Web.Services;

namespace PristonTale.Web.Pages;

[EnableRateLimiting(WebHosting.FormPostsPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CadastroModel(AccountAppService service, RegistrationGate gate, ILogger<CadastroModel> logger)
    : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Escolha o nome da conta.")]
    public string Conta { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Escolha uma senha.")]
    public string Senha { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Repita a senha.")]
    public string Confirmacao { get; set; } = "";

    // A field people never see: a form that fills it was sent by a bot.
    [BindProperty]
    public string? Site { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? LocalRedirect(SitePaths.Account) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(Site))
        {
            Error = "Não foi possível criar a conta.";
            return Page();
        }

        if (ModelState.IsValid && !AccountRules.IsValidName(Conta))
        {
            ModelState.AddModelError(nameof(Conta), NameRule);
        }

        if (ModelState.IsValid && !AccountRules.IsValidPassword(Senha, Conta))
        {
            ModelState.AddModelError(nameof(Senha), PasswordRule);
        }

        if (ModelState.IsValid && !string.Equals(Senha, Confirmacao, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(Confirmacao), "As senhas não são iguais.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!gate.TryEnter(WebHosting.AddressOf(HttpContext)))
        {
            Error = "Muitas contas criadas a partir desta conexão. Tente de novo mais tarde.";
            return Page();
        }

        var registration = await service.RegisterAsync(Conta, Senha, ct);
        if (registration.Account is not { } account)
        {
            if (registration.Result == RegistrationResult.NameTaken)
            {
                ModelState.AddModelError(nameof(Conta), "Este nome de conta já existe. Escolha outro.");
            }
            else
            {
                Error = "Não foi possível criar a conta. Confira os dados e tente de novo.";
            }

            return Page();
        }

        logger.LogInformation("Account {Account} registered on the website", Conta);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AccountSession.PrincipalOf(account));
        return LocalRedirect(SitePaths.NewAccount);
    }

    public static string NameRule =>
        $"De {AccountRules.MinNameLength} a {AccountRules.MaxNameLength} letras e números, sem espaços nem acentos, começando por uma letra.";

    public static string PasswordRule =>
        $"De {AccountRules.MinPasswordLength} a {AccountRules.MaxPasswordLength} caracteres, diferente do nome da conta.";
}
