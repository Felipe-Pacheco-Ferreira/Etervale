using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public static class EventCalendarWire
{
    public const ushort Version = 1;
    public const int MaxBosses = 64;
    public const int MaxBossSets = 24;
    public const int MaxOccurrences = 288;
    public const int MaxNameBytes = 32;
    public const byte NoBossSet = 0xFF;
    public const int OccurrenceSize = 12;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Event calendar message");
}

// C->S the event calendar, when its window opens: {header, Guid OperationId}.
public readonly record struct EventCalendarRequestPacket(Guid OperationId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 16;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.EventCalendarRequest);
        EventCalendarWire.Header(ref writer);
        writer.Guid(OperationId);
        return writer.Finish();
    }

    public static EventCalendarRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        EventCalendarWire.ReadHeader(ref reader);
        return new EventCalendarRequestPacket(reader.Guid());
    }
}

public readonly record struct CalendarBossWire(string Name, string FieldName);

// One occurrence: Kind (CalendarEventKind), Flags (CalendarOccurrenceFlags), the bosses of a boss call (bit i for boss i),
// and its start and length in whole minutes (Unix minutes, UTC; both 0 for an event that always runs).
public readonly record struct CalendarOccurrenceWire(byte Kind, byte Flags, ulong Bosses, uint StartMinute, uint Minutes)
{
    // An occurrence from start to end, the start rounded down to its minute and the end up; a time before 1970 is 0.
    public static CalendarOccurrenceWire Between(byte kind, byte flags, ulong bosses, DateTimeOffset start, DateTimeOffset end)
    {
        var first = Math.Clamp(start.ToUnixTimeSeconds() / 60, 0, uint.MaxValue);
        var minutes = Math.Clamp((end.ToUnixTimeSeconds() - first * 60 + 59) / 60, 0, uint.MaxValue);
        return new CalendarOccurrenceWire(kind, flags, bosses, (uint)first, (uint)minutes);
    }

    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeSeconds(StartMinute * 60L);

    public DateTimeOffset End => Start.AddMinutes(Minutes);
}

// S->C the event calendar, answering its request: {header, Guid OperationId, int32 ExperiencePercent, uint16 Drops
// (DropEvents), uint8 BossCount, uint8 BossSetCount, {text Name, text FieldName} x BossCount, uint64 x BossSetCount (the
// distinct boss sets of the calls), int32 OccurrenceCount, {uint8 Kind, uint8 Flags, uint8 BossSet (NoBossSet for none),
// uint8 0, uint32 StartMinute, uint32 Minutes} x OccurrenceCount filling the rest}.
public sealed record EventCalendarPacket(Guid OperationId, int ExperiencePercent, ushort Drops, IReadOnlyList<CalendarBossWire> Bosses,
    IReadOnlyList<CalendarOccurrenceWire> Occurrences) : IFramePacket
{
    private const int FixedSize = 8 + WireHeader.Size + 16 + 4 + 2 + 2 + 4;

    public const int MaxSize = FixedSize + EventCalendarWire.MaxBosses * 2 * (2 + EventCalendarWire.MaxNameBytes) + EventCalendarWire.MaxBossSets * 8
        + EventCalendarWire.MaxOccurrences * EventCalendarWire.OccurrenceSize;

    public int Write(Span<byte> destination)
    {
        if (Bosses.Count > EventCalendarWire.MaxBosses || Occurrences.Count > EventCalendarWire.MaxOccurrences)
        {
            throw new InvalidOperationException("The event calendar holds too many bosses or occurrences.");
        }

        var sets = new List<ulong>();
        foreach (var occurrence in Occurrences)
        {
            if (occurrence.Bosses != 0 && !sets.Contains(occurrence.Bosses))
            {
                sets.Add(occurrence.Bosses);
            }
        }

        if (sets.Count > EventCalendarWire.MaxBossSets)
        {
            throw new InvalidOperationException("The event calendar's boss calls hold too many boss sets.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.EventCalendar);
        EventCalendarWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Int32(ExperiencePercent);
        writer.UInt16(Drops);
        writer.Byte((byte)Bosses.Count);
        writer.Byte((byte)sets.Count);
        foreach (var boss in Bosses)
        {
            Utf8Text.Write(ref writer, boss.Name, EventCalendarWire.MaxNameBytes);
            Utf8Text.Write(ref writer, boss.FieldName, EventCalendarWire.MaxNameBytes);
        }

        foreach (var set in sets)
        {
            writer.UInt64(set);
        }

        writer.Int32(Occurrences.Count);
        foreach (var occurrence in Occurrences)
        {
            writer.Byte(occurrence.Kind);
            writer.Byte(occurrence.Flags);
            writer.Byte(occurrence.Bosses == 0 ? EventCalendarWire.NoBossSet : (byte)sets.IndexOf(occurrence.Bosses));
            writer.Byte(0);
            writer.UInt32(occurrence.StartMinute);
            writer.UInt32(occurrence.Minutes);
        }

        return writer.Finish();
    }

    public static EventCalendarPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, FixedSize);
        EventCalendarWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var experience = reader.Int32();
        var drops = reader.UInt16();
        var (bossCount, setCount) = (reader.Byte(), reader.Byte());
        if (bossCount > EventCalendarWire.MaxBosses || setCount > EventCalendarWire.MaxBossSets)
        {
            throw new InvalidDataException("Event calendar holds too many bosses or boss sets.");
        }

        var bosses = new CalendarBossWire[bossCount];
        for (var index = 0; index < bosses.Length; index++)
        {
            bosses[index] = new CalendarBossWire(Utf8Text.Read(ref reader, EventCalendarWire.MaxNameBytes, "Calendar boss"),
                Utf8Text.Read(ref reader, EventCalendarWire.MaxNameBytes, "Calendar boss field"));
        }

        var sets = new ulong[setCount];
        for (var index = 0; index < sets.Length; index++)
        {
            sets[index] = reader.UInt64();
        }

        var count = reader.Int32();
        if (count is < 0 or > EventCalendarWire.MaxOccurrences || reader.Remaining != count * EventCalendarWire.OccurrenceSize)
        {
            throw new InvalidDataException("Event calendar occurrence count disagrees with its length.");
        }

        var occurrences = new CalendarOccurrenceWire[count];
        for (var index = 0; index < occurrences.Length; index++)
        {
            var (kind, flags, set) = (reader.Byte(), reader.Byte(), reader.Byte());
            reader.Skip(1);
            if (set != EventCalendarWire.NoBossSet && set >= sets.Length)
            {
                throw new InvalidDataException("Event calendar occurrence names an unknown boss set.");
            }

            occurrences[index] = new CalendarOccurrenceWire(kind, flags, set == EventCalendarWire.NoBossSet ? 0 : sets[set], reader.UInt32(), reader.UInt32());
        }

        return new EventCalendarPacket(operation, experience, drops, bosses, occurrences);
    }
}
