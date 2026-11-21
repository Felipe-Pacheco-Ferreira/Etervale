using PristonTale.Application.Chat;
using PristonTale.Contracts.Texts;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;

namespace PristonTale.Tests;

// The chat's translation with a translator that needs no network: who gets a translated line, the cache, the hearer's
// characters per minute, a failure, and the wire and settings that carry the player's choice.
internal static class ChatTranslationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a player's line reaches who asked for a translation once more, translated and marked, and nobody else", TranslatedLineAsync);
        yield return ("the same text in the same language is asked once, and a hearer's own language never", CacheAsync);
        yield return ("a hearer's translations stop at its characters per minute and start again the next minute", LimitAsync);
        yield return ("a failed translation leaves the original alone and is asked again next time", FailureAsync);
        yield return ("a whisper reaches its receiver translated, with its From> prefix", WhisperAsync);
        yield return ("the wire carries the chosen language and the server's flag, and the settings resolve and cycle the choice", WireAndSettingsAsync);
    }

    private sealed class FakeTranslator : IChatTranslator
    {
        public List<(string Text, string Language)> Asked { get; } = [];
        public int Failures { get; set; }

        public Task<string?> TranslateAsync(string text, string language, CancellationToken cancellationToken)
        {
            Asked.Add((text, language));
            if (Failures > 0)
            {
                Failures--;
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>($"[{language}] {text}");
        }
    }

    private static (ChatFixture Fixture, FakeTranslator Translator) Make(int perMinute = ChatTranslation.DefaultCharactersPerMinute)
    {
        var translator = new FakeTranslator();
        ChatFixture? fixture = null;
        fixture = new ChatFixture(translation: new ChatTranslation(translator, perMinute, () => fixture!.NowMs));
        return (fixture, translator);
    }

    private static List<ChatLine> Translated(ChatFixture.Player player)
    {
        lock (player.Link.Lines)
        {
            return player.Link.Lines.Where(static line => line.Line.Id == GameTextId.ChatTranslated).ToList();
        }
    }

    private static async Task TranslatedLineAsync()
    {
        var (fixture, _) = Make();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 10, 10);
        var cid = await fixture.JoinAsync("Cid", 20, 20);
        ann.Session.Language = "pt-BR";
        bob.Member.TranslateTo = "en";
        await fixture.SayAsync(ann, "bom dia");
        Assert.True(bob.Link.Texts().Contains("Ann: bom dia"), "the hearer lost the original line");
        Assert.True(Translated(bob) is [{ Text: "Ann: [en] bom dia" } line] && line.SpeakerId == ann.Id && line.Kind == PristonTale.Contracts.Social.ChatKind.Player,
            "the translated line did not follow as the speaker's line, marked");
        Assert.True(Translated(cid).Count == 0 && Translated(ann).Count == 0, "a translation reached a player who did not ask, or the speaker");
    }

    private static async Task CacheAsync()
    {
        var (fixture, translator) = Make();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 10, 10);
        var cid = await fixture.JoinAsync("Cid", 20, 20);
        var dan = await fixture.JoinAsync("Dan", 30, 30);
        ann.Session.Language = "pt-BR";
        bob.Member.TranslateTo = "en";
        cid.Member.TranslateTo = "en";
        dan.Member.TranslateTo = "pt-BR";
        await fixture.SayAsync(ann, "boa noite");
        fixture.NowMs += 10_000;
        await fixture.SayAsync(ann, "boa noite");
        Assert.True(translator.Asked.Count == 1 && translator.Asked[0] == ("boa noite", "en"), "the same text in the same language was asked again");
        Assert.True(Translated(bob).Count == 2 && Translated(cid).Count == 2, "a cached translation did not reach every hearer");
        Assert.True(Translated(dan).Count == 0, "a hearer reading the speaker's language got a translation");
    }

    private static async Task LimitAsync()
    {
        var (fixture, translator) = Make(perMinute: 12);
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 10, 10);
        bob.Member.TranslateTo = "es";
        await fixture.SayAsync(ann, "first one");
        fixture.NowMs += 3_000;
        await fixture.SayAsync(ann, "second one");
        Assert.True(translator.Asked.Count == 1 && Translated(bob).Count == 1, "a translation past the characters per minute was asked");
        fixture.NowMs += 60_000;
        await fixture.SayAsync(ann, "second one");
        Assert.True(translator.Asked.Count == 2 && Translated(bob).Count == 2, "the next minute did not give the hearer its characters back");
    }

    private static async Task FailureAsync()
    {
        var (fixture, translator) = Make();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 10, 10);
        bob.Member.TranslateTo = "es";
        translator.Failures = 1;
        await fixture.SayAsync(ann, "hello there");
        Assert.True(bob.Link.Texts().Contains("Ann: hello there") && Translated(bob).Count == 0, "a failure sent a line or lost the original");
        fixture.NowMs += 10_000;
        await fixture.SayAsync(ann, "hello there");
        Assert.True(translator.Asked.Count == 2 && Translated(bob) is [{ Text: "Ann: [es] hello there" }], "a failed translation was remembered instead of asked again");
    }

    private static async Task WhisperAsync()
    {
        var (fixture, _) = Make();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 900, 900);
        bob.Member.TranslateTo = "pt-BR";
        await fixture.SayAsync(ann, "/:Bob see you soon");
        Assert.True(Translated(bob) is [{ Text: "From> Ann: [pt-BR] see you soon" } line] && line.Kind == PristonTale.Contracts.Social.ChatKind.Whisper,
            "the whisper did not arrive translated with its prefix");
        Assert.True(Translated(ann).Count == 0, "the sender got its own whisper translated");
    }

    private static Task WireAndSettingsAsync()
    {
        var buffer = new byte[ChatTranslateToPacket.MaxSize];
        var asked = new ChatTranslateToPacket("pt-BR");
        Assert.True(ChatTranslateToPacket.Read(buffer.AsSpan(0, asked.Write(buffer))) == asked, "the chosen language changed on the wire");
        var joinedBuffer = new byte[WorldJoinedPacket.Size];
        var joined = new WorldJoinedPacket(7, 1234, 2, 5678, ChatTranslation: true);
        Assert.True(WorldJoinedPacket.Read(joinedBuffer.AsSpan(0, joined.Write(joinedBuffer))) == joined, "the server's translation flag changed on the wire");

        var settings = new ClientSettings { Language = ClientSettings.SpanishLanguage, ChatTranslateTo = ClientSettings.GameLanguageTranslation };
        Assert.True(settings.ChatTranslationTarget == ClientSettings.SpanishLanguage && new ClientSettings().ChatTranslationTarget == ""
            && (settings with { ChatTranslateTo = "en" }).ChatTranslationTarget == "en", "the translation language was not resolved");
        Assert.True((settings with { ChatTranslateTo = "xx" }).Validate(Engine3D.Diagnostics.ConsoleClientLog.Instance).ChatTranslateTo == "",
            "an unknown translation language was kept");
        Assert.True(HudSettingsPanel.NextChatTranslation("", 1) == ClientSettings.GameLanguageTranslation
            && HudSettingsPanel.NextChatTranslation("", -1) == ClientSettings.SupportedLanguages[^1], "the panel did not cycle the translation choices");
        return Task.CompletedTask;
    }
}
