using System.Net;
using System.Text.Json.Serialization;
using PristonTale.Web.Hosting;

namespace PristonTale.Web.Services;

// Cloudflare Turnstile's server check (Security:TurnstileSiteKey/TurnstileSecret): the token the widget gave the page is
// sent to Cloudflare with the secret and the player's address; only a confirmed token passes. Off when the keys are empty.
public sealed class Turnstile(WebSettings settings, IHttpClientFactory clients, ILogger<Turnstile> logger)
{
    public const string ScriptOrigin = "https://challenges.cloudflare.com";
    public const string ScriptAddress = ScriptOrigin + "/turnstile/v0/api.js";
    private const string VerifyAddress = ScriptOrigin + "/turnstile/v0/siteverify";

    public bool Enabled => settings.Security.TurnstileEnabled;

    public string SiteKey => settings.Security.TurnstileSiteKey;

    public async Task<bool> VerifyAsync(string? token, IPAddress? address, CancellationToken ct)
    {
        if (!Enabled)
        {
            return true;
        }

        if (string.IsNullOrEmpty(token) || token.Length > 2048)
        {
            return false;
        }

        var form = new Dictionary<string, string> { ["secret"] = settings.Security.TurnstileSecret, ["response"] = token };
        if (address is not null)
        {
            form["remoteip"] = address.ToString();
        }

        try
        {
            using var response = await clients.CreateClient(nameof(Turnstile)).PostAsync(VerifyAddress, new FormUrlEncodedContent(form), ct);
            var result = await response.Content.ReadFromJsonAsync<VerifyResult>(ct);
            return response.IsSuccessStatusCode && result?.Success == true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(exception, "Turnstile check failed");
            return false;
        }
    }

    private sealed record VerifyResult([property: JsonPropertyName("success")] bool Success);
}
