using System.Numerics;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Duel messages (DuelRules). Each body starts with {uint16 Version = 1, uint16 Flags = 0}; readers reject version 0.
// Names are UTF-8 with a uint16 byte length, within the character names' limit. A challenge travels as the chat line
// "/duelo name"; the server answers in system lines, DuelAsked and DuelState.
public static class DuelWire
{
    public const ushort Version = 1;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Duel message");

    internal static string Name(ref PacketReader reader) => Utf8Text.Read(ref reader, AccountWire.MaxNameBytes, "Duel message");
}

// C->S: answer the challenge DuelAsked showed. {header, uint64 FromId, uint8 Accept (0 or 1)}.
public sealed record DuelAnswerPacket(ulong FromId, bool Accept) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 8 + 1;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.DuelAnswer);
        DuelWire.Header(ref writer);
        writer.UInt64(FromId);
        writer.Byte(Accept ? (byte)1 : (byte)0);
        return writer.Finish();
    }

    public static DuelAnswerPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        DuelWire.ReadHeader(ref reader);
        var from = reader.UInt64();
        return reader.Byte() switch
        {
            0 => new DuelAnswerPacket(from, false),
            1 => new DuelAnswerPacket(from, true),
            _ => throw new InvalidDataException("Duel answer is neither yes nor no.")
        };
    }
}

// S->C: a player challenges the receiver to a duel. {header, uint64 FromId, text FromName}.
public sealed record DuelAskedPacket(ulong FromId, string FromName) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 8 + 2;
    public const int MaxSize = MinimumSize + AccountWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.DuelAsked);
        DuelWire.Header(ref writer);
        writer.UInt64(FromId);
        Utf8Text.Write(ref writer, FromName, AccountWire.MaxNameBytes);
        return writer.Finish();
    }

    public static DuelAskedPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        DuelWire.ReadHeader(ref reader);
        return new DuelAskedPacket(reader.UInt64(), DuelWire.Name(ref reader));
    }
}

// S->C: the receiver's duel, sent when a shown second changes; OpponentId 0 once it duels nobody. {header, uint64
// OpponentId, text OpponentName, float32 X, Y, Z (the arena's center), uint16 SecondsLeft, uint8 OutsideSecondsLeft (0
// inside the arena)}.
public sealed record DuelStatePacket(ulong OpponentId, string OpponentName, Vector3 Center, int SecondsLeft, int OutsideSecondsLeft) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 8 + 2 + 12 + 2 + 1;
    public const int MaxSize = MinimumSize + AccountWire.MaxNameBytes;

    public static DuelStatePacket None { get; } = new(0, "", Vector3.Zero, 0, 0);

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.DuelState);
        DuelWire.Header(ref writer);
        writer.UInt64(OpponentId);
        Utf8Text.Write(ref writer, OpponentName, AccountWire.MaxNameBytes);
        writer.Single(Center.X);
        writer.Single(Center.Y);
        writer.Single(Center.Z);
        writer.UInt16((ushort)Math.Clamp(SecondsLeft, 0, ushort.MaxValue));
        writer.Byte((byte)Math.Clamp(OutsideSecondsLeft, 0, byte.MaxValue));
        return writer.Finish();
    }

    public static DuelStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        DuelWire.ReadHeader(ref reader);
        var opponent = reader.UInt64();
        var name = DuelWire.Name(ref reader);
        var center = new Vector3(reader.Single(), reader.Single(), reader.Single());
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(center.Z))
        {
            throw new InvalidDataException("Duel state has an invalid center.");
        }

        return new DuelStatePacket(opponent, name, center, reader.UInt16(), reader.Byte());
    }
}
