using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PristonTale.Application.Support;

public enum TicketAuthor
{
    Player,
    Staff
}

public enum TicketStatus
{
    // Waiting for the staff: no answer yet, or the player wrote after the last answer.
    Open,

    // The staff wrote last.
    Answered,

    // Closed by the staff, or cancelled by its player before any answer.
    Closed
}

// One message of a ticket's conversation; the first is the ticket's own message.
public sealed record TicketMessage(DateTimeOffset At, TicketAuthor Author, string Name, string Text);

// The ticket as stored (<id>.json) and, once read, its conversation and closing. The subject's category comes from the
// "[Bug] ", "[Melhoria] " or "[Outro] " prefix the game's window sends.
public sealed record SupportTicket(Guid Id, DateTimeOffset CreatedAt, string Subject, string Message)
{
    public string Category { get; init; } = "";

    // The account and character that opened it, as the staff sees them ("Conta (Personagem)").
    public string AuthorName { get; init; } = "";

    [JsonIgnore]
    public IReadOnlyList<TicketMessage> Messages { get; init; } = [];

    // Null while open; SupportTickets.ClosedByPlayer or SupportTickets.ClosedByStaffPrefix and a name once closed.
    [JsonIgnore]
    public string? ClosedBy { get; init; }

    [JsonIgnore]
    public bool Closed => ClosedBy is not null;

    [JsonIgnore]
    public bool ClosedByPlayer => ClosedBy == SupportTickets.ClosedByPlayer;

    [JsonIgnore]
    public bool Answered => Messages.Any(static message => message.Author == TicketAuthor.Staff);

    // Its player may edit or cancel it until the staff answers.
    [JsonIgnore]
    public bool Editable => !Closed && !Answered;

    [JsonIgnore]
    public TicketStatus Status => Closed ? TicketStatus.Closed
        : Messages.Count > 0 && Messages[^1].Author == TicketAuthor.Staff ? TicketStatus.Answered
        : TicketStatus.Open;

    [JsonIgnore]
    public DateTimeOffset UpdatedAt => Messages.Count > 0 ? Messages[^1].At : CreatedAt;

    // The short number players and staff call the ticket by (#1000 to #9999); the id stays the key.
    [JsonIgnore]
    public int Number => (int)(BitConverter.ToUInt32(Id.ToByteArray(), 0) % 9000) + 1000;
}

// Each ticket is <account>/<id>.json; every later message is a file of its own (<id>.<time>-<random>.msg), so the game
// server, the site and the Discord bot add messages without a shared lock; an answer of the earlier single-answer format
// (<id>.reply) reads as a staff message. <id>.media.<ext> is its attachment and <id>.closed who closed it. A closed ticket
// stays in the history and takes no message, edit or attachment.
public sealed class SupportTickets(string directory)
{
    public const string ClosedByPlayer = "player";
    public const string ClosedByStaffPrefix = "staff:";
    public const string StaffName = "Equipe";
    public static readonly IReadOnlyList<string> Categories = ["Bug", "Melhoria", "Outro"];
    private const string MessageExtension = ".msg";

    public async Task<IReadOnlyList<SupportTicket>> ListAsync(long accountId, CancellationToken ct)
    {
        var path = AccountPath(accountId);
        if (!Directory.Exists(path))
        {
            return [];
        }

        var tickets = new List<SupportTicket>();
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            if (await ReadAsync(file, ct) is { } ticket)
            {
                tickets.Add(ticket);
            }
        }

        return tickets.OrderByDescending(static ticket => ticket.CreatedAt).ToArray();
    }

    public Task<SupportTicket?> FindAsync(long accountId, Guid id, CancellationToken ct)
    {
        var file = TicketPath(accountId, id) + ".json";
        return File.Exists(file) ? ReadAsync(file, ct) : Task.FromResult<SupportTicket?>(null);
    }

    public async Task<Guid> CreateAsync(long accountId, string subject, string message, string authorName, CancellationToken ct)
    {
        var path = AccountPath(accountId);
        Directory.CreateDirectory(path);
        var (category, title) = SplitCategory(subject);
        var ticket = new SupportTicket(Guid.NewGuid(), DateTimeOffset.UtcNow, title, message) { Category = category, AuthorName = authorName };
        var target = Path.Combine(path, ticket.Id.ToString("N") + ".json");
        var temporary = target + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, ticket, cancellationToken: ct);
            }

            File.Move(temporary, target);
            return ticket.Id;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // A message of the conversation, as stored; null for a missing or closed ticket.
    public async Task<TicketMessage?> AddMessageAsync(long accountId, Guid id, TicketAuthor author, string name, string text, CancellationToken ct)
    {
        var path = TicketPath(accountId, id);
        if (!File.Exists(path + ".json") || IsClosed(accountId, id))
        {
            return null;
        }

        var message = new TicketMessage(DateTimeOffset.UtcNow, author, name, text);
        var target = path + "." + message.At.UtcTicks.ToString("D19", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + MessageExtension;
        var temporary = target + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(message), ct);
            File.Move(temporary, target);
            return message;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // A staff answer: a staff message under the staff member's name.
    public async Task<bool> ReplyAsync(long accountId, Guid id, string staffName, string message, CancellationToken ct) =>
        await AddMessageAsync(accountId, id, TicketAuthor.Staff, staffName, message, ct) is not null;

    // Its player changes the subject (and category) and the first message, until the staff answers.
    public async Task<bool> EditAsync(long accountId, Guid id, string subject, string message, CancellationToken ct)
    {
        if (await FindAsync(accountId, id, ct) is not { Editable: true } ticket)
        {
            return false;
        }

        var (category, title) = SplitCategory(subject);
        var target = TicketPath(accountId, id) + ".json";
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(ticket with { Subject = title, Message = message, Category = category }), ct);
            File.Move(temporary, target, overwrite: true);
            return true;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // Its player cancels it, until the staff answers.
    public async Task<bool> CancelAsync(long accountId, Guid id, CancellationToken ct) =>
        await FindAsync(accountId, id, ct) is { Editable: true } && await WriteOnceAsync(accountId, id, ".closed", ClosedByPlayer, ct);

    // The staff closes it, answered or not.
    public Task<bool> CloseAsync(long accountId, Guid id, string staffName, CancellationToken ct) =>
        WriteOnceAsync(accountId, id, ".closed", ClosedByStaffPrefix + staffName, ct);

    // The staff opens a closed ticket again; false when it is missing or open.
    public bool Reopen(long accountId, Guid id)
    {
        var closed = TicketPath(accountId, id) + ".closed";
        if (!File.Exists(closed))
        {
            return false;
        }

        File.Delete(closed);
        return true;
    }

    public bool IsClosed(long accountId, Guid id) => File.Exists(TicketPath(accountId, id) + ".closed");

    public const long MaximumAttachmentBytes = 20 * 1024 * 1024;
    private static readonly string[] AttachmentExtensions = [".png", ".jpg", ".webp", ".mp4", ".webm"];

    public bool Exists(long accountId, Guid id) => File.Exists(TicketPath(accountId, id) + ".json");

    public string? Attachment(long accountId, Guid id)
    {
        var prefix = TicketPath(accountId, id) + ".media";
        return AttachmentExtensions.Select(extension => prefix + extension).FirstOrDefault(File.Exists);
    }

    public async Task<bool> AttachAsync(long accountId, Guid id, Stream content, string extension, CancellationToken ct)
    {
        if (!AttachmentExtensions.Contains(extension) || !Exists(accountId, id) || IsClosed(accountId, id) || Attachment(accountId, id) is not null)
        {
            return false;
        }

        var prefix = TicketPath(accountId, id) + ".media";
        var temporary = prefix + ".upload";
        FileStream stream;
        try
        {
            stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        }
        catch (IOException) when (File.Exists(temporary))
        {
            return false;
        }

        try
        {
            await using (stream)
            {
                if (Attachment(accountId, id) is not null)
                {
                    return false;
                }

                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) != 0)
                {
                    total += read;
                    if (total > MaximumAttachmentBytes)
                    {
                        return false;
                    }
                    await stream.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                if (total == 0)
                {
                    return false;
                }
            }

            File.Move(temporary, prefix + extension);
            return true;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // Every ticket, the ones waiting for the staff first, then answered, then closed; newest first in each.
    public async Task<IReadOnlyList<(long AccountId, SupportTicket Ticket)>> QueueAsync(CancellationToken ct)
    {
        var queue = new List<(long AccountId, SupportTicket Ticket)>();
        if (Directory.Exists(directory))
        {
            foreach (var folder in Directory.EnumerateDirectories(directory))
            {
                if (long.TryParse(Path.GetFileName(folder), out var id))
                {
                    foreach (var ticket in await ListAsync(id, ct))
                    {
                        queue.Add((id, ticket));
                    }
                }
            }
        }

        return queue.OrderBy(static entry => entry.Ticket.Status).ThenByDescending(static entry => entry.Ticket.UpdatedAt).ToArray();
    }

    // "[Melhoria] Título" is the category and the title; a subject without a known category keeps it whole.
    public static (string Category, string Title) SplitCategory(string subject)
    {
        foreach (var category in Categories)
        {
            var prefix = "[" + category + "] ";
            if (subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && subject.Length > prefix.Length)
            {
                return (category, subject[prefix.Length..].Trim());
            }
        }

        return ("", subject);
    }

    private async Task<SupportTicket?> ReadAsync(string file, CancellationToken ct)
    {
        SupportTicket? ticket;
        await using (var stream = File.OpenRead(file))
        {
            ticket = await JsonSerializer.DeserializeAsync<SupportTicket>(stream, cancellationToken: ct);
        }

        if (ticket is null)
        {
            return null;
        }

        var prefix = Path.Combine(Path.GetDirectoryName(file)!, ticket.Id.ToString("N"));
        var messages = new List<TicketMessage> { new(ticket.CreatedAt, TicketAuthor.Player, ticket.AuthorName, ticket.Message) };
        if (File.Exists(prefix + ".reply"))
        {
            messages.Add(new TicketMessage(File.GetLastWriteTimeUtc(prefix + ".reply"), TicketAuthor.Staff, StaffName,
                await File.ReadAllTextAsync(prefix + ".reply", ct)));
        }

        foreach (var message in Directory.EnumerateFiles(Path.GetDirectoryName(file)!, ticket.Id.ToString("N") + ".*" + MessageExtension).Order(StringComparer.Ordinal))
        {
            if (JsonSerializer.Deserialize<TicketMessage>(await File.ReadAllTextAsync(message, ct)) is { } read)
            {
                messages.Add(read);
            }
        }

        return ticket with
        {
            Messages = messages.OrderBy(static message => message.At).ToArray(),
            ClosedBy = File.Exists(prefix + ".closed") ? await File.ReadAllTextAsync(prefix + ".closed", ct) : null
        };
    }

    // Writes <id><suffix> of an existing ticket unless it is already there; two writers race on the move and one wins.
    private async Task<bool> WriteOnceAsync(long accountId, Guid id, string suffix, string text, CancellationToken ct)
    {
        var path = TicketPath(accountId, id);
        var target = path + suffix;
        if (!File.Exists(path + ".json") || File.Exists(target))
        {
            return false;
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, ct);
            File.Move(temporary, target);
            return true;
        }
        catch (IOException) when (File.Exists(target))
        {
            return false;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private string TicketPath(long accountId, Guid id) => Path.Combine(AccountPath(accountId), id.ToString("N"));

    private string AccountPath(long accountId) => Path.Combine(directory, accountId.ToString(CultureInfo.InvariantCulture));
}
