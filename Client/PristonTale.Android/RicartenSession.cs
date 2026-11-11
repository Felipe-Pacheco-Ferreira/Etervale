using System.Numerics;
using System.Diagnostics;
using Android.Widget;
using PristonTale.Client.Net;
using PristonTale.Contracts.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;

namespace PristonTale.Mobile;

internal sealed class RicartenSession(IPlayChannel play, FieldTerrain terrain, RicartenSceneView map, MovementJoystick joystick, TextView notice, Vector3 spawn)
{
    private static readonly TimeSpan LocalStepInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(200);
    private Vector3 _position = spawn;
    private Vector2 _stick;
    private Vector2 _walkVelocity;
    private bool _reportedMoving;
    private OwnerState? _owner;
    private float _facing;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        joystick.DirectionChanged += SetDirection;
        var receive = ReceiveAsync(session.Token);
        var movement = MoveAsync(session.Token);
        try
        {
            var completed = await Task.WhenAny(receive, movement);
            await completed;
            return "A conexão com Ricarten terminou.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return "Você saiu de Ricarten.";
        }
        finally
        {
            joystick.DirectionChanged -= SetDirection;
            session.Cancel();
            try
            {
                await Task.WhenAll(receive, movement);
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested)
            {
            }
        }
    }

    private void SetDirection(Vector2 input) => _stick = input;

    private async Task ReceiveAsync(CancellationToken ct)
    {
        await foreach (var update in play.ReadAllAsync(ct))
        {
            try
            {
                if (update.Warp is { } warp)
                {
                    if (warp.FieldId != FieldCodes.Ricarten)
                    {
                        return;
                    }
                    _position = warp.Position;
                    _walkVelocity = Vector2.Zero;
                    map.UpdatePlayer(_position, _facing, _owner?.MoveSpeed ?? 1, moving: false, running: false, snap: true);
                    notice.Text = "Posição corrigida pelo servidor.";
                }
                if (update.Owner is { } owner)
                {
                    if (_owner is null)
                    {
                        notice.Text = "Joystick para andar. Arraste à direita para mover a câmera.";
                    }
                    _owner = owner;
                }
                foreach (var spawn in update.Spawns)
                {
                    if (!spawn.IsSelf)
                    {
                        map.SetEntity(spawn.EntityId, spawn.Position, spawn.Kind, spawn.TemplateId, spawn.Facing, spawn.ModelScale, spawn.MoveState);
                    }
                }
                foreach (var state in update.States)
                {
                    map.MoveEntity(state.EntityId, state.Position, state.Facing, state.MoveState);
                }
                foreach (var removed in update.Removed)
                {
                    map.RemoveEntity(removed);
                }
                map.PublishEntities();
            }
            finally
            {
                play.Recycle(update);
            }
        }
    }

    private async Task MoveAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(LocalStepInterval);
        var previousStep = Stopwatch.GetTimestamp();
        var previousReport = previousStep;
        while (await timer.WaitForNextTickAsync(ct))
        {
            var now = Stopwatch.GetTimestamp();
            var seconds = Math.Min(.05f, (float)Stopwatch.GetElapsedTime(previousStep, now).TotalSeconds);
            previousStep = now;
            if (_owner is not { Life: > 0 } owner)
            {
                if (_reportedMoving)
                {
                    await ReportAsync(false, false, ct);
                }
                continue;
            }

            var running = !owner.Overweight;
            var speed = running
                ? Locomotion.RunUnitsPerSecond(owner.MoveSpeed)
                : Locomotion.WalkUnitsPerSecond(owner.MoveSpeed, inVillage: true);
            var direction = map.MovementDirection(_stick);
            var velocity = direction * speed;
            if (running)
            {
                _walkVelocity = Vector2.Zero;
            }
            else
            {
                var difference = velocity - _walkVelocity;
                var change = difference.Length();
                var responseSeconds = direction == Vector2.Zero ? .10f : .16f;
                var maxChange = speed * seconds / responseSeconds;
                _walkVelocity = change > maxChange ? _walkVelocity + difference * (maxChange / change) : velocity;
                velocity = _walkVelocity;
            }
            var displacement = velocity * seconds;
            var next = _position + new Vector3(displacement.X, 0f, displacement.Y);
            var slid = terrain.Slide(_position, next);
            var moving = false;
            if (displacement != Vector2.Zero && terrain.TryFloorNear(slid, FieldTerrain.StepUp, out var floor))
            {
                moving = Vector2.DistanceSquared(new(_position.X, _position.Z), new(slid.X, slid.Z)) > 1e-6f;
                _position = slid with { Y = floor };
                _facing = MathF.Atan2(displacement.X, displacement.Y);
            }
            if (!moving)
            {
                _walkVelocity = Vector2.Zero;
            }
            map.UpdatePlayer(_position, _facing, owner.MoveSpeed, moving, running, snap: false);
            if (moving != _reportedMoving || moving && Stopwatch.GetElapsedTime(previousReport, now) >= ReportInterval)
            {
                previousReport = now;
                if (moving)
                {
                    notice.Text = $"Posição · {_position.X:F0}, {_position.Z:F0}";
                }
                await ReportAsync(moving, running, ct);
            }
        }
    }

    private async Task ReportAsync(bool moving, bool running, CancellationToken ct)
    {
        await play.SendMovementAsync(_position, _facing, moving, moving && running, 0, FieldCodes.Ricarten, ct);
        _reportedMoving = moving;
    }
}
