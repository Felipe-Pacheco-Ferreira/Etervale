using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C: the receiver's Battle Royale (RoyaleRules), sent when what it shows changes. {header (uint16 Version = 1, uint16
// Flags = 0), uint8 State (0 none, 1 registered, 2 fighting, 3 out and watching), uint8 Standing, uint8 Total (the
// registered while registered), uint16 Radius (the safe zone's), uint16 SecondsToShrink (0 once it no longer shrinks),
// uint16 SecondsLeft}. Registering and leaving travel as the chat lines "/royale" and "/royale sair"; the server answers
// in system lines.
public sealed record RoyaleStatePacket(byte State, int Standing, int Total, int Radius, int SecondsToShrink, int SecondsLeft) : IFramePacket
{
    public const ushort Version = 1;
    public const int Size = 8 + WireHeader.Size + 3 + 6;
    public const byte NoneState = 0;
    public const byte RegisteredState = 1;
    public const byte FightingState = 2;
    public const byte OutState = 3;

    public static RoyaleStatePacket None { get; } = new(NoneState, 0, 0, 0, 0, 0);

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.RoyaleState);
        WireHeader.Write(ref writer, Version);
        writer.Byte(State);
        writer.Byte((byte)Math.Clamp(Standing, 0, byte.MaxValue));
        writer.Byte((byte)Math.Clamp(Total, 0, byte.MaxValue));
        writer.UInt16((ushort)Math.Clamp(Radius, 0, ushort.MaxValue));
        writer.UInt16((ushort)Math.Clamp(SecondsToShrink, 0, ushort.MaxValue));
        writer.UInt16((ushort)Math.Clamp(SecondsLeft, 0, ushort.MaxValue));
        return writer.Finish();
    }

    public static RoyaleStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Royale state");
        var state = reader.Byte();
        if (state > OutState)
        {
            throw new InvalidDataException("Royale state has an unknown state.");
        }

        return new RoyaleStatePacket(state, reader.Byte(), reader.Byte(), reader.UInt16(), reader.UInt16(), reader.UInt16());
    }
}
