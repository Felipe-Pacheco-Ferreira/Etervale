using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PristonTale.Application.Chat;
using PristonTale.Persistence.Abstractions.WorldData;

namespace PristonTale.Server.Chat;

// The chat translation of appsettings.json ("ChatTranslation" section): Provider None (default), DeepL or Google, the
// owner's ApiKey, and CharactersPerMinute, the most characters one player's translations may take a minute. Without a
// provider or a key the server translates nothing and tells the clients so.
public sealed record ChatTranslationSettings(string Provider, string ApiKey, int CharactersPerMinute)
{
    public const string Section = "ChatTranslation";

    public static ChatTranslationSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var perMinute = section.GetValue("CharactersPerMinute", ChatTranslation.DefaultCharactersPerMinute);
        if (perMinute < 1)
        {
            throw new InvalidOperationException("ChatTranslation:CharactersPerMinute must be at least 1.");
        }

        return new ChatTranslationSettings(section["Provider"] ?? "None", section["ApiKey"] ?? "", perMinute);
    }

    public IChatTranslator? CreateTranslator(ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return null;
        }

        return Provider.ToUpperInvariant() switch
        {
            "DEEPL" => new DeepLChatTranslator(ApiKey, logger),
            "GOOGLE" => new GoogleChatTranslator(ApiKey, logger),
            "NONE" or "" => null,
            _ => throw new InvalidOperationException($"ChatTranslation:Provider '{Provider}' is not None, DeepL or Google.")
        };
    }
}

// The HTTP side shared by the providers: one client with a short timeout, failures logged at most once a minute so a
// wrong key does not flood the log.
public abstract class HttpChatTranslator(ILogger logger) : IChatTranslator, IDisposable
{
    protected static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private long _lastWarningMs = long.MinValue;

    protected HttpClient Http { get; } = new() { Timeout = Timeout };

    public async Task<string?> TranslateAsync(string text, string language, CancellationToken cancellationToken)
    {
        if (TargetOf(language) is not { } target)
        {
            return null;
        }

        try
        {
            return await RequestAsync(text, target, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastWarningMs);
            if (now - last >= 60_000 && Interlocked.CompareExchange(ref _lastWarningMs, now, last) == last)
            {
                logger.LogWarning("Chat translation failed: {Message}", exception.Message);
            }

            return null;
        }
    }

    // The provider's code for a catalog language, null when it has none.
    protected abstract string? TargetOf(string language);

    protected abstract Task<string?> RequestAsync(string text, string target, CancellationToken cancellationToken);

    public void Dispose()
    {
        Http.Dispose();
        GC.SuppressFinalize(this);
    }
}

// DeepL's API v2: a key ending in ":fx" belongs to the free plan, which has its own address.
public sealed class DeepLChatTranslator : HttpChatTranslator
{
    public DeepLChatTranslator(string apiKey, ILogger logger) : base(logger)
    {
        Http.BaseAddress = new Uri(apiKey.EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com/" : "https://api.deepl.com/");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + apiKey);
    }

    protected override string? TargetOf(string language) => language switch
    {
        CatalogLanguages.English => "EN-US",
        CatalogLanguages.BrazilianPortuguese => "PT-BR",
        CatalogLanguages.Spanish => "ES",
        CatalogLanguages.SimplifiedChinese => "ZH",
        CatalogLanguages.Japanese => "JA",
        _ => null
    };

    protected override async Task<string?> RequestAsync(string text, string target, CancellationToken cancellationToken)
    {
        using var response = await Http.PostAsJsonAsync("v2/translate", new DeepLRequest([text], target), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<DeepLResponse>(cancellationToken).ConfigureAwait(false);
        return answer?.Translations is [{ Text: { } translated }, ..] ? translated : null;
    }

    private sealed record DeepLRequest([property: JsonPropertyName("text")] string[] Text, [property: JsonPropertyName("target_lang")] string TargetLang);

    private sealed record DeepLResponse([property: JsonPropertyName("translations")] DeepLTranslation[]? Translations);

    private sealed record DeepLTranslation([property: JsonPropertyName("text")] string? Text);
}

// Google Cloud Translation's basic API (v2), the key in its header rather than the address.
public sealed class GoogleChatTranslator : HttpChatTranslator
{
    public GoogleChatTranslator(string apiKey, ILogger logger) : base(logger)
    {
        Http.BaseAddress = new Uri("https://translation.googleapis.com/");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("X-Goog-Api-Key", apiKey);
    }

    protected override string? TargetOf(string language) => language switch
    {
        CatalogLanguages.English => "en",
        CatalogLanguages.BrazilianPortuguese => "pt",
        CatalogLanguages.Spanish => "es",
        CatalogLanguages.Filipino => "tl",
        CatalogLanguages.Vietnamese => "vi",
        CatalogLanguages.SimplifiedChinese => "zh-CN",
        CatalogLanguages.Japanese => "ja",
        _ => null
    };

    protected override async Task<string?> RequestAsync(string text, string target, CancellationToken cancellationToken)
    {
        using var response = await Http.PostAsJsonAsync("language/translate/v2", new GoogleRequest(text, target, "text"), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<GoogleResponse>(cancellationToken).ConfigureAwait(false);
        return answer?.Data?.Translations is [{ TranslatedText: { } translated }, ..] ? translated : null;
    }

    private sealed record GoogleRequest([property: JsonPropertyName("q")] string Q, [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("format")] string Format);

    private sealed record GoogleResponse([property: JsonPropertyName("data")] GoogleData? Data);

    private sealed record GoogleData([property: JsonPropertyName("translations")] GoogleTranslation[]? Translations);

    private sealed record GoogleTranslation([property: JsonPropertyName("translatedText")] string? TranslatedText);
}
