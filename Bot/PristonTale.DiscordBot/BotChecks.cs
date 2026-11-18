using System.Globalization;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Support;
using PristonTale.DiscordBot.Sources;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot;

// --check: one command's answer printed to the console, against the configured site, database, game server and files,
// without connecting to Discord.
public static class BotChecks
{
    public const int LastLines = 10;

    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> check, TextWriter output)
    {
        var queries = services.GetRequiredService<BotQueries>();
        var settings = services.GetRequiredService<BotSettings>();
        var ct = CancellationToken.None;
        var argument = check.Count > 1 ? check[1] : null;
        switch (check[0])
        {
            case SlashCommands.Status:
                await output.WriteLineAsync(await queries.StatusAsync(ct));
                return 0;
            case SlashCommands.Ranking:
                var playerClass = ClassTitles.FromSlug(argument);
                if (argument is not null && playerClass is null)
                {
                    await output.WriteLineAsync("Classes: " + string.Join(", ", ClassTitles.All.Select(ClassTitles.Slug)));
                    return 2;
                }

                await output.WriteLineAsync(await queries.RankingAsync(playerClass, ct));
                return 0;
            case SlashCommands.Events:
                await output.WriteLineAsync(await queries.EventsAsync(ct));
                return 0;
            case SlashCommands.News:
                await output.WriteLineAsync(await queries.NewsAsync(ct));
                return 0;
            case "tickets":
                return await TicketsAsync(services.GetService<SupportTickets>(), output, ct);
            case "chat":
                await ChatAsync("public", settings.Activity.PublicPath, output, ct);
                await ChatAsync("private", settings.Activity.PrivatePath, output, ct);
                return 0;
            case SlashCommands.GameMaster when argument is not null:
                var level = check.Count > 2 && int.TryParse(check[2], NumberStyles.None, CultureInfo.InvariantCulture, out var asked)
                    ? asked
                    : GameMasterLevel.Moderator;
                await output.WriteLineAsync(await queries.GameMasterAsync("discord:check", "Console", level, argument, ct));
                return 0;
            default:
                await output.WriteLineAsync(BotCommandLine.Usage);
                return 2;
        }
    }

    private static async Task<int> TicketsAsync(SupportTickets? tickets, TextWriter output, CancellationToken ct)
    {
        if (tickets is null)
        {
            await output.WriteLineAsync("Tickets:Path is empty.");
            return 2;
        }

        var queue = await tickets.QueueAsync(ct);
        await output.WriteLineAsync($"{queue.Count(static entry => entry.Ticket.Status == TicketStatus.Open)} aguardando de {queue.Count}");
        foreach (var (accountId, ticket) in queue.Take(LastLines))
        {
            await output.WriteLineAsync(BotTexts.TicketHeader(accountId, ticket, TimeZoneInfo.Local));
            foreach (var message in ticket.Messages.Skip(1))
            {
                await output.WriteLineAsync(BotTexts.TicketMessage(message, TimeZoneInfo.Local));
            }

            await output.WriteLineAsync();
        }

        return 0;
    }

    private static async Task ChatAsync(string stream, string path, TextWriter output, CancellationToken ct)
    {
        if (path.Length == 0)
        {
            await output.WriteLineAsync($"{stream}: off");
            return;
        }

        var snapshot = await ActivityCursor.ReadAsync(BotSettings.BesideExecutable(path), ct);
        await output.WriteLineAsync(snapshot is null
            ? $"{stream}: no snapshot at {path}"
            : $"{stream}: {snapshot.OnlinePlayers} online, written {snapshot.UpdatedAt:u}");
        foreach (var message in snapshot?.Messages.TakeLast(LastLines) ?? [])
        {
            await output.WriteLineAsync(BotTexts.ChatLine(message));
        }
    }
}
