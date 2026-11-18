using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Parties;

namespace PristonTale.Tests;

internal static class PartyDirectoryTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("party directory creates a real solo group, leader controls admission and its last disconnect removes it", LifecycleAsync);
        yield return ("party directory closed joins require the leader, level gaps and stale leaders refuse", ClosedAndStaleAsync);
        yield return ("party directory serializes competing joins and late invitations cannot move an existing member", CompetingAsync);
        yield return ("party directory policy and empty searches survive presence encoding and frame coalescing", PresenceAsync);
        yield return ("party directory wire bounds maximum names and rejects invalid flags, counts and trailing bytes", WireAsync);
    }

    private static (ChatFixture Fixture, PartyAppService Parties) Fixture()
    {
        var fixture = new ChatFixture();
        return (fixture, new PartyAppService(fixture.Sessions, fixture.World, fixture.Directory));
    }

    private static Task Command(ChatFixture f, PartyAppService service, ChatFixture.Player player, PartyCommand command, ulong member = 0) =>
        f.RunAsync(service.CommandAsync(player.Session.SessionToken, command, member));

    private static WorldSnapshot Snapshot(ChatFixture f, ChatFixture.Player player)
    {
        f.Tick(1);
        return ChatFixture.Latest(player)!.Value;
    }

    private static async Task LifecycleAsync()
    {
        var (f, service) = Fixture();
        var ann = await f.JoinAsync("Ann", 0, 0);
        var bob = await f.JoinAsync("Bob", 100, 0);
        await Command(f, service, ann, PartyCommand.Create);
        await Command(f, service, ann, PartyCommand.Create);
        var own = Snapshot(f, ann).Party!;
        Assert.True(own.Members.Count == 1 && own.Members[0].EntityId == ann.Id && !own.IsOpen,
            "creation must register exactly one authoritative, closed solo group");
        Assert.True(f.World.Parties.MembersOf(ann.Id).SequenceEqual([ann.Id]), "solo group absent from chat roster");
        await Command(f, service, bob, PartyCommand.Search);
        var entries = Snapshot(f, bob).PartyDirectory!;
        Assert.True(entries is [{ LeaderName: "Ann", FieldId: ChatFixture.Field, AverageLevel: 1, MemberCount: 1, IsOpen: false }],
            "directory did not describe the real solo leader");
        await Command(f, service, ann, PartyCommand.Open);
        await Command(f, service, bob, PartyCommand.Join, ann.Id);
        Assert.True(Snapshot(f, bob).Party is { Members.Count: 2, IsOpen: true }, "open join did not take effect immediately");
        await Command(f, service, bob, PartyCommand.Close);
        Assert.True(Snapshot(f, ann).Party!.IsOpen, "a nonleader closed the group");
        await Command(f, service, bob, PartyCommand.Leave);
        Assert.True(Snapshot(f, ann).Party is { Members.Count: 1, IsOpen: true }, "created group was released at one member");
        await f.RunAsync(f.World.LeaveAsync(ChatFixture.Field, ann.Id, ann.Outbox.Writer));
        await Command(f, service, bob, PartyCommand.Search);
        Assert.True(Snapshot(f, bob).PartyDirectory is { Length: 0 }, "the last disconnect left a directory ghost");
    }

    private static async Task ClosedAndStaleAsync()
    {
        var (f, service) = Fixture();
        var ann = await f.JoinAsync("Ann", 0, 0);
        var bob = await f.JoinAsync("Bob", 100, 0);
        var cid = await f.JoinAsync("Cid", 0, 100);
        var high = await f.JoinAsync("High", 100, 100, level: 40);
        await Command(f, service, ann, PartyCommand.Create);
        await Command(f, service, bob, PartyCommand.Join, ann.Id);
        Assert.True(Snapshot(f, bob).Party is null && Snapshot(f, ann).PartyRequest is { FromName: "Bob" },
            "a closed group joined without its leader's answer");
        await f.RunAsync(service.AnswerAsync(ann.Session.SessionToken, bob.Id, true));
        Assert.True(Snapshot(f, bob).Party is { Members.Count: 2 }, "leader acceptance did not use the existing invitation path");
        await Command(f, service, ann, PartyCommand.Open);
        await Command(f, service, high, PartyCommand.Join, ann.Id);
        Assert.True(Snapshot(f, high).Party is null && high.Link.LastLine == PartyMessages.LevelGap, "directory join bypassed level rules");
        await Command(f, service, cid, PartyCommand.Search);
        Assert.True(Snapshot(f, cid).PartyDirectory![0].LeaderId == ann.Id, "search fixture did not capture the old leader");
        await Command(f, service, ann, PartyCommand.Delegate, bob.Id);
        await Command(f, service, cid, PartyCommand.Join, ann.Id);
        Assert.True(Snapshot(f, cid).Party is null, "stale leader listing joined a different leader's party");
        await Command(f, service, bob, PartyCommand.Disband);
        await Command(f, service, cid, PartyCommand.Join, bob.Id);
        Assert.True(Snapshot(f, cid).Party is null, "stale disbanded listing created a party");
    }

    private static async Task CompetingAsync()
    {
        var (f, service) = Fixture();
        var ann = await f.JoinAsync("Ann", 0, 0);
        var rival = await f.JoinAsync("Rival", 100, 0);
        var late = await f.JoinAsync("Late", 0, 100);
        await Command(f, service, ann, PartyCommand.Create);
        await Command(f, service, ann, PartyCommand.Open);
        await Command(f, service, rival, PartyCommand.Create, 1);
        Assert.True(Snapshot(f, rival).Party!.IsOpen, "creation did not apply its initial admission policy atomically");
        await f.RunAsync(service.InviteAsync(rival.Session.SessionToken, late.Id));
        await Command(f, service, late, PartyCommand.Join, ann.Id);
        await f.RunAsync(service.AnswerAsync(late.Session.SessionToken, rival.Id, true));
        Assert.True(Snapshot(f, late).Party!.Members[0].EntityId == ann.Id && Snapshot(f, rival).Party!.Members.Count == 1,
            "a late invitation moved a player already admitted by another group");
        for (var index = 0; index < 3; index++)
        {
            var member = await f.JoinAsync("Member" + index, 0, index * 100);
            await Command(f, service, member, PartyCommand.Join, ann.Id);
        }

        var first = await f.JoinAsync("First", 0, -100);
        var second = await f.JoinAsync("Second", 0, -200);
        // Both requests are enqueued before the owner tick with one vacancy left.
        await f.RunAsync(Task.WhenAll(service.CommandAsync(first.Session.SessionToken, PartyCommand.Join, ann.Id),
            service.CommandAsync(second.Session.SessionToken, PartyCommand.Join, ann.Id)));
        Assert.True(Snapshot(f, ann).Party!.Members.Count == PartyLimits.MaxMembers, "competing joins exceeded group capacity");
        var joined = (Snapshot(f, first).Party is null ? 0 : 1) + (Snapshot(f, second).Party is null ? 0 : 1);
        Assert.True(joined == 1, "competing join requests did not resolve to exactly one vacancy");
        Assert.True(first.Link.LastLine == PartyMessages.CannotEnter || second.Link.LastLine == PartyMessages.CannotEnter,
            "the full group did not refuse the losing join request");
    }

    private static Task PresenceAsync()
    {
        var wire = new WireRoundTrip(1);
        PartyMemberView[] members = [new(1, "Ann", 1, 100, 100, 0, 0)];
        var closed = new WorldSnapshot(1, [], fieldId: 3) { Party = new PartyView(members) };
        Assert.True(wire.Push(closed).Last(update => update.Party is not null).Party is { IsOpen: false, Members.Count: 1 },
            "solo party was lost through the wire");
        var open = closed with { Party = new PartyView(members, true) };
        Assert.True(wire.Push(open).Last(update => update.Party is not null).Party!.IsOpen, "admission change without roster change was lost");
        var search = open with { PartyDirectory = [new(1, "Ann", 3, 1, 1, true)] };
        Assert.True(wire.Push(search.WithEvents([]).WithoutFrame()).Last(update => update.PartyDirectory is not null).PartyDirectory!
            .SequenceEqual(search.PartyDirectory!), "frame coalescing dropped the directory response");
        var empty = search with { PartyDirectory = new PartyDirectoryEntry[0] };
        Assert.True(wire.Push(empty).Single(update => update.PartyDirectory is not null).PartyDirectory!.Count == 0,
            "an empty result did not clear previous entries");
        Assert.True(!wire.Push(empty).Any(update => update.PartyDirectory is not null), "an unchanged result was retransmitted each tick");
        Assert.True(wire.Push(empty with { PartyDirectory = new PartyDirectoryEntry[0] }).Any(update => update.PartyDirectory is { Count: 0 }),
            "a repeated empty search did not receive a new response");
        Assert.True(wire.Push(empty with { Party = null }).Last(update => update.Party is not null).Party is { Members.Count: 0, IsOpen: false },
            "party leave retained admission metadata");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var entries = Enumerable.Range(1, PartyLimits.MaxDirectoryEntries)
            .Select(index => new PartyDirectoryEntry((ulong)index, new string('A', AccountWire.MaxNameBytes), 3, 20, 6, true)).ToArray();
        var length = new PartyDirectoryPacket(1, true, true, entries).Write(buffer);
        Assert.True(length <= FrameCodec.MaxFrameSize && PartyDirectoryPacket.MaxSize <= FrameCodec.MaxFrameSize,
            "maximum directory exceeds a transport frame");
        var read = PartyDirectoryPacket.Read(buffer.AsSpan(0, length));
        Assert.True(read.OwnLeaderId == 1 && read.OwnOpen && read.HasDirectory && read.Entries.SequenceEqual(entries),
            "directory failed to round-trip maximum-length names");
        length = new PartyDirectoryPacket(0, false, true, []).Write(buffer);
        buffer[20] = 2;
        Assert.Throws<InvalidDataException>(() => PartyDirectoryPacket.Read(buffer.AsSpan(0, length)), "invalid boolean accepted");
        length = new PartyDirectoryPacket(0, false, true, []).Write(buffer);
        buffer[22] = PartyLimits.MaxDirectoryEntries + 1;
        Assert.Throws<InvalidDataException>(() => PartyDirectoryPacket.Read(buffer.AsSpan(0, length)), "oversized count accepted");
        length = new PartyDirectoryPacket(0, false, true, []).Write(buffer);
        Assert.Throws<InvalidDataException>(() => PartyDirectoryPacket.Read(buffer.AsSpan(0, length + 1)), "trailing bytes accepted");
        foreach (var command in new[] { PartyCommand.Create, PartyCommand.Open, PartyCommand.Close, PartyCommand.Search, PartyCommand.Join })
        {
            var commandLength = new PartyCommandPacket(command, 9).Write(buffer);
            Assert.True(PartyCommandPacket.Read(buffer.AsSpan(0, commandLength)).Command == command, "new party command did not round-trip");
        }

        return Task.CompletedTask;
    }
}
