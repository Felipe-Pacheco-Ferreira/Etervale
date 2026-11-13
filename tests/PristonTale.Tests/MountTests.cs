using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class MountTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a mounted player's reports may cover the mounted speed, a player on foot's may not", BudgetAsync);
        yield return ("mounting is refused dead, busy, in combat and in action fields; attacks, casts, hits and warps dismount", RefusalsAsync);
        yield return ("the mount request reaches the world, every viewer's description carries the mount and an action field refuses it", WorldAndWireAsync);
    }

    private static Task BudgetAsync()
    {
        var registry = new CharacterClassRegistry();
        var rider = new WorldPlayer(1, new Vector3(100, 0, 100), registry.CreatePlayer(PlayerClass.Fighter, "Rider"), false, default);
        var walker = new WorldPlayer(2, new Vector3(100, 0, 100), registry.CreatePlayer(PlayerClass.Fighter, "Walker"), false, default);
        var moveSpeed = rider.Character.Derived.MoveSpeed;
        Assert.True(MountRules.UnitsPerSecond(moveSpeed) == Locomotion.RunUnitsPerSecond(moveSpeed) * MountRules.SpeedFactor, "the mounted speed is not the run speed times the factor");
        Assert.True(rider.TryMount(FieldState.Forest) && rider.Mounted, "an idle player out of combat could not mount");
        Assert.True(rider.ReportMovement(new Vector3(100, 0, 100), 0f, false, false, 0, 0) == MovementVerdict.Accepted
            && walker.ReportMovement(new Vector3(100, 0, 100), 0f, false, false, 0, 0) == MovementVerdict.Accepted, "first reports rejected");

        // Two seconds at the mounted speed: past what running allows (the budget holds two seconds of 1.35 x run speed).
        var far = new Vector3(100 + MountRules.UnitsPerSecond(moveSpeed) * 2f, 0, 100);
        Assert.True(rider.ReportMovement(far, 0f, true, true, 0, 2000) == MovementVerdict.Accepted, "a mounted player's ride was rejected");
        Assert.True(walker.ReportMovement(far, 0f, true, true, 0, 2000) == MovementVerdict.Rejected, "a player on foot moved at the mounted speed");

        rider.Dismount();
        var farther = far with { X = far.X + MountRules.UnitsPerSecond(moveSpeed) * 2f };
        Assert.True(rider.ReportMovement(farther, 0f, true, true, 0, 4000) == MovementVerdict.Rejected, "a dismounted player kept the mounted speed");
        return Task.CompletedTask;
    }

    private static Task RefusalsAsync()
    {
        var registry = new CharacterClassRegistry();
        WorldPlayer Fresh() => new(1, new Vector3(100, 0, 100), registry.CreatePlayer(PlayerClass.Fighter, "Rider"), false, default);

        Assert.True(!Fresh().TryMount(FieldState.Action), "a player mounted in an action field");
        Assert.True(!MountRules.MayMount(true, true, true, 0f, FieldState.Forest), "an overweight player may mount");
        Assert.True(!MountRules.RidesFast(true, true) && MountRules.RidesFast(true, false), "an overweight rider rides at the mounted speed");
        Assert.True(Fresh().TryMount(FieldState.Village) && Fresh().TryMount(FieldState.Forest), "a player could not mount in a village or a field");

        var dead = Fresh();
        dead.ApplyDamage(1_000_000);
        Assert.True(dead.IsDead && !dead.TryMount(FieldState.Forest), "a dead player mounted");

        var swinging = Fresh();
        Assert.True(swinging.TryMount(FieldState.Forest), "mount");
        swinging.BeginSwing(2, new Vector3(110, 0, 100), 500, 250);
        Assert.True(!swinging.Mounted, "a swing kept the mount");
        Assert.True(!swinging.TryMount(FieldState.Forest), "a swinging player mounted");
        swinging.TickAction(500, out _);
        Assert.True(!swinging.IsBusy && !swinging.TryMount(FieldState.Forest), "a player mounted right after its swing, still in combat");
        swinging.TickAction(MountRules.CombatHoldMs, out _);
        Assert.True(swinging.TryMount(FieldState.Forest), "combat did not end after its hold");

        var casting = Fresh();
        Assert.True(casting.TryMount(FieldState.Forest), "mount");
        casting.BeginCast(new Vector3(120, 0, 100), 600);
        Assert.True(!casting.Mounted, "a cast kept the mount");

        var hurt = Fresh();
        Assert.True(hurt.TryMount(FieldState.Forest), "mount");
        hurt.ApplyDamage(1);
        Assert.True(!hurt.Mounted && !hurt.TryMount(FieldState.Forest), "a hit kept the mount or let the player mount again at once");
        hurt.TickAction(MountRules.CombatHoldMs - 1f, out _);
        Assert.True(!hurt.TryMount(FieldState.Forest), "combat ended before its hold");
        hurt.TickAction(1f, out _);
        Assert.True(hurt.TryMount(FieldState.Forest), "combat did not end after its hold");

        var guarded = Fresh();
        Assert.True(guarded.TryMount(FieldState.Forest), "mount");
        guarded.Guard(1000);
        guarded.ApplyDamage(5);
        Assert.True(guarded.Mounted, "a hit the guard ignored dismounted the player");

        var warped = Fresh();
        Assert.True(warped.TryMount(FieldState.Forest), "mount");
        warped.EnterField(new Vector3(500, 0, 500), inVillage: false);
        Assert.True(!warped.Mounted, "a warp kept the mount");
        return Task.CompletedTask;
    }

    private static async Task WorldAndWireAsync()
    {
        var fixture = new ChatFixture();
        var ann = await fixture.JoinAsync("Ann", 0, 0);
        var bob = await fixture.JoinAsync("Bob", 10, 10);
        Assert.True(await fixture.RunAsync(fixture.World.MountAsync(ann.Id, mount: true)), "the world refused an idle player's mount");
        fixture.Tick(1);
        Assert.True(Mounted(bob, ann.Id) && Mounted(ann, ann.Id), "the mount did not reach every viewer's view");
        Assert.True(await fixture.RunAsync(fixture.World.MountAsync(ann.Id, mount: false)), "the dismount was refused");
        fixture.Tick(1);
        Assert.True(!Mounted(bob, ann.Id), "the dismount did not reach the viewer");

        var action = new ChatFixture(state: FieldState.Action);
        var cid = await action.JoinAsync("Cid", 0, 0);
        Assert.True(!await action.RunAsync(action.World.MountAsync(cid.Id, mount: true)), "an action field allowed the mount");

        var request = new byte[MountRequestPacket.Size];
        foreach (var mount in new[] { true, false })
        {
            Assert.True(new MountRequestPacket(mount).Write(request) == MountRequestPacket.Size && MountRequestPacket.Read(request).Mount == mount
                && BitConverter.ToUInt32(request, 4) == EtervaleOpcodes.MountRequest, "the mount request did not round-trip");
        }

        request[10] = 2;
        Assert.Throws<InvalidDataException>(() => MountRequestPacket.Read(request), "a mount request with unknown flags was read");

        var description = new EntityDescriptionPacket(7, 1, EntityKind.Player, 10, 1, 1f, "Ann", "",
            new DescribedPlayer(PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, true, false, 0, 0) { Mounted = true });
        var buffer = new byte[EntityDescriptionPacket.MaximumSize];
        var read = EntityDescriptionPacket.Read(buffer.AsSpan(0, description.Write(buffer)));
        Assert.True(read.Player is { Mounted: true, InVillage: true, Phoenix: false }, "the description's mount flag did not round-trip");
    }

    private static bool Mounted(ChatFixture.Player viewer, ulong rider) =>
        ChatFixture.Latest(viewer)!.Value.Entities.Single(entity => entity.EntityId == rider).Appearance.Mounted;
}
