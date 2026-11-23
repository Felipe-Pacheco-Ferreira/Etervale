using System.Buffers;
using System.Text;
using System.Text.Json;

namespace PristonTale.Gameplay.UI;

// The touch HUD's combat feed, drawn by the page as the Android client's (touch-feed.js) from HudCombatFeed.
public sealed partial class GameplayHud
{
    // The lines added so far, the kills since entering the world and the newest lines, oldest first; the page shows those
    // past the last count it saw.
    internal string TouchFeed()
    {
        var feed = _combatFeed;
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("added", feed.Added);
            json.WriteNumber("kills", feed.Kills);
            json.WriteStartArray("lines");
            for (var index = feed.Count - 1; index >= 0; index--)
            {
                var line = feed.Newest(index);
                json.WriteStartObject();
                json.WriteNumber("sequence", line.Sequence);
                json.WriteString("kind", line.Kind.ToString());
                json.WriteNumber("amount", line.Amount);
                json.WriteNumber("count", line.Count);
                json.WriteNumber("needed", line.Needed);
                json.WriteString("name", line.Name);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
