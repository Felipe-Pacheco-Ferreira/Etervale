using System.Text.Json;
using PristonTale.Contracts.Social;

namespace PristonTale.DiscordBot.Sources;

// Where a relay stands in one of the game server's activity snapshots: the server run (StreamId) and the last message
// sent on. Saved in the bot's state so a restart neither repeats nor loses lines.
public sealed record ActivityPosition(Guid StreamId, long Sequence);

// The new lines of an activity snapshot. The server writes the last hundred lines of its run every second, numbered from
// 1 in a stream of its own. The first snapshot a fresh cursor sees only marks where it stands (the backlog was before
// the bot), a stream the cursor has not seen before is a server that restarted and every line of it is new, and lines
// the bot itself sent into the game (ExternalChat's prefix) are left out. Peek does not move the cursor: the relay
// commits the position only once Discord took the lines, so a failed send is tried again on the next turn.
public sealed class ActivityCursor(ActivityPosition? start = null)
{
    public ActivityPosition? Position { get; private set; } = start;

    public (IReadOnlyList<PublicGameMessage> Lines, ActivityPosition Position) Peek(PublicGameActivity snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var last = snapshot.Messages.Length == 0 ? 0 : snapshot.Messages.Max(static message => message.Sequence);
        if (Position is null)
        {
            return ([], new ActivityPosition(snapshot.StreamId, last));
        }

        var after = Position.StreamId == snapshot.StreamId ? Position.Sequence : 0;
        var fresh = snapshot.Messages.Where(message => message.Sequence > after).OrderBy(static message => message.Sequence).ToArray();
        return (fresh, new ActivityPosition(snapshot.StreamId, Math.Max(after, last)));
    }

    public void Commit(ActivityPosition position) => Position = position;

    // The snapshot at the path, or null while there is none or it is being replaced.
    public static async Task<PublicGameActivity?> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<PublicGameActivity>(stream, cancellationToken: ct);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
