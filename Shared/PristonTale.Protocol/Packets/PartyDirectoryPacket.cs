using PristonTale.Contracts.Social;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C: own party admission policy, followed by an optional server-local directory response. A separate message keeps
// the original PartyMembers body unchanged. A search response replaces the previous result, including an empty one.
public sealed record PartyDirectoryPacket(ulong OwnLeaderId, bool OwnOpen, bool HasDirectory,
    IReadOnlyList<PartyDirectoryEntry> Entries) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 8 + 1 + 1 + 2;
    public const int MaxSize = MinimumSize + PartyLimits.MaxDirectoryEntries * (8 + 2 + AccountWire.MaxNameBytes + 4 * 3 + 1);

    public int Write(Span<byte> destination)
    {
        if (Entries.Count > PartyLimits.MaxDirectoryEntries || !HasDirectory && Entries.Count != 0 || OwnLeaderId == 0 && OwnOpen)
        {
            throw new ArgumentException("Invalid party directory state.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyDirectory);
        PartyWire.Header(ref writer);
        writer.UInt64(OwnLeaderId);
        writer.Byte(OwnOpen ? (byte)1 : (byte)0);
        writer.Byte(HasDirectory ? (byte)1 : (byte)0);
        writer.UInt16((ushort)Entries.Count);
        foreach (var entry in Entries)
        {
            Validate(entry);
            writer.UInt64(entry.LeaderId);
            Utf8Text.Write(ref writer, entry.LeaderName, AccountWire.MaxNameBytes);
            writer.Int32(entry.FieldId);
            writer.Int32(entry.AverageLevel);
            writer.Int32(entry.MemberCount);
            writer.Byte(entry.IsOpen ? (byte)1 : (byte)0);
        }

        return writer.Finish();
    }

    public static PartyDirectoryPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        PartyWire.ReadHeader(ref reader);
        var ownLeader = reader.UInt64();
        var ownOpen = ReadBool(ref reader);
        var hasDirectory = ReadBool(ref reader);
        var count = reader.UInt16();
        if (count > PartyLimits.MaxDirectoryEntries || !hasDirectory && count != 0 || ownLeader == 0 && ownOpen)
        {
            throw new InvalidDataException("Invalid party directory state.");
        }

        var entries = new PartyDirectoryEntry[count];
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = new PartyDirectoryEntry(reader.UInt64(), PartyWire.Name(ref reader), reader.Int32(), reader.Int32(),
                reader.Int32(), ReadBool(ref reader));
            Validate(entry);
            entries[index] = entry;
        }

        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("Party directory has trailing bytes.");
        }

        return new PartyDirectoryPacket(ownLeader, ownOpen, hasDirectory, entries);
    }

    private static bool ReadBool(ref PacketReader reader) => reader.Byte() switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Invalid party directory flag.")
    };

    private static void Validate(PartyDirectoryEntry entry)
    {
        if (entry.LeaderId == 0 || entry.LeaderName.Length == 0 || entry.AverageLevel < 1
            || entry.MemberCount is < 1 or > PartyLimits.MaxMembers)
        {
            throw new InvalidDataException("Invalid party directory entry.");
        }
    }
}
