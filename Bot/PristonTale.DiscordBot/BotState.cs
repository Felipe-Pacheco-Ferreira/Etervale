using System.Text.Json;
using PristonTale.DiscordBot.Sources;

namespace PristonTale.DiscordBot;

// What the bot remembers between runs, in discord-bot-state.json beside the executable: where each chat relay stands,
// the news already announced (null until the first read, which announces nothing) and the Discord post and thread of each
// ticket not closed yet. A state file of an earlier format starts over.
public sealed class BotState
{
    public const string FileName = "discord-bot-state.json";
    public const int MostNews = 100;

    public Dictionary<string, ActivityPosition> Activity { get; init; } = [];
    public List<string>? News { get; set; }
    public Dictionary<Guid, TicketPost> Tickets { get; init; } = [];

    public static async Task<BotState> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return new BotState();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<BotState>(stream, cancellationToken: ct) ?? new BotState();
        }
        catch (JsonException)
        {
            return new BotState();
        }
    }

    public async Task SaveAsync(string path, CancellationToken ct)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this), ct);
        File.Move(temporary, path, overwrite: true);
    }
}

// A ticket's post in its category channel and the thread under it: the header text last shown and the
// messages already in the thread (TicketMessageKey), so a game master's line written from the thread is not posted back.
public sealed record TicketPost(long AccountId, ulong ChannelId, ulong MessageId, ulong ThreadId, string Header, HashSet<string> Posted);
