using PristonTale.Contracts.Texts;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// The events and rewards window. Track, goal, entry state and claim status are the Simulation's RewardTrack,
// RewardGoal, RewardEntryState and RewardClaimStatus, mapped at the edges.
public static class RewardWire
{
    public const ushort Version = 1;
    public const int MaxTracks = 4;
    public const int MaxEntries = 8;
    public const int MaxItems = 3;
    public const int MaxNameBytes = 64;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "reward events message");
}

public enum RewardEventsAction : byte
{
    Open = 0,
    Claim = 1,
    ClaimAll = 2
}

// C->S {header, uint8 Action, uint8 Track, uint8 Index (a Claim's entry)}: Open asks for the window, Claim claims one
// entry, ClaimAll every entry ready in the track.
public readonly record struct RewardEventsRequestPacket(RewardEventsAction Action, byte Track, byte Index) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 3;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.RewardEventsRequest);
        RewardWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.Byte(Track);
        writer.Byte(Index);
        return writer.Finish();
    }

    public static RewardEventsRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        RewardWire.ReadHeader(ref reader);
        var action = reader.Byte();
        if (!Enum.IsDefined((RewardEventsAction)action))
        {
            throw new InvalidDataException("Reward events request has an unknown action.");
        }

        return new RewardEventsRequestPacket((RewardEventsAction)action, reader.Byte(), reader.Byte());
    }
}

public readonly record struct RewardItemWire(uint Code, ushort Count, string Name);

public sealed record RewardEntryWire(byte Goal, byte State, uint Progress, uint Target, ulong Gold, IReadOnlyList<RewardItemWire> Items);

// SecondsLeft: to the track's end or reset, null without a deadline.
public sealed record RewardTrackWire(byte Track, ushort MinLevel, ulong? SecondsLeft, IReadOnlyList<RewardEntryWire> Entries);

// S->C the window: {header, uint8 TrackCount, tracks}; a track is {uint8 Track, uint8 Flags (1 has a deadline),
// uint16 MinLevel, [varuint SecondsLeft], uint8 EntryCount, entries}; an entry is {uint8 Goal, uint8 State, varuint
// Progress, varuint Target, varuint Gold, uint8 ItemCount, {uint32 Code, uint16 Count, text Name (64 B)} x ItemCount}.
public sealed record RewardEventsPacket(IReadOnlyList<RewardTrackWire> Tracks) : IFramePacket
{
    private const int MaxItemSize = 4 + 2 + 2 + RewardWire.MaxNameBytes;
    private const int MaxEntrySize = 2 + 3 * VarInt.MaxBytes64 + 1 + RewardWire.MaxItems * MaxItemSize;
    private const int MaxTrackSize = 4 + VarInt.MaxBytes64 + 1 + RewardWire.MaxEntries * MaxEntrySize;
    public const int MaxSize = 8 + WireHeader.Size + 1 + RewardWire.MaxTracks * MaxTrackSize;
    private const byte HasDeadline = 1;

    public int Write(Span<byte> destination)
    {
        if (Tracks.Count > RewardWire.MaxTracks)
        {
            throw new InvalidOperationException("The reward window holds at most four tracks.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.RewardEvents);
        RewardWire.Header(ref writer);
        writer.Byte((byte)Tracks.Count);
        foreach (var track in Tracks)
        {
            if (track.Entries.Count > RewardWire.MaxEntries)
            {
                throw new InvalidOperationException("A reward track holds at most eight entries.");
            }

            writer.Byte(track.Track);
            writer.Byte(track.SecondsLeft is null ? (byte)0 : HasDeadline);
            writer.UInt16(track.MinLevel);
            if (track.SecondsLeft is { } seconds)
            {
                writer.VarUInt(seconds);
            }

            writer.Byte((byte)track.Entries.Count);
            foreach (var entry in track.Entries)
            {
                WriteEntry(ref writer, entry);
            }
        }

        return writer.Finish();
    }

    public static RewardEventsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 1);
        RewardWire.ReadHeader(ref reader);
        var trackCount = reader.Byte();
        if (trackCount > RewardWire.MaxTracks)
        {
            throw new InvalidDataException("Reward events hold too many tracks.");
        }

        var tracks = new RewardTrackWire[trackCount];
        for (var index = 0; index < tracks.Length; index++)
        {
            var (track, flags, minLevel) = (reader.Byte(), reader.Byte(), reader.UInt16());
            ulong? seconds = (flags & HasDeadline) != 0 ? reader.VarUInt() : null;
            var entryCount = reader.Byte();
            if (entryCount > RewardWire.MaxEntries)
            {
                throw new InvalidDataException("A reward track holds too many entries.");
            }

            var entries = new RewardEntryWire[entryCount];
            for (var entry = 0; entry < entries.Length; entry++)
            {
                entries[entry] = ReadEntry(ref reader);
            }

            tracks[index] = new RewardTrackWire(track, minLevel, seconds, entries);
        }

        return new RewardEventsPacket(tracks);
    }

    private static void WriteEntry(ref PacketWriter writer, RewardEntryWire entry)
    {
        if (entry.Items.Count > RewardWire.MaxItems)
        {
            throw new InvalidOperationException("A reward entry holds at most three items.");
        }

        writer.Byte(entry.Goal);
        writer.Byte(entry.State);
        writer.VarUInt(entry.Progress);
        writer.VarUInt(entry.Target);
        writer.VarUInt(entry.Gold);
        writer.Byte((byte)entry.Items.Count);
        foreach (var item in entry.Items)
        {
            writer.UInt32(item.Code);
            writer.UInt16(item.Count);
            Utf8Text.Write(ref writer, item.Name, RewardWire.MaxNameBytes);
        }
    }

    private static RewardEntryWire ReadEntry(ref PacketReader reader)
    {
        var (goal, state) = (reader.Byte(), reader.Byte());
        var (progress, target, gold) = (reader.VarUInt32(), reader.VarUInt32(), reader.VarUInt());
        var itemCount = reader.Byte();
        if (itemCount > RewardWire.MaxItems)
        {
            throw new InvalidDataException("A reward entry holds too many items.");
        }

        var items = new RewardItemWire[itemCount];
        for (var index = 0; index < items.Length; index++)
        {
            items[index] = new RewardItemWire(reader.UInt32(), reader.UInt16(), Utf8Text.Read(ref reader, RewardWire.MaxNameBytes, "Reward item name"));
        }

        return new RewardEntryWire(goal, state, progress, target, gold, items);
    }
}

// S->C how a RewardEventsRequest ended: {header, uint8 Action, uint8 Track, uint8 Status (0 done), uint8 Claimed (the
// entries claimed, a bit an index), game text Message}.
public sealed record RewardEventsResultPacket(RewardEventsAction Action, byte Track, byte Status, byte Claimed, GameText Message) : IFramePacket
{
    public const int MaxSize = 8 + WireHeader.Size + 4 + GameTextWire.MaximumSize;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.RewardEventsResult);
        RewardWire.Header(ref writer);
        writer.Byte((byte)Action);
        writer.Byte(Track);
        writer.Byte(Status);
        writer.Byte(Claimed);
        GameTextWire.Write(ref writer, Message);
        return writer.Finish();
    }

    public static RewardEventsResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 4 + GameTextWire.MinimumSize);
        RewardWire.ReadHeader(ref reader);
        var (action, track, status, claimed) = (reader.Byte(), reader.Byte(), reader.Byte(), reader.Byte());
        return new RewardEventsResultPacket((RewardEventsAction)action, track, status, claimed, GameTextWire.Read(ref reader));
    }
}
