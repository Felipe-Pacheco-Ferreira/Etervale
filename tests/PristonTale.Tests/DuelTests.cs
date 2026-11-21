using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

// Duels (DuelRules): the rule, the challenge and its refusals, who fights whom during one, the win at 1 life, losing out of
// the arena or by leaving, the draw at the limit, the notices, the wire and the chat filter.
internal static class DuelTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("duel rules: hunting fields, the arena's circle on the ground, and a blow that reaches the last life leaves 1 and wins", RulesAsync);
        yield return ("a challenge by name reaches the target, refusals say why, and an accepted duel fights only its two, whatever their PvP", ChallengeAsync);
        yield return ("a duel blow leaves the loser at 1 life and nobody dies; the duellists and the field's other players are told", WinAsync);
        yield return ("a duellist out of the arena five seconds or gone from the world loses, and three minutes end in a draw", EndsAsync);
        yield return ("the duel's packets cross the wire, the duel reaches the duellist's snapshot, and other players' results can be hidden", WireAsync);
    }

    private static Task RulesAsync()
    {
        Assert.True(DuelRules.FieldAllows(FieldState.Forest) && !DuelRules.FieldAllows(FieldState.Village) && !DuelRules.FieldAllows(FieldState.Castle),
            "the duel fields differ");
        var center = DuelRules.Center(new Vector3(0, 0, 0), new Vector3(200, 40, 0));
        Assert.True(center == new Vector3(100, 20, 0) && DuelRules.InArena(center, new Vector3(100 + DuelRules.ArenaRadius, 900, 0))
            && !DuelRules.InArena(center, new Vector3(101 + DuelRules.ArenaRadius, 0, 0)), "the arena's circle differs");
        Assert.True(DuelRules.Blow(30, 100) == (30, false) && DuelRules.Blow(100, 100) == (99, true) && DuelRules.Blow(500, 1) == (0, true),
            "a duel blow took the last life or missed the win");
        return Task.CompletedTask;
    }

    private static async Task<(ChatFixture Fixture, ChatFixture.Player Ann, ChatFixture.Player Bob)> DuellingAsync(FieldState state = FieldState.Forest)
    {
        var fixture = new ChatFixture(state: state);
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 20, 20);
        await fixture.SayAsync(ann, "/duelo Bob");
        var request = Body(fixture, bob).DuelRequest;
        if (request is not null)
        {
            Assert.True(await fixture.RunAsync(fixture.World.AnswerDuelAsync(bob.Session, ann.Id, true)), "the duel was not accepted");
        }

        return (fixture, ann, bob);
    }

    private static async Task ChallengeAsync()
    {
        var (fixture, ann, bob) = await DuellingAsync();
        var cid = await fixture.JoinAsync("Cid", 40, 40, level: 60);
        var (annBody, bobBody, cidBody) = (Body(fixture, ann), Body(fixture, bob), Body(fixture, cid));
        var field = Field(fixture);
        Assert.True(annBody.Duel is { } duel && ReferenceEquals(duel, bobBody.Duel) && bobBody.DuelRequest is null && !annBody.Pvp,
            "the accepted challenge started no duel");
        Assert.True(field.SetAttackTarget(ann.Id, bob.Id, sustained: false) && field.SetAttackTarget(bob.Id, ann.Id, sustained: false),
            "the duellists could not fight each other");
        await fixture.RunAsync(fixture.World.SwitchPvpAsync(cid.Id, true));
        annBody.RestorePvp(PvpRules.CombatHoldMs, 0f);
        Assert.True(!field.SetAttackTarget(cid.Id, ann.Id, sustained: false) && !field.SetAttackTarget(ann.Id, cid.Id, sustained: false),
            "a third player fought a duellist");
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(cid)!.Value.Entities.Single(entity => entity.EntityId == ann.Id).Appearance.Duelling,
            "a viewer did not see the duellist's duel, so its client would take it as a target");
        cidBody.Hidden = true;
        Assert.True(await fixture.RunAsync(fixture.World.ChallengeDuelAsync(cid.Session, "Bob")) == DuelChallenge.NotFound, "a hidden player challenged");
        cidBody.Hidden = false;
        fixture.World.TakeTickNotices();

        Assert.True(await fixture.RunAsync(fixture.World.ChallengeDuelAsync(cid.Session, "Ann")) == DuelChallenge.Busy
            && Told(fixture, cid, GameTextId.DuelBusy) is ["Ann"], "a duellist was challenged");
        Assert.True(await fixture.RunAsync(fixture.World.ChallengeDuelAsync(cid.Session, "Nobody")) == DuelChallenge.NotFound
            && await fixture.RunAsync(fixture.World.ChallengeDuelAsync(cid.Session, "cid")) == DuelChallenge.Self, "a missing or own name was challenged");
        var far = await fixture.JoinAsync("Far", 2000, 2000);
        Assert.True(await fixture.RunAsync(fixture.World.ChallengeDuelAsync(cid.Session, "Far")) == DuelChallenge.TooFar, "a far player was challenged");

        await fixture.SayAsync(cid, "/DUEL far");
        Assert.True(cidBody.Duel is null && Body(fixture, far).DuelRequest is null, "a far player got the chat challenge");
        var dan = await fixture.JoinAsync("Dan", 60, 60);
        await fixture.SayAsync(cid, "/DUEL dan");
        Assert.True(Body(fixture, dan).DuelRequest is { FromName: "Cid" }, "the chat command sent no challenge");
        fixture.World.TakeTickNotices();
        Assert.True(!await fixture.RunAsync(fixture.World.AnswerDuelAsync(dan.Session, cid.Id, false)) && Told(fixture, cid, GameTextId.DuelRefused) is ["Dan"]
            && cidBody.Duel is null, "a refusal started a duel or was not told");

        var town = new ChatFixture(state: FieldState.Village);
        var eve = await town.JoinAsync("Eve", 0, 0);
        await town.JoinAsync("Fay", 20, 20);
        Assert.True(await town.RunAsync(town.World.ChallengeDuelAsync(eve.Session, "Fay")) == DuelChallenge.Field, "a duel was challenged in a town");
    }

    private static async Task WinAsync()
    {
        var (fixture, ann, bob) = await DuellingAsync();
        var cid = await fixture.JoinAsync("Cid", 40, 40);
        var (annBody, bobBody) = (Body(fixture, ann), Body(fixture, bob));
        var experience = bobBody.Character.Experience;
        fixture.Tick((int)(PlayerGrace.ArrivalMs / 50f) + 2);
        bobBody.ApplyDamage(bobBody.Life - 2);
        fixture.World.TakeTickNotices();
        Assert.True(Field(fixture).SetAttackTarget(ann.Id, bob.Id, sustained: true), "the opponent was not taken");
        var notices = new List<WorldNotice>();
        for (var step = 0; step < 300 && annBody.Duel is not null; step++)
        {
            fixture.Tick(20);
            notices.AddRange(fixture.World.TakeTickNotices());
        }

        // The loser stood at 1 life when the duel ended (DuelRules.Blow); regeneration may have added some since.
        Assert.True(annBody.Duel is null && bobBody.Duel is null && !bobBody.IsDead && bobBody.Life < bobBody.MaxLife && !bobBody.SlainByPlayer
            && bobBody.Character.Experience == experience, "the duel did not end without a death");
        Assert.True(notices.Any(notice => notice.To == ann.Id && notice.Text == GameText.Of(GameTextId.DuelWon, "Bob"))
            && notices.Any(notice => notice.To == bob.Id && notice.Text == GameText.Of(GameTextId.DuelLost, "Ann"))
            && notices.Any(notice => notice.To == cid.Id && notice.Text == GameText.Of(GameTextId.DuelNotice, "Ann", "Bob")),
            "the result was not told to the duellists and the field");
        Assert.True(!Field(fixture).SetAttackTarget(ann.Id, bob.Id, sustained: false), "the duellists still fought after the duel");
    }

    private static async Task EndsAsync()
    {
        var (fixture, ann, bob) = await DuellingAsync();
        Body(fixture, bob).Teleport(new Vector3(3000, 0, 3000));
        fixture.World.TakeTickNotices();
        fixture.Tick((int)(DuelRules.OutsideMs / 50f) - 5);
        Assert.True(Body(fixture, ann).Duel is not null, "a duellist lost before its time out of the arena");
        fixture.Tick(10);
        Assert.True(Body(fixture, ann).Duel is null && fixture.World.TakeTickNotices().Any(notice => notice.To == ann.Id && notice.Text.Id == GameTextId.DuelWon),
            "the one out of the arena did not lose");

        var (left, cid, dan) = await DuellingAsync();
        await left.RunAsync(left.World.LeaveAsync(ChatFixture.Field, dan.Id, dan.Outbox.Writer));
        left.Tick(1);
        Assert.True(Body(left, cid).Duel is null && left.World.TakeTickNotices().Any(notice => notice.To == cid.Id && notice.Text.Id == GameTextId.DuelWon),
            "the one who stayed did not win");

        var (timed, eve, fay) = await DuellingAsync();
        timed.Tick((int)(DuelRules.LimitMs / 50f) + 2);
        var told = timed.World.TakeTickNotices();
        Assert.True(Body(timed, eve).Duel is null && told.Any(notice => notice.To == eve.Id && notice.Text == GameText.Of(GameTextId.DuelDraw, "Bob"))
            && told.Any(notice => notice.To == fay.Id && notice.Text.Id == GameTextId.DuelDraw), "the limit was no draw");
    }

    private static async Task WireAsync()
    {
        var answer = new byte[DuelAnswerPacket.Size];
        Assert.True(DuelAnswerPacket.Read(answer.AsSpan(0, new DuelAnswerPacket(7, true).Write(answer))) == new DuelAnswerPacket(7, true),
            "the duel answer did not round-trip");
        var asked = new byte[DuelAskedPacket.MaxSize];
        Assert.True(DuelAskedPacket.Read(asked.AsSpan(0, new DuelAskedPacket(9, "Ann").Write(asked))) == new DuelAskedPacket(9, "Ann"),
            "the challenge did not round-trip");
        var description = new EntityDescriptionPacket(7, 1, Contracts.World.EntityKind.Player, 60, 1, 1f, "Ann", "",
            new DescribedPlayer(Contracts.Characters.PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, false, false, 0, 0) { Duelling = true });
        var looks = new byte[EntityDescriptionPacket.MaximumSize];
        Assert.True(EntityDescriptionPacket.Read(looks.AsSpan(0, description.Write(looks))).Player is { Duelling: true, Pvp: false, Murderer: false },
            "the duelling flag did not round-trip");
        var state = new byte[DuelStatePacket.MaxSize];
        var sent = new DuelStatePacket(4, "Bob", new Vector3(10, 20, -30), 125, 3);
        Assert.True(DuelStatePacket.Read(state.AsSpan(0, sent.Write(state))) is { OpponentId: 4, OpponentName: "Bob", SecondsLeft: 125, OutsideSecondsLeft: 3 } read
            && read.Center == sent.Center, "the duel state did not round-trip");

        var (fixture, ann, bob) = await DuellingAsync();
        fixture.Tick(1);
        Assert.True(ChatFixture.Latest(ann)!.Value.Duel is { OpponentId: var opponent, OpponentName: "Bob", SecondsLeft: > 170, OutsideSecondsLeft: 0 }
            && opponent == bob.Id, "the duel did not reach the duellist's snapshot");

        var notice = new ChatLineInfo(ChatKind.System, 0, "", GameText.Of(GameTextId.DuelNotice, "Ann", "Bob"));
        var own = new ChatLineInfo(ChatKind.System, 0, "", GameText.Of(GameTextId.DuelWon, "Bob"));
        var hiding = new ClientSettings { HideDuelNotices = true };
        Assert.True(ChatLineFilter.Hides(hiding, notice) && !ChatLineFilter.Hides(hiding, own) && !ChatLineFilter.Hides(new ClientSettings(), notice),
            "the duel notices were not hidden by the option alone");
    }

    private static FieldInstance Field(ChatFixture fixture) =>
        fixture.World.TryGetOpenField(ChatFixture.Field, out var field) ? field : throw new InvalidOperationException("the fixture field is not open");

    private static WorldPlayer Body(ChatFixture fixture, ChatFixture.Player player) =>
        ((IFieldCombatHost)Field(fixture)).TryGetPlayer(player.Id, out var body) ? body : throw new InvalidOperationException("the player is not in the field");

    private static IReadOnlyList<string>? Told(ChatFixture fixture, ChatFixture.Player player, GameTextId id) =>
        fixture.World.TakeTickNotices().LastOrDefault(notice => notice.To == player.Id && notice.Text.Id == id) is { Text: var text } notice && notice.To != 0
            ? text.Args
            : null;
}
