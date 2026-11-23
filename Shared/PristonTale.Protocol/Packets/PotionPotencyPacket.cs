using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public readonly record struct PotionPotencyEntry(Guid InstanceId, int LifeMin, int LifeMax, int ManaMin, int ManaMax, int StaminaMin, int StaminaMax)
    : IWireEntry<PotionPotencyEntry>
{
    public static int Size => 16 + sizeof(int) * 6;

    public void Write(ref PacketWriter writer)
    {
        writer.Guid(InstanceId);
        writer.Int32(LifeMin);
        writer.Int32(LifeMax);
        writer.Int32(ManaMin);
        writer.Int32(ManaMax);
        writer.Int32(StaminaMin);
        writer.Int32(StaminaMax);
    }

    public static PotionPotencyEntry Read(ref PacketReader reader) =>
        new(reader.Guid(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
}

public sealed record PotionPotencyPacket(long Revision, IReadOnlyList<PotionPotencyEntry> Items)
{
    public const int MaxEntries = ItemDetailsPartPacket.MaxItemsPerPart;
    public const int HeaderSize = 8 + WireHeader.Size + sizeof(long) + sizeof(int);

    public int Write(Span<byte> destination)
    {
        if (Items.Count > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(Items), "Too many potion entries.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.PotionPotency);
        WireHeader.Write(ref writer, 1);
        writer.Int64(Revision);
        WireList.Write(ref writer, Items);
        return writer.Finish();
    }

    public static PotionPotencyPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        WireHeader.Read(ref reader, "Potion potency");
        return new PotionPotencyPacket(reader.Int64(), WireList.Read<PotionPotencyEntry>(ref reader, MaxEntries, "Potion potency"));
    }
}
