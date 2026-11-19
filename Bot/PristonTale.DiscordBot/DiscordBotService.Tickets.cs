using System.Globalization;
using Discord;
using Discord.WebSocket;
using PristonTale.Application.Support;
using PristonTale.Persistence.Abstractions.GameMasters;

namespace PristonTale.DiscordBot;

// Support tickets as conversations: each ticket not closed is posted once in its category's channel with a "Fechar"
// button, and a thread under the post carries the conversation. The player's messages (from the game or the site) and
// the staff's (from the site) are posted in the thread; what a game master writes in the thread goes to the ticket as a
// staff message. A closed ticket keeps its post, with Transcript, Reabrir and Deletar, and its thread locked until a game
// master deletes it from Discord.
public sealed partial class DiscordBotService
{
    private const int AuditResultLimit = 1000;
    private const string TicketCloseButton = "ticket-close";
    private const string TicketTranscriptButton = "ticket-transcript";
    private const string TicketReopenButton = "ticket-reopen";
    private const string TicketDeleteButton = "ticket-delete";

    // Markers among a post's posted messages: its attachment is in the thread, its closing is shown.
    private const string AttachmentKey = "media";
    private const string ClosedKey = "closed";

    // The loop, the close button and a game master's line in a thread all change tickets; one at a time, so a ticket is
    // never posted twice and a line written from a thread is never posted back.
    private readonly SemaphoreSlim _ticketGate = new(1, 1);

    private async Task SyncTicketsAsync(CancellationToken ct)
    {
        await _ticketGate.WaitAsync(ct);
        try
        {
            await SyncTicketsOnceAsync(ct);
        }
        finally
        {
            _ticketGate.Release();
        }
    }

    private async Task SyncTicketsOnceAsync(CancellationToken ct)
    {
        if (_tickets is null)
        {
            return;
        }

        var queue = await _tickets.QueueAsync(ct);
        var listed = new HashSet<Guid>();
        foreach (var (accountId, ticket) in queue.OrderBy(static entry => entry.Ticket.CreatedAt))
        {
            listed.Add(ticket.Id);
            try
            {
                await SyncTicketAsync(accountId, ticket, ct);
            }
            catch (Discord.Net.HttpException exception) when (exception.HttpCode == System.Net.HttpStatusCode.NotFound)
            {
                // The post or its thread was deleted on Discord: forget it, so a ticket still open is posted again.
                await SaveStateAsync(state => state.Tickets.Remove(ticket.Id), ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Ticket {Ticket} could not be posted or updated", ticket.Id);
            }
        }

        var gone = _state.Tickets.Keys.Where(id => !listed.Contains(id)).ToArray();
        if (gone.Length > 0)
        {
            await SaveStateAsync(state => Array.ForEach(gone, id => state.Tickets.Remove(id)), ct);
        }
    }

    private async Task SyncTicketAsync(long accountId, SupportTicket ticket, CancellationToken ct)
    {
        var zone = time.LocalTimeZone;
        var header = BotTexts.TicketHeader(accountId, ticket, zone);
        var changed = false;
        if (!_state.Tickets.TryGetValue(ticket.Id, out var post))
        {
            if (ticket.Closed || _client.GetChannel(settings.Discord.TicketChannel(ticket.Category)) is not ITextChannel channel)
            {
                return;
            }

            var message = await channel.SendMessageAsync(header, allowedMentions: AllowedMentions.None, components: Buttons(accountId, ticket.Id, closed: false));
            var created = await channel.CreateThreadAsync(BotTexts.TicketThreadName(ticket), ThreadType.PublicThread, ThreadArchiveDuration.OneWeek, message);
            post = new TicketPost(accountId, channel.Id, message.Id, created.Id, header, []);
            await SaveStateAsync(state => state.Tickets[ticket.Id] = post, ct);
        }
        else if (post.Header != header && _client.GetChannel(post.ChannelId) is IMessageChannel channel)
        {
            await channel.ModifyMessageAsync(post.MessageId, message =>
            {
                message.Content = header;
                message.Components = Buttons(accountId, ticket.Id, ticket.Closed);
                message.AllowedMentions = AllowedMentions.None;
            });
            post = post with { Header = header };
            changed = true;
        }

        // A closed ticket already shown closed needs nothing until it changes; its archived thread is not fetched.
        if (ticket.Closed && post.Posted.Contains(ClosedKey) && !changed)
        {
            return;
        }

        if (await ThreadAsync(post.ThreadId) is not { } thread)
        {
            return;
        }

        if (!post.Posted.Contains(AttachmentKey) && _tickets!.Attachment(accountId, ticket.Id) is { } attachment)
        {
            if (new FileInfo(attachment).Length <= AttachmentLimit)
            {
                await thread.SendFileAsync(attachment, "📎 Anexo do jogador", allowedMentions: AllowedMentions.None);
            }
            else
            {
                await thread.SendMessageAsync("📎 O jogador anexou um arquivo grande demais para o Discord; veja no atendimento do site.",
                    allowedMentions: AllowedMentions.None);
            }

            post.Posted.Add(AttachmentKey);
            changed = true;
        }

        // The first message is in the post itself.
        for (var index = 1; index < ticket.Messages.Count; index++)
        {
            var message = ticket.Messages[index];
            if (post.Posted.Add(TicketMessageKey(message)))
            {
                await thread.SendMessageAsync(BotTexts.TicketMessage(message, zone), allowedMentions: AllowedMentions.None);
                changed = true;
            }
        }

        // A closed ticket stays in its channel, its thread locked, until a game master deletes it; reopened, it goes on.
        var closedShown = post.Posted.Contains(ClosedKey);
        if (ticket.Closed && !closedShown)
        {
            await thread.SendMessageAsync(ticket.ClosedByPlayer ? "🔒 O jogador cancelou o ticket." : "🔒 Ticket fechado pela equipe.",
                allowedMentions: AllowedMentions.None);
            await thread.ModifyAsync(properties =>
            {
                properties.Archived = true;
                properties.Locked = true;
            });
            post.Posted.Add(ClosedKey);
            changed = true;
        }
        else if (!ticket.Closed && closedShown)
        {
            await thread.ModifyAsync(properties =>
            {
                properties.Archived = false;
                properties.Locked = false;
            });
            await thread.SendMessageAsync("🔓 Ticket reaberto pela equipe.", allowedMentions: AllowedMentions.None);
            post.Posted.Remove(ClosedKey);
            changed = true;
        }

        if (changed)
        {
            await SaveStateAsync(state => state.Tickets[ticket.Id] = post, ct);
        }
    }

    private async Task<IThreadChannel?> ThreadAsync(ulong threadId) =>
        _client.GetChannel(threadId) as IThreadChannel ?? await _client.Rest.GetChannelAsync(threadId) as IThreadChannel;

    // Open: close it. Closed: its transcript, open it again, or delete its post and thread from Discord.
    private static MessageComponent Buttons(long accountId, Guid id, bool closed) => closed
        ? new ComponentBuilder()
            .WithButton("Transcript", TicketId(TicketTranscriptButton, accountId, id), ButtonStyle.Secondary)
            .WithButton("Reabrir", TicketId(TicketReopenButton, accountId, id), ButtonStyle.Success)
            .WithButton("Deletar", TicketId(TicketDeleteButton, accountId, id), ButtonStyle.Danger)
            .Build()
        : new ComponentBuilder().WithButton("Fechar ticket", TicketId(TicketCloseButton, accountId, id), ButtonStyle.Danger).Build();

    // A message of a ticket as the bot remembers it was posted: its time and author.
    internal static string TicketMessageKey(TicketMessage message) =>
        message.At.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + (message.Author == TicketAuthor.Staff ? "S" : "P");

    private async Task HandleButtonAsync(SocketMessageComponent component, CancellationToken ct)
    {
        var customId = component.Data.CustomId;
        var kind = customId.Split(':')[0];
        if (kind == PurgeButton)
        {
            await PurgeAsync(component, ct);
            return;
        }

        if (ParseTicket(customId, kind) is not { } ticket || _tickets is null
            || kind is not (TicketCloseButton or TicketTranscriptButton or TicketReopenButton or TicketDeleteButton))
        {
            return;
        }

        var level = LevelOf(component.User);
        if (level < settings.Tickets.MinGmLevel)
        {
            await component.RespondAsync("⛔ Só game masters cuidam dos tickets.", ephemeral: true);
            return;
        }

        await component.DeferAsync(ephemeral: true);
        var id = ticket.Id.ToString("N");
        switch (kind)
        {
            case TicketTranscriptButton when await _tickets.FindAsync(ticket.AccountId, ticket.Id, ct) is { } found:
                using (var transcript = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(BotTexts.TicketTranscript(ticket.AccountId, found, time.LocalTimeZone))))
                {
                    await component.FollowupWithFileAsync(transcript, $"ticket-{found.Number}.txt", "📄 Transcript do ticket.", ephemeral: true);
                }

                return;
            case TicketCloseButton when await _tickets.CloseAsync(ticket.AccountId, ticket.Id, DisplayName(component.User), ct):
                await component.FollowupAsync("🔒 Ticket fechado.", ephemeral: true);
                await AuditTicketAsync(component.User, level, "discord: ticket close " + id, "closed", ct);
                break;
            case TicketReopenButton when _tickets.Reopen(ticket.AccountId, ticket.Id):
                await component.FollowupAsync("🔓 Ticket reaberto.", ephemeral: true);
                await AuditTicketAsync(component.User, level, "discord: ticket reopen " + id, "reopened", ct);
                break;
            case TicketDeleteButton:
                await DeletePostAsync(ticket.Id, ct);
                await component.FollowupAsync("🗑️ Ticket apagado do Discord; ele continua no histórico do jogador.", ephemeral: true);
                await AuditTicketAsync(component.User, level, "discord: ticket delete " + id, "deleted from Discord", ct);
                return;
            default:
                await component.FollowupAsync("Este ticket já mudou; atualize a mensagem.", ephemeral: true);
                return;
        }

        await SyncTicketsAsync(ct);
    }

    // Removes a closed ticket's post and thread from Discord; the ticket stays in storage, closed, so it is not posted again.
    private async Task DeletePostAsync(Guid id, CancellationToken ct)
    {
        await _ticketGate.WaitAsync(ct);
        try
        {
            if (!_state.Tickets.TryGetValue(id, out var post))
            {
                return;
            }

            if (await ThreadAsync(post.ThreadId) is { } thread)
            {
                await thread.DeleteAsync();
            }

            if (_client.GetChannel(post.ChannelId) is IMessageChannel channel)
            {
                await channel.DeleteMessageAsync(post.MessageId);
            }

            await SaveStateAsync(state => state.Tickets.Remove(id), ct);
        }
        finally
        {
            _ticketGate.Release();
        }
    }

    // A game master's line in a ticket's thread becomes a staff message of the ticket.
    private async Task<bool> AnswerFromThreadAsync(SocketMessage message, CancellationToken ct)
    {
        if (_tickets is null || message.Channel is not IThreadChannel thread || message.Author.IsBot || message.Author.IsWebhook
            || message is not SocketUserMessage || string.IsNullOrWhiteSpace(message.Content))
        {
            return false;
        }

        await _ticketGate.WaitAsync(ct);
        try
        {
            var entry = _state.Tickets.FirstOrDefault(pair => pair.Value.ThreadId == thread.Id);
            if (entry.Value is not { } post)
            {
                return false;
            }

            var level = LevelOf(message.Author);
            if (level < settings.Tickets.MinGmLevel)
            {
                await message.AddReactionAsync(new Emoji("⛔"));
                return true;
            }

            var text = message.CleanContent.Trim();
            if (await _tickets.AddMessageAsync(post.AccountId, entry.Key, TicketAuthor.Staff, DisplayName(message.Author), text, ct) is not { } stored)
            {
                await message.AddReactionAsync(new Emoji("⚠️"));
                return true;
            }

            post.Posted.Add(TicketMessageKey(stored));
            await SaveStateAsync(state => state.Tickets[entry.Key] = post, ct);
            await message.AddReactionAsync(new Emoji("✅"));
            await AuditTicketAsync(message.Author, level, "discord: ticket reply " + entry.Key.ToString("N"), text, ct);
            return true;
        }
        finally
        {
            _ticketGate.Release();
        }
    }

    private Task AuditTicketAsync(IUser user, int level, string command, string result, CancellationToken ct) =>
        gameMasters.AppendAuditAsync(new GmAuditEntry(0, AuditUser(user), DisplayName(user), level, command, settings.Tickets.MinGmLevel, true,
            result.Length > AuditResultLimit ? result[..AuditResultLimit] : result), ct);

    private static string TicketId(string kind, long accountId, Guid id) =>
        string.Join(':', kind, accountId.ToString(CultureInfo.InvariantCulture), id.ToString("N"));

    // The account and ticket of a button id made by TicketId, or null for another component.
    internal static (long AccountId, Guid Id)? ParseTicket(string customId, string kind)
    {
        var parts = customId.Split(':');
        return parts.Length == 3 && parts[0] == kind && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var account)
            && Guid.TryParseExact(parts[2], "N", out var id)
            ? (account, id)
            : null;
    }
}
