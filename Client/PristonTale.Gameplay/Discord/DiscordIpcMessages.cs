using System.Buffers;
using System.Text.Json;

namespace PristonTale.Gameplay.Discord;

// A reply from Discord: Event "READY" answers the handshake, "ERROR" a refused command (Error holds its message); a
// close frame carries only Error.
public sealed record DiscordIpcReply(string Command, string Event, string Nonce, string Error);

// The JSON bodies of Discord's local RPC: the handshake and SET_ACTIVITY.
public static class DiscordIpcMessages
{
    public const int Version = 1;
    public const string ReadyEvent = "READY";
    public const string ErrorEvent = "ERROR";
    public const string SetActivityCommand = "SET_ACTIVITY";

    public static byte[] Handshake(string applicationId) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("v", Version);
        json.WriteString("client_id", applicationId);
        json.WriteEndObject();
    });

    public static byte[] SetActivity(int processId, DiscordActivity activity, string nonce) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteString("cmd", SetActivityCommand);
        json.WriteStartObject("args");
        json.WriteNumber("pid", processId);
        if (activity is null)
        {
            json.WriteNull("activity");
        }
        else
        {
            WriteActivity(json, activity);
        }

        json.WriteEndObject();
        json.WriteString("nonce", nonce);
        json.WriteEndObject();
    });

    public static DiscordIpcReply ReadReply(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new DiscordIpcReply(null, null, null, null);
        }

        string error = null;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            error = Text(data, "message");
        }

        return new DiscordIpcReply(Text(root, "cmd"), Text(root, "evt"), Text(root, "nonce"), error ?? Text(root, "message"));
    }

    private static void WriteActivity(Utf8JsonWriter json, DiscordActivity activity)
    {
        json.WriteStartObject("activity");
        WriteOptional(json, "details", activity.Details);
        WriteOptional(json, "state", activity.State);
        if (activity.StartUnixSeconds is { } start)
        {
            json.WriteStartObject("timestamps");
            json.WriteNumber("start", start);
            json.WriteEndObject();
        }

        json.WriteStartObject("assets");
        WriteOptional(json, "large_image", activity.LargeImage);
        WriteOptional(json, "large_text", activity.LargeText);
        WriteOptional(json, "small_image", activity.SmallImage);
        WriteOptional(json, "small_text", activity.SmallText);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter json, string name, string value)
    {
        if (value is not null)
        {
            json.WriteString(name, value);
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static byte[] Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            write(json);
        }

        return buffer.WrittenSpan.ToArray();
    }
}
