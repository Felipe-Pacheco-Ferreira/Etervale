using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C: the receiver's titles (TitleRules), sent when they change. {header (uint16 Version = 1, uint16 Flags = 0), uint8
// Shown (the title code, 0 none), uint32 Owned (bit 1 << code for each title it owns: the permanent ones earned and the
// positions it holds)}.
public sealed record TitlesPacket(byte Shown, uint Owned) : IFramePacket
{
    public const ushort Version = 1;
    public const int Size = 8 + WireHeader.Size + 1 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Titles);
        WireHeader.Write(ref writer, Version);
        writer.Byte(Shown);
        writer.UInt32(Owned);
        return writer.Finish();
    }

    public static TitlesPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Titles");
        return new TitlesPacket(reader.Byte(), reader.UInt32());
    }
}

// C->S: show a title over the name, or none. {header (uint16 Version = 1, uint16 Flags = 0), uint8 Title (the code, 0
// none)}. When the shown title changes the server answers with Titles; a title the player does not own changes nothing.
public sealed record ChooseTitlePacket(byte Title) : IFramePacket
{
    public const ushort Version = 1;
    public const int Size = 8 + WireHeader.Size + 1;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ChooseTitle);
        WireHeader.Write(ref writer, Version);
        writer.Byte(Title);
        return writer.Finish();
    }

    public static ChooseTitlePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Choose title");
        return new ChooseTitlePacket(reader.Byte());
    }
}
