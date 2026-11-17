using System.Text.Json;

namespace PristonTale.Application.Support;

public sealed record SupportTicket(Guid Id, DateTimeOffset CreatedAt, string Subject, string Message, string? Reply = null);

public sealed class SupportTickets(string directory)
{
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
            await using var stream = File.OpenRead(file);
            if (await JsonSerializer.DeserializeAsync<SupportTicket>(stream, cancellationToken: ct) is { } ticket)
            {
                var reply = Path.Combine(path, ticket.Id.ToString("N") + ".reply");
                tickets.Add(File.Exists(reply) ? ticket with { Reply = await File.ReadAllTextAsync(reply, ct) } : ticket);
            }
        }

        return tickets.OrderByDescending(ticket => ticket.CreatedAt).ToArray();
    }

    public async Task<Guid> CreateAsync(long accountId, string subject, string message, CancellationToken ct)
    {
        var path = AccountPath(accountId);
        Directory.CreateDirectory(path);
        var ticket = new SupportTicket(Guid.NewGuid(), DateTimeOffset.UtcNow, subject, message);
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

    public const long MaximumAttachmentBytes = 20 * 1024 * 1024;
    private static readonly string[] AttachmentExtensions = [".png", ".jpg", ".webp", ".mp4", ".webm"];

    public bool Exists(long accountId, Guid id) => File.Exists(Path.Combine(AccountPath(accountId), id.ToString("N") + ".json"));

    public string? Attachment(long accountId, Guid id)
    {
        var prefix = Path.Combine(AccountPath(accountId), id.ToString("N") + ".media");
        return AttachmentExtensions.Select(extension => prefix + extension).FirstOrDefault(File.Exists);
    }

    public async Task<bool> AttachAsync(long accountId, Guid id, Stream content, string extension, CancellationToken ct)
    {
        if (!AttachmentExtensions.Contains(extension) || !Exists(accountId, id) || Attachment(accountId, id) is not null)
        {
            return false;
        }

        var prefix = Path.Combine(AccountPath(accountId), id.ToString("N") + ".media");
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

        return queue.OrderBy(entry => entry.Ticket.Reply is not null).ThenByDescending(entry => entry.Ticket.CreatedAt).ToArray();
    }

    public async Task<bool> ReplyAsync(long accountId, Guid id, string message, CancellationToken ct)
    {
        var path = Path.Combine(AccountPath(accountId), id.ToString("N"));
        var reply = path + ".reply";
        if (!File.Exists(path + ".json") || File.Exists(reply))
        {
            return false;
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, message, ct);
            File.Move(temporary, reply);
            return true;
        }
        catch (IOException) when (File.Exists(reply))
        {
            return false;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private string AccountPath(long accountId) =>
        Path.Combine(directory, accountId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
