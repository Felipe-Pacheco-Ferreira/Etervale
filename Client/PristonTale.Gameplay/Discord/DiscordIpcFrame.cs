using System.Buffers.Binary;

namespace PristonTale.Gameplay.Discord;

public enum DiscordIpcOpcode
{
    Handshake = 0,
    Frame = 1,
    Close = 2,
    Ping = 3,
    Pong = 4
}

public readonly record struct DiscordIpcMessage(DiscordIpcOpcode Opcode, byte[] Payload);

// Discord's local IPC frame: a little-endian 32-bit opcode, a little-endian 32-bit payload length, then the payload
// (UTF-8 JSON).
public static class DiscordIpcFrame
{
    public const int HeaderSize = 8;

    // Discord's own replies stay far below this; a longer length means the stream is not Discord's.
    public const int LongestPayload = 64 * 1024;

    public static byte[] Encode(DiscordIpcOpcode opcode, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, (int)opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }

    public static async Task WriteAsync(Stream stream, DiscordIpcOpcode opcode, byte[] payload, CancellationToken cancellation)
    {
        await stream.WriteAsync(Encode(opcode, payload), cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    public static async Task<DiscordIpcMessage> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        var opcode = BinaryPrimitives.ReadInt32LittleEndian(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > LongestPayload || !Enum.IsDefined((DiscordIpcOpcode)opcode))
        {
            throw new InvalidDataException($"Not a Discord IPC frame (opcode {opcode}, length {length}).");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellation).ConfigureAwait(false);
        return new DiscordIpcMessage((DiscordIpcOpcode)opcode, payload);
    }
}
