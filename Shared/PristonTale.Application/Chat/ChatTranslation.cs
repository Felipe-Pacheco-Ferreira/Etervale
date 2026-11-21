using System.Collections.Concurrent;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.Chat;

// A translation service the server pays for (DeepL, Google): null when the request failed. Called off the tick.
public interface IChatTranslator
{
    Task<string?> TranslateAsync(string text, string language, CancellationToken cancellationToken);
}

// What players write, translated for the members who asked for it (ChatMember.TranslateTo). The original line is
// delivered at once; the translation follows as a second line of the same kind, its text marked by
// GameTextId.ChatTranslated (the client adds "(translated)"). Nothing is asked when the hearer reads the speaker's own
// language. The same text in the same language is asked once while it stays in the cache, and a hearer's requests take
// at most CharactersPerMinute characters. A failed or unchanged translation sends nothing: the original stays.
public sealed class ChatTranslation(IChatTranslator translator, int charactersPerMinute, Func<long> nowMs)
{
    public const int MostCached = 4096;
    public const int DefaultCharactersPerMinute = 1000;
    private readonly ConcurrentDictionary<(string Text, string Language), Task<string?>> _cache = new();

    // compose turns the translated words into the line as the original was built ("Name: text", "From> name: text").
    public void Offer(ChatMember hearer, ChatMember speaker, ChatKind kind, ulong speakerId, string message, Func<string, string> compose)
    {
        var language = hearer.TranslateTo;
        if (language.Length == 0 || ReferenceEquals(hearer, speaker) || string.Equals(language, speaker.Session.Language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = (message, language);
        if (!_cache.TryGetValue(key, out var translation))
        {
            if (!hearer.TakeTranslation(message.Length, nowMs(), charactersPerMinute))
            {
                return;
            }

            if (_cache.Count >= MostCached)
            {
                _cache.Clear();
            }

            translation = _cache.GetOrAdd(key, static (asked, service) => service.AskAsync(asked.Text, asked.Language), this);
        }

        _ = DeliverAsync(hearer, kind, speakerId, key, translation, compose);
    }

    private async Task<string?> AskAsync(string text, string language)
    {
        try
        {
            return await translator.TranslateAsync(text, language, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException
            or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task DeliverAsync(ChatMember hearer, ChatKind kind, ulong speakerId, (string Text, string Language) key, Task<string?> translation,
        Func<string, string> compose)
    {
        var translated = (await translation.ConfigureAwait(false))?.Trim();
        if (translated is null)
        {
            // A failure is asked again next time instead of being remembered.
            _cache.TryRemove(new KeyValuePair<(string, string), Task<string?>>(key, translation));
            return;
        }

        if (translated.Length == 0 || string.Equals(translated, key.Text.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        hearer.Link.Deliver(new ChatLine(kind, speakerId, compose(translated), GameText.Of(GameTextId.ChatTranslated)));
    }
}
