using System.Text;
using PristonTale.Application.Support;
using PristonTale.Contracts.Social;
using PristonTale.DiscordBot.Sources;
using PristonTale.Persistence.Abstractions.GameMasters;
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

    public const uint StatusUpColor = 0x2ECC71;
    public const uint StatusDownColor = 0xE74C3C;
    public const uint StatusMaintenanceColor = 0xF1C40F;

    // The world status channel's standing card: each world up or down and its players, green while every world is up, red
    // when one is down and yellow during maintenance (/manutencao), whatever the worlds answer.
    public static BotCard StatusPanel(IReadOnlyList<WorldReport> worlds, bool maintenance = false, string reason = "")
    {
        var fields = worlds.Select(report => new BotCardField(
            (maintenance ? "🟡 " : report.Online ? "🟢 " : "🔴 ") + report.World.Name + (report.World.Region.Length > 0 ? " · " + report.World.Region : ""),
            maintenance ? "Em atualização" : report.Load is { } load ? "No ar · " + Online(load) : "Fora do ar", false)).ToArray();
        if (maintenance)
        {
            var why = reason.Length > 0 ? "\n**Motivo:** " + Escape(reason) : "";
            return new BotCard("📡 Status dos servidores", "🔧 Os servidores estão em atualização. Voltamos em breve!" + why, fields,
                StatusMaintenanceColor, "Atualizado automaticamente");
        }

        var allUp = worlds.Count > 0 && worlds.All(static report => report.Online);
        var summary = worlds.Count == 0 ? "Nenhum mundo configurado no site." : allUp ? "Todos os mundos estão no ar." : "Há mundos fora do ar.";
        return new BotCard("📡 Status dos servidores", summary, fields, allUp ? StatusUpColor : StatusDownColor, "Atualizado automaticamente");
    }

    private const int AuditResultShown = 300;

    // One line of the game masters' audit channel: where it came from (🎮 game, 🌐 site, 💬 Discord), when, who and at
    // which level, the command, and its outcome (✅ done, ⛔ refused) with what it answered.
    public static string AuditLine(GmAuditRecord entry, TimeZoneInfo zone)
    {
        // A game command sent from Discord (/gm) keeps the game's line but its user is the member's "discord:<id>".
        var origin = entry.Command.StartsWith("discord:", StringComparison.Ordinal) || entry.UserId.StartsWith("discord:", StringComparison.Ordinal) ? "💬"
            : entry.Command.StartsWith("web:", StringComparison.Ordinal) ? "🌐"
            : "🎮";
        var who = entry.CharacterName.Length > 0 ? entry.CharacterName : entry.UserId;
        var at = TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Utc)), zone);
        var line = $"{origin} `{at.ToString("dd/MM HH:mm", SiteText.Culture)}` **{Escape(who)}** (nv. {entry.GmLevel}) · `{Unfence(entry.Command).Replace('`', '\'')}` "
            + (entry.Accepted ? "✅" : "⛔");
        var result = entry.Result.Replace('\n', ' ').Trim();
        return result.Length == 0 ? line : line + " — " + Escape(Cut(result, AuditResultShown));
    }

    // The confirmation /limparchat asks before deleting, seen only by who asked.
    public static string PurgeQuestion(ulong channelId, int count, bool all) =>
        $"🧹 Apagar {(all ? $"todas as mensagens (até {SiteText.Number(count)})" : SiteText.Number(count) + " mensagens, das mais novas")} de <#{channelId}>? "
        + "Isso não pode ser desfeito.";

    public static string MaintenanceChanged(bool on, string reason) => on
        ? "🟡 **Manutenção iniciada** — os servidores estão em atualização." + (reason.Length > 0 ? " Motivo: " + Escape(reason) : "")
        : "🟢 **Manutenção concluída** — os servidores estão de volta!";

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

    public const string Everyone = "@everyone";

    // Discord's limits for a slash command's text (6000) and an embed's title; the title also carries the emoji.
    public const int DevNoteTitleLimit = 200;
    public const int DevNoteTextLimit = 4000;

    // 🛠️ marks a new system (its title says so), "✨ Nova melhoria:" an improvement, "🧰 Ferramenta da equipe:" a staff tool.
    public static string DevNoteTitle(string title, DevNoteKind kind = DevNoteKind.System) => kind switch
    {
        DevNoteKind.Improvement => "✨ Nova melhoria: ",
        DevNoteKind.Staff => "🧰 Ferramenta da equipe: ",
        _ => "🛠️ "
    } + Cut(title.Trim(), DevNoteTitleLimit);

    // A slash command's text is one line: "\n" written in it breaks the line.
    public static string DevNoteText(string text) => Cut(text.Replace("\\n", "\n", StringComparison.Ordinal).Trim(), DevNoteTextLimit);

    public static string DevNoteSignature(string author, DateTimeOffset when, TimeZoneInfo zone) =>
        $"{author} · {TimeZoneInfo.ConvertTime(when, zone):dd'/'MM'/'yyyy}";

    // A development note as a news post of the site and the game: the summary on one line, a body line per note line and
    // an empty line between paragraphs, signed by the notes' author, with the game's badge of its kind.
    public static NewsItem DevNoteNews(DevNote note, string author) => new(note.Date, note.Title, note.Summary.Replace('\n', ' '),
        note.Body.SelectMany(static (paragraph, index) => (index == 0 ? [] : new[] { "" }).Concat(paragraph.Split('\n'))).ToArray(), author,
        note.Kind == DevNoteKind.Improvement ? NewsKind.Improvement : NewsKind.News);

    // New systems and improvements are news of the game and the site; staff tools are not.
    public static bool InNews(DevNote note) => note.Kind is DevNoteKind.System or DevNoteKind.Improvement;

    // A line of the game's chat as the relay channels show it, inside a code block (ChatBlocks), where Discord formats and
    // pings nothing; the server's name leads when the bot follows more than one.
    public static string ChatLine(PublicGameMessage message, string server = "") =>
        (server.Length == 0 ? $"[{ChannelName(message.Channel)}] " : $"[{server} · {ChannelName(message.Channel)}] ") + Unfence(message.Text);

    // Chat lines as Discord messages, each one code block (the dark box), within the message limit.
    public static IReadOnlyList<string> ChatBlocks(IEnumerable<string> lines) =>
        Pack(lines, MessageLimit - Fence.Length * 2 - 2).Select(static block => Fence + "\n" + block + "\n" + Fence).ToArray();

    private const string Fence = "```";

    // A line cannot close the code block it is in.
    private static string Unfence(string text) => text.Replace(Fence, "'''", StringComparison.Ordinal);

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
    public static IReadOnlyList<string> Pack(IEnumerable<string> lines, int limit = MessageLimit)
    {
        var messages = new List<string>();
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            var piece = Cut(line, limit);
            if (current.Length > 0 && current.Length + 1 + piece.Length > limit)
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
        Fence + "\n" + Cut(Unfence(string.Join("\n", lines)), MessageLimit - CodeReserve) + "\n" + Fence;

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
