using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Sessions;
using PristonTale.Web.Hosting;

namespace PristonTale.Web.Pages;

[EnableRateLimiting(WebHosting.FormPostsPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EntrarModel(AccountAppService accounts, LoginThrottle throttle) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Informe sua conta.")]
    [StringLength(AccountRules.MaxAccountBytes, ErrorMessage = "Conta ou senha incorreta.")]
    public string Conta { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Informe sua senha.")]
    [StringLength(AccountRules.MaxPasswordBytes, ErrorMessage = "Conta ou senha incorreta.")]
    public string Senha { get; set; } = "";

    [BindProperty(SupportsGet = true, Name = AccountSession.ReturnParameter)]
    public string? Voltar { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? LocalRedirect(SitePaths.Account) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var address = WebHosting.AddressOf(HttpContext);
        var now = Environment.TickCount64;
        if (throttle.IsBlocked(address, Conta, now))
        {
            Error = "Muitas tentativas sem sucesso. Aguarde alguns minutos e tente de novo.";
            return Page();
        }

        // Every attempt counts before the hash is computed, so posts sent at once cannot all pass the check before the
        // first failure is counted; a success clears the account's count.
        throttle.RecordFailure(address, Conta, now);
        var check = await accounts.CheckAsync(Conta, Senha, ct);
        if (check.Account is not { } account)
        {
            Error = check.Failure == LoginFailure.LoginDenied ? "Esta conta está bloqueada." : "Conta ou senha incorreta.";
            return Page();
        }

        throttle.RecordSuccess(Conta);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AccountSession.PrincipalOf(account));
        return LocalRedirect(Url.IsLocalUrl(Voltar) ? Voltar : SitePaths.Account);
    }
}
