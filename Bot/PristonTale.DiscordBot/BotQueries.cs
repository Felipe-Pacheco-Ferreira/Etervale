using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.DiscordBot.Sources;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot;

// The answers of the bot's commands, the same in Discord and in the console's --check.
public sealed class BotQueries(SiteClient site, SiteData data, ClassTitles titles, GameLinks links)
{
    public async Task<string> StatusAsync(CancellationToken ct) => BotTexts.Status(await site.WorldsAsync(ct));

    public async Task<string> RankingAsync(PlayerClass? playerClass, CancellationToken ct)
    {
        var scope = playerClass is { } chosen ? titles.Of(chosen, 0) : "Todas as classes";
        return BotTexts.Ranking(await data.TopAsync(playerClass, ct), titles, scope);
    }

    public async Task<string> EventsAsync(CancellationToken ct) => BotTexts.Events(await data.CalendarAsync(ct));

    public async Task<string> NewsAsync(CancellationToken ct) => BotTexts.News(await site.NewsAsync(ct));

    public async Task<string> GameMasterAsync(string userId, string name, int level, string line, CancellationToken ct)
    {
        if (links.Commands is not { } link)
        {
            return "O link com o servidor de jogo não está configurado (GameLink:Url).";
        }

        try
        {
            return BotTexts.GameMasterResult(await link.CommandAsync(new BotCommandRequest(userId, name, level, line), ct));
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            || (exception is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return "🔴 O servidor de jogo não respondeu.";
        }
    }
}
