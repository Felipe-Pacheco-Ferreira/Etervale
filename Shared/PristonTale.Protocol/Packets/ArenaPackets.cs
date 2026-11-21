using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C: the receiver's arena (ArenaRules), sent when what it shows changes. {header (uint16 Version = 1, uint16 Flags =
// 0), uint8 Mode (0 none, 1 one against one, 3 three against three), uint8 State (1 queued, 2 out of its match),
// uint8 OwnStanding, uint8 FoeStanding, uint16 SecondsLeft}. Joining and leaving the queue travel as the chat lines
// "/arena 1x1", "/arena 3x3" and "/arena sair"; the server answers in system lines.
public sealed record ArenaStatePacket(byte Mode, bool Queued, bool Out, int OwnStanding, int FoeStanding, int SecondsLeft) : IFramePacket
{
    public const ushort Version = 1;
    public const int Size = 8 + WireHeader.Size + 4 + 2;
    private const byte QueuedFlag = 1;
    private const byte OutFlag = 2;

    public static ArenaStatePacket None { get; } = new(0, false, false, 0, 0, 0);

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ArenaState);
        WireHeader.Write(ref writer, Version);
        writer.Byte(Mode);
        writer.Byte((byte)((Queued ? QueuedFlag : 0) | (Out ? OutFlag : 0)));
        writer.Byte((byte)Math.Clamp(OwnStanding, 0, byte.MaxValue));
        writer.Byte((byte)Math.Clamp(FoeStanding, 0, byte.MaxValue));
        writer.UInt16((ushort)Math.Clamp(SecondsLeft, 0, ushort.MaxValue));
        return writer.Finish();
    }

    public static ArenaStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Arena state");
        var mode = reader.Byte();
        var state = reader.Byte();
        if (mode is not (0 or 1 or 3) || state > (QueuedFlag | OutFlag))
        {
            throw new InvalidDataException("Arena state has an unknown mode or state.");
        }

        return new ArenaStatePacket(mode, (state & QueuedFlag) != 0, (state & OutFlag) != 0, reader.Byte(), reader.Byte(), reader.UInt16());
    }
}
