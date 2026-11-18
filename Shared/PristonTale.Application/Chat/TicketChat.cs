using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using PristonTale.Application.Support;
using PristonTale.Simulation.Social;

namespace PristonTale.Application.Chat;

// The game's support window over chat commands; every answer is a server line "Ticket> ...". A long text travels in
// parts: "/ticketstart <subject>" (a new ticket), "/ticketreply <id>" (a message) or "/ticketedit <id> <subject>" (its
// player's edit), then "/ticketpart <base64 of up to 48 characters>" lines and "/ticketsave", within 15 s.
//
// "/ticketlist" answers "list", one "item <id> <number> <Open|Answered|Closed|Cancelled> <category or -> <created> <updated> <messages> <editable>
// <subject>" per ticket (times in Unix seconds) and "listend". "/ticketview <id>" answers "view <id> <number> <status>
// <category or -> <created> <editable> <author>", "subject <text>", then per message "msg <P|S> <time> <name>" followed
// by its text as "text <line>" lines continued by "more <rest>", and "viewend". "/ticketclose <id>" cancels a ticket its
// player may still edit. Saves answer "saved|replied|edited|closed <id>", refusals "error". "/tickets" and "/novoticket"
// keep the plain list and the one-line ticket of clients released before the conversation.
public sealed class TicketChat(SupportTickets? tickets, Func<long> nowMs)
{
    public const int SubjectLimit = 100;
    public const int MessageLimit = 1500;
    public const int TicketsListed = 30;
    private const int PartCharacters = 48;
    private const int TextCharacters = 200;
    private const int DraftMs = 15000;
    private const int LegacyListed = 10;

    private enum DraftKind
    {
        Create,
        Reply,
        Edit
    }

    private sealed record Draft(DraftKind Kind, Guid Ticket, string Subject, long Expires)
    {
        public StringBuilder Body { get; } = new();
    }

    private readonly ConditionalWeakTable<ChatMember, Draft> _drafts = new();

    private static readonly string[] Commands =
        ["/tickets", "/ticketlist", "/ticketview ", "/ticketclose ", "/ticketstart ", "/ticketreply ", "/ticketedit ", "/ticketpart ", "/ticketsave", "/novoticket "];

    public static bool Handles(string text)
    {
        foreach (var command in Commands)
        {
            if (command.EndsWith(' ') ? text.StartsWith(command, StringComparison.OrdinalIgnoreCase) : text.Equals(command, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // The name the staff sees for the player: "account (character)".
    public static string AuthorOf(ChatMember member) => member.UserId + " (" + member.Name + ")";

    public async Task HandleAsync(ChatMember member, string text)
    {
        void Send(string value) => member.Link.Deliver(ChatLine.Reply("Ticket> " + value));

        if (tickets is null)
        {
            Send("error");
            return;
        }

        try
        {
            if (!await RunAsync(tickets, member, text, Send))
            {
                Send("error");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
        {
            Send("error");
        }
    }

    private async Task<bool> RunAsync(SupportTickets store, ChatMember member, string text, Action<string> send)
    {
        var ct = CancellationToken.None;
        if (Starts(text, "/ticketstart ", out var subject))
        {
            return Begin(member, new Draft(DraftKind.Create, Guid.Empty, subject.Trim(), nowMs() + DraftMs));
        }

        if (Starts(text, "/ticketreply ", out var replied))
        {
            return Guid.TryParseExact(replied.Trim(), "N", out var id) && Begin(member, new Draft(DraftKind.Reply, id, "", nowMs() + DraftMs));
        }

        if (Starts(text, "/ticketedit ", out var edited))
        {
            var fields = edited.Split(' ', 2);
            return fields.Length == 2 && Guid.TryParseExact(fields[0], "N", out var id)
                && Begin(member, new Draft(DraftKind.Edit, id, fields[1].Trim(), nowMs() + DraftMs));
        }

        if (Starts(text, "/ticketpart ", out var part))
        {
            if (!_drafts.TryGetValue(member, out var draft) || draft.Expires < nowMs())
            {
                _drafts.Remove(member);
                return false;
            }

            var piece = Encoding.UTF8.GetString(Convert.FromBase64String(part));
            if (draft.Body.Length + piece.Length > MessageLimit)
            {
                _drafts.Remove(member);
                return false;
            }

            draft.Body.Append(ChatCommandText.Clean(piece));
            return true;
        }

        if (text.Equals("/ticketsave", StringComparison.OrdinalIgnoreCase))
        {
            if (!_drafts.TryGetValue(member, out var draft) || draft.Expires < nowMs())
            {
                _drafts.Remove(member);
                return false;
            }

            _drafts.Remove(member);
            var body = draft.Body.ToString().Trim();
            if (body.Length == 0)
            {
                return false;
            }

            switch (draft.Kind)
            {
                case DraftKind.Create:
                    send($"saved {await store.CreateAsync(member.AccountId, draft.Subject, body, AuthorOf(member), ct):N}");
                    return true;
                case DraftKind.Reply when await store.AddMessageAsync(member.AccountId, draft.Ticket, TicketAuthor.Player, AuthorOf(member), body, ct) is not null:
                    send($"replied {draft.Ticket:N}");
                    return true;
                case DraftKind.Edit when await store.EditAsync(member.AccountId, draft.Ticket, draft.Subject, body, ct):
                    send($"edited {draft.Ticket:N}");
                    return true;
                default:
                    return false;
            }
        }

        if (Starts(text, "/ticketclose ", out var closing))
        {
            if (!Guid.TryParseExact(closing.Trim(), "N", out var id) || !await store.CancelAsync(member.AccountId, id, ct))
            {
                return false;
            }

            send($"closed {id:N}");
            return true;
        }

        if (Starts(text, "/ticketview ", out var viewed))
        {
            if (!Guid.TryParseExact(viewed.Trim(), "N", out var id) || await store.FindAsync(member.AccountId, id, ct) is not { } ticket)
            {
                return false;
            }

            SendView(ticket, send);
            return true;
        }

        if (text.Equals("/ticketlist", StringComparison.OrdinalIgnoreCase))
        {
            send("list");
            foreach (var ticket in (await store.ListAsync(member.AccountId, ct)).Take(TicketsListed))
            {
                send(Invariant($"item {ticket.Id:N} {ticket.Number} {StatusOf(ticket)} {Category(ticket)} {ticket.CreatedAt.ToUnixTimeSeconds()} ")
                    + Invariant($"{ticket.UpdatedAt.ToUnixTimeSeconds()} {ticket.Messages.Count} {(ticket.Editable ? 1 : 0)} {ticket.Subject}"));
            }

            send("listend");
            return true;
        }

        if (Starts(text, "/novoticket ", out var oneLine))
        {
            var fields = oneLine.Split('|', 2);
            if (fields.Length != 2 || fields[0].Trim().Length is < 1 or > SubjectLimit || string.IsNullOrWhiteSpace(fields[1]))
            {
                return false;
            }

            await store.CreateAsync(member.AccountId, fields[0].Trim(), fields[1].Trim(), AuthorOf(member), ct);
            send("saved");
            return true;
        }

        if (text.Equals("/tickets", StringComparison.OrdinalIgnoreCase))
        {
            SendLegacyList(await store.ListAsync(member.AccountId, ct), send);
            return true;
        }

        return false;
    }

    private bool Begin(ChatMember member, Draft draft)
    {
        _drafts.Remove(member);
        if (draft.Kind != DraftKind.Reply && draft.Subject.Length is < 1 or > SubjectLimit)
        {
            return false;
        }

        _drafts.Add(member, draft);
        return true;
    }

    private static void SendView(SupportTicket ticket, Action<string> send)
    {
        send(Invariant($"view {ticket.Id:N} {ticket.Number} {StatusOf(ticket)} {Category(ticket)} {ticket.CreatedAt.ToUnixTimeSeconds()} ")
            + Invariant($"{(ticket.Editable ? 1 : 0)} {ticket.Messages[0].Name}"));
        send("subject " + ticket.Subject);
        foreach (var message in ticket.Messages)
        {
            send(Invariant($"msg {(message.Author == TicketAuthor.Staff ? 'S' : 'P')} {message.At.ToUnixTimeSeconds()} {message.Name}"));
            foreach (var line in message.Text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
            {
                for (var offset = 0; offset < Math.Max(1, line.Length); offset += TextCharacters)
                {
                    send((offset == 0 ? "text " : "more ") + line.Substring(offset, Math.Min(TextCharacters, line.Length - offset)));
                }
            }
        }

        send("viewend");
    }

    // The list of clients released before the conversation: plain lines of 48 characters.
    private static void SendLegacyList(IReadOnlyList<SupportTicket> list, Action<string> send)
    {
        send("begin");
        foreach (var ticket in list.Take(LegacyListed))
        {
            send("line " + ticket.Subject);
            foreach (var message in ticket.Messages)
            {
                if (message.Author == TicketAuthor.Staff)
                {
                    send("line Resposta do GM:");
                }

                foreach (var line in message.Text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
                {
                    for (var offset = 0; offset < line.Length; offset += PartCharacters)
                    {
                        send("line " + line.Substring(offset, Math.Min(PartCharacters, line.Length - offset)));
                    }
                }
            }

            send(ticket.Status switch
            {
                TicketStatus.Closed => ticket.ClosedByPlayer ? "line Cancelado por você" : "line Fechado pela equipe",
                TicketStatus.Answered => "line Respondido",
                _ => "line Aguardando atendimento"
            });
            send("line ");
        }

        send("end");
    }

    // Open, Answered, Closed (by the staff) or Cancelled (by its player).
    private static string StatusOf(SupportTicket ticket) => ticket.ClosedByPlayer ? "Cancelled" : ticket.Status.ToString();

    private static string Category(SupportTicket ticket) => ticket.Category.Length == 0 ? "-" : ticket.Category;

    private static bool Starts(string text, string command, out string rest)
    {
        var match = text.StartsWith(command, StringComparison.OrdinalIgnoreCase);
        rest = match ? text[command.Length..] : "";
        return match;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
