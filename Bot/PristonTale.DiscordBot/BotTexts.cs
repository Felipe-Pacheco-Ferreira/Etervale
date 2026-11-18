using System.Text;
using PristonTale.Application.Support;
using PristonTale.Contracts.Social;
using PristonTale.DiscordBot.Sources;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Simulation.World.Calendar;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot;

// Every text the bot posts, in Brazilian Portuguese with Discord's markdown. The game's own words (players' lines,
// game-master replies) are escaped so they never format, and messages never mention anyone (DiscordBotService).
public static class BotTexts
{
    // Discord's limit for a message's content.
    public const int MessageLimit = 2000;
    public const int RankingShown = 10;
    public const int NewsShown = 3;
    public const int CallsShown = 3;

    public static string Status(IReadOnlyList<WorldReport> worlds)
    {
        if (worlds.Count == 0)
        {
            return "Nenhum mundo configurado no site.";
        }

        var text = new StringBuilder("**Mundos do Avalon**\n");
        foreach (var report in worlds)
        {
            text.Append(report.Online ? "🟢 " : "🔴 ").Append("**").Append(Escape(report.World.Name)).Append("** (")
                .Append(Escape(report.World.Region)).Append(") — ");
            text.Append(report.Load is { } load ? Online(load) : "fora do ar").Append('\n');
        }

        return text.ToString().TrimEnd();
    }

    public static string Online(WorldLoad load) => load.Capacity > 0
        ? $"{SiteText.Number(load.Players)} de {SiteText.Number(load.Capacity)} online"
        : $"{SiteText.Number(load.Players)} online";

    public static string PlayersOnline(IReadOnlyList<WorldReport> worlds) =>
        worlds.Any(static world => world.Online)
            ? $"{SiteText.Number(worlds.Sum(static world => world.Load?.Players ?? 0))} jogadores online"
            : "Servidor fora do ar";

    public static string WorldChanged(WorldReport report) => report.Online
        ? $"🟢 O mundo **{Escape(report.World.Name)}** está no ar."
        : $"🔴 O mundo **{Escape(report.World.Name)}** saiu do ar.";

    public static string Ranking(IReadOnlyList<RankedCharacter> top, ClassTitles titles, string scope)
    {
        if (top.Count == 0)
        {
            return $"**Ranking — {scope}**\nNinguém no ranking ainda.";
        }

        var text = new StringBuilder($"**Ranking — {scope}**\n");
        for (var index = 0; index < Math.Min(top.Count, RankingShown); index++)
        {
            var character = top[index];
            text.Append(index + 1).Append(". **").Append(Escape(character.Name)).Append("** — ").Append(titles.Of(character.JobCode, character.Tier))
                .Append(" · nível ").Append(character.Level);
            if (!string.IsNullOrEmpty(character.ClanName))
            {
                text.Append(" · ").Append(Escape(character.ClanName));
            }

            text.Append('\n');
        }

        return text.ToString().TrimEnd();
    }

    public static string Events(SiteCalendar calendar)
    {
        var zone = calendar.Zone;
        var text = new StringBuilder($"**Eventos** (horário do servidor, {SiteText.ZoneName(zone, calendar.Now)})\n");
        text.Append("\n**Agora**\n");
        if (calendar.Running.Count == 0)
        {
            text.Append("Nenhum evento agora.\n");
        }

        foreach (var occurrence in calendar.Running)
        {
            text.Append("• ").Append(SiteText.Title(occurrence.Kind)).Append(" — ").Append(SiteText.Window(occurrence, zone)).Append('\n');
        }

        text.Append("\n**Próximas 24 horas**\n");
        if (calendar.Coming.Count == 0)
        {
            text.Append("Nada marcado.\n");
        }

        foreach (var entry in calendar.Coming)
        {
            text.Append("• ").Append(SiteText.Title(entry.Kind)).Append(" — ").Append(SiteText.Window(entry.First, zone)).Append('\n');
        }

        if (calendar.NextCalls.Count > 0)
        {
            text.Append("\n**Chefes de campo** (nascem entre ").Append(SiteText.BossMinuteRange).Append(")\n");
            foreach (var call in calendar.NextCalls.Take(CallsShown))
            {
                text.Append("• ").Append(SiteText.Start(call.Call, zone)).Append(" — ").Append(Bosses(call.Bosses)).Append('\n');
            }
        }

        return text.ToString().TrimEnd();
    }

    public static string EventStarted(CalendarOccurrence occurrence, IReadOnlyList<CalendarBoss> bosses, TimeZoneInfo zone) =>
        occurrence.Kind == CalendarEventKind.FieldBosses
            ? $"⚔️ Chamada dos chefes de campo das {SiteText.Start(occurrence, zone)}: {Bosses(bosses)}."
            : $"🎉 Começou: **{SiteText.Title(occurrence.Kind)}** — {SiteText.Window(occurrence, zone)}. {SiteText.Describe(occurrence.Kind)}";

    public static string News(IReadOnlyList<NewsItem> items)
    {
        if (items.Count == 0)
        {
            return "Nenhuma notícia publicada.";
        }

        return string.Join("\n\n", items.Take(NewsShown).Select(NewsPost));
    }

    public static string NewsPost(NewsItem item) =>
        $"📰 **{Escape(item.Title)}** — {SiteText.Date(item.Date)}\n{Escape(item.Summary)}";

    // A line of the game's chat as the relay channels show it.
    public static string ChatLine(PublicGameMessage message) => $"**[{ChannelName(message.Channel)}]** {Escape(message.Text)}";

    public static string ChannelName(string channel) => channel switch
    {
        "local" => "Local",
        "trade" => "Troca",
        "notice" => "Aviso",
        "whisper" => "Sussurro",
        "party" => "Grupo",
        "clan" => "Clã",
        _ => channel
    };

    // Lines joined into as few messages as Discord takes, each within its limit; a longer line is cut.
    public static IReadOnlyList<string> Pack(IEnumerable<string> lines)
    {
        var messages = new List<string>();
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            var piece = Cut(line, MessageLimit);
            if (current.Length > 0 && current.Length + 1 + piece.Length > MessageLimit)
            {
                messages.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(piece);
        }

        if (current.Length > 0)
        {
            messages.Add(current.ToString());
        }

        return messages;
    }

    // The post that heads a ticket's thread: number, category, subject, state, who opened it and its first message.
    public static string TicketHeader(long accountId, SupportTicket ticket, TimeZoneInfo zone)
    {
        var icon = ticket.Status switch
        {
            TicketStatus.Closed => "🔒",
            TicketStatus.Answered => "✅",
            _ => "🎫"
        };
        var text = new StringBuilder();
        text.Append(icon).Append(" **#").Append(ticket.Number);
        if (ticket.Category.Length > 0)
        {
            text.Append(" · ").Append(ticket.Category);
        }

        text.Append("** — ").Append(Escape(ticket.Subject)).Append('\n')
            .Append("Aberto em ").Append(Time(ticket.CreatedAt, zone)).Append(" por ").Append(Escape(ticket.AuthorName))
            .Append(" · conta #").Append(accountId).Append(" · **").Append(SiteText.TicketState(ticket)).Append("**\n")
            .Append(Quote(ticket.Message));
        return Cut(text.ToString(), MessageLimit);
    }

    // A message of the conversation as the thread shows it.
    public static string TicketMessage(TicketMessage message, TimeZoneInfo zone) =>
        Cut((message.Author == TicketAuthor.Staff ? "🛡️ **" : "👤 **") + Escape(message.Name) + "** · " + Time(message.At, zone) + "\n" + Quote(message.Text),
            MessageLimit);

    // The whole conversation as a plain text file, for the staff's records.
    public static string TicketTranscript(long accountId, SupportTicket ticket, TimeZoneInfo zone)
    {
        var text = new StringBuilder();
        text.Append("Ticket #").Append(ticket.Number).Append(" (").Append(ticket.Id.ToString("N")).Append(")\n")
            .Append("Categoria: ").Append(ticket.Category.Length == 0 ? "-" : ticket.Category).Append('\n')
            .Append("Assunto: ").Append(ticket.Subject).Append('\n')
            .Append("Conta: #").Append(accountId).Append(" — ").Append(ticket.AuthorName).Append('\n')
            .Append("Estado: ").Append(SiteText.TicketState(ticket)).Append("\n\n");
        foreach (var message in ticket.Messages)
        {
            text.Append('[').Append(Time(message.At, zone)).Append("] ").Append(message.Author == TicketAuthor.Staff ? "Equipe — " : "Jogador — ")
                .Append(message.Name).Append('\n').Append(message.Text).Append("\n\n");
        }

        return text.ToString();
    }

    // The thread's name: "#1234 Subject", within Discord's 100 characters.
    public static string TicketThreadName(SupportTicket ticket) => Cut("#" + ticket.Number + " " + ticket.Subject, ThreadNameLimit);

    public const int ThreadNameLimit = 100;

    private static string Time(DateTimeOffset moment, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(moment, zone).ToString("dd/MM/yyyy HH:mm", SiteText.Culture);

    public static string GameMasterResult(BotCommandResponse response) => response.Outcome switch
    {
        "Done" => response.Replies.Count == 0 ? "✅ Feito." : "✅ " + Code(response.Replies),
        "Denied" => "⛔ Seu nível de GM não permite este comando." + (response.Replies.Count == 0 ? "" : "\n" + Code(response.Replies)),
        "NeedsWorld" => "⛔ Este comando precisa de um personagem no jogo.",
        _ => "❓ Comando desconhecido."
    };

    private static string Bosses(IReadOnlyList<CalendarBoss> bosses) =>
        bosses.Count == 0 ? "—" : string.Join(", ", bosses.Select(static boss => $"{Escape(boss.Name)} ({Escape(boss.FieldName)})"));

    private static string Quote(string text) => "> " + Escape(text).Replace("\n", "\n> ", StringComparison.Ordinal);

    // The replies in one code block that fits a message with the outcome line before it, so a split never breaks the fence.
    private static string Code(IReadOnlyList<string> lines) =>
        "```\n" + Cut(string.Join("\n", lines).Replace("```", "'''", StringComparison.Ordinal), MessageLimit - CodeReserve) + "\n```";

    // Room for the outcome line and the fences around a code block.
    private const int CodeReserve = 100;

    // The text within the limit, ending with "…" when cut, never between the two halves of a character.
    public static string Cut(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        var keep = limit - 1;
        if (char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return text[..keep] + "…";
    }

    public static string Escape(string text) => Discord.Format.Sanitize(text);
}
