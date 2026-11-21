using Microsoft.Extensions.Logging.Abstractions;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Web.Services;

namespace PristonTale.Tests;

// A news post's kind: kept by the site's feed by name, a post without one is news, and the game's window reads it into
// its badge.
internal static class NewsKindTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("news: a post keeps its kind (news, event, improvement) and the game names its badge, news when the kind is missing; the community links and the address the link question shows", KindAsync);
    }

    private static Task KindAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "etervale-news-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """[{"date":"2026-11-18","title":"Velho","summary":"s","body":[]}]""");
            var feed = new NewsFeed(path);
            feed.Publish(new NewsItem(new DateOnly(2026, 11, 19), "Evento", "s", [], "DEV Strong", NewsKind.Event));
            var items = new NewsFeed(path).Items(NullLogger.Instance);
            Assert.True(items.Count == 2 && items[0].Kind == NewsKind.Event && items[1].Kind == NewsKind.News,
                "the feed lost a post's kind or gave an old post another kind than news");
            Assert.True(File.ReadAllText(path).Contains("\"kind\": \"Event\"", StringComparison.Ordinal), "the kind is not written by name");
        }
        finally
        {
            File.Delete(path);
        }

        Assert.True(HudNews.ParseKind("Event") == HudNews.NewsKind.Event && HudNews.ParseKind("improvement") == HudNews.NewsKind.Improvement
            && HudNews.ParseKind("") == HudNews.NewsKind.News && HudNews.ParseKind("other") == HudNews.NewsKind.News,
            "the game read a kind wrongly");
        Assert.True(HudNews.KindLabel(HudNews.NewsKind.News, "pt-BR") == "Novidade" && HudNews.KindLabel(HudNews.NewsKind.Event, "pt-BR") == "Evento"
            && HudNews.KindLabel(HudNews.NewsKind.Improvement, "pt-BR") == "Melhoria" && HudNews.KindLabel(HudNews.NewsKind.Improvement, "en") == "Improvement",
            "a badge is not named Novidade, Evento or Melhoria");

        var defaults = new PristonTale.Engine3D.Rendering.ClientSettings();
        Assert.True(defaults.DiscordUrl == "https://discord.com/invite/4FdF7Mes2B" && defaults.InstagramUrl == "https://www.instagram.com/noxiustale",
            "the community links are not the Discord invite and the Instagram page");
        Assert.True(HudNews.ShownAddress(new Uri(defaults.DiscordUrl)) == "discord.com/invite/4FdF7Mes2B"
            && HudNews.ShownAddress(new Uri("https://www.instagram.com/noxiustale/")) == "www.instagram.com/noxiustale",
            "the link question does not show the address without its scheme and closing slash");
        return Task.CompletedTask;
    }
}
