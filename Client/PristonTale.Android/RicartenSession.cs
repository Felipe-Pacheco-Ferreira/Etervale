using System.Numerics;
using System.Diagnostics;
using Android.Widget;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using PristonTale.Simulation.Quests;

namespace PristonTale.Mobile;

internal sealed record FieldSessionResult(string Message, PresenceUpdate? Update = null);

internal sealed class RicartenSession(IPlayChannel play, FieldTerrain terrain, RicartenSceneView map, MovementJoystick joystick, TextView notice, Vector3 spawn,
    int fieldId, string fieldName, Action playArrivalSound, MobileQuestJournal journal, Button attack, MobileWorldHud hud, MobileQuestDialog questDialog, MobileWorldChat chat, MobileMinimap? minimap, MobileShortcuts shortcuts, Button autoHunt, Button portalGuide)
{
    private static readonly WorldCatalog Fields = new();
    private FieldWarpGate? _pendingGate;
    private float _gateDelay;
    private bool _awaitingGate;
    private float _gateReplySeconds;
    private static readonly TimeSpan LocalStepInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(200);
    private Vector3 _position = spawn;
    private Vector2 _stick;
    private Vector2 _walkVelocity;
    private bool _reportedMoving;
    private OwnerState? _owner;
    private float _facing;
    private bool _arrived;
    private bool _attackPending;
    private ulong _attackSequence;
    private readonly Dictionary<ulong, (Vector3 Position, string Name, int Life, int MaxLife, int QuestCode)> _monsters = new();
    private ulong _target;
    private Vector3[] _route = [];
    private int _routeStep;
    private int _routeRequest;
    private QuestView? _quest;
    private bool _autoHunting;
    private long _nextAutoAction;

    public async Task<FieldSessionResult> RunAsync(PresenceUpdate? initial, CancellationToken ct)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        joystick.DirectionChanged += SetDirection;
        attack.Click += AttackNearest;
        autoHunt.Click += ToggleAutoHunt;
        portalGuide.Click += GuideToPortal;
        if (minimap is not null)
        {
            minimap.DestinationSelected += GuideTo;
        }
        var receive = ReceiveAsync(initial, session.Token);
        var movement = MoveAsync(session.Token);
        try
        {
            var completed = await Task.WhenAny(receive, movement);
            await completed;
            return new FieldSessionResult($"A conexão com {fieldName} terminou.", completed == receive ? await receive : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new FieldSessionResult($"Você saiu de {fieldName}.");
        }
        finally
        {
            attack.Click -= AttackNearest;
            autoHunt.Click -= ToggleAutoHunt;
            portalGuide.Click -= GuideToPortal;
            if (minimap is not null)
            {
                minimap.DestinationSelected -= GuideTo;
            }
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

        void ToggleAutoHunt(object? sender, EventArgs args)
        {
            SetAutoHunt(!_autoHunting);
        }

        async void GuideToPortal(object? sender, EventArgs args)
        {
            if (_owner is not { Life: > 0 } owner || !Fields.TryGetFieldByCode(fieldId, out var field))
            {
                return;
            }

            var gate = field.WarpGates.FirstOrDefault(candidate => WalkingDestination(candidate, owner.Level) is not null);
            if (gate is null)
            {
                notice.Text = "Portal indisponível para este personagem.";
                return;
            }

            SetAutoHunt(false);
            await GuideToAsync(new Vector2(gate.X, gate.Z), session.Token, gate.Y);
        }

        async void GuideTo(Vector2 goal)
        {
            SetAutoHunt(false);
            await GuideToAsync(goal, session.Token);
        }

        async void AttackNearest(object? sender, EventArgs args)
        {
            if (_owner is not { Life: > 0 } || _attackPending)
            {
                return;
            }

            var nearest = 0UL;
            var distance = float.PositiveInfinity;
            foreach (var (id, monster) in _monsters)
            {
                var next = Vector3.DistanceSquared(_position, monster.Position);
                if (next < distance || next == distance && id < nearest)
                {
                    distance = next;
                    nearest = id;
                }
            }

            if (nearest == 0)
            {
                notice.Text = "Nenhum monstro próximo.";
                return;
            }

            _attackPending = true;
            try
            {
                var monster = _monsters[nearest];
                _target = nearest;
                shortcuts.SetTarget(nearest);
                hud.SetTarget(monster.Name, monster.Life, monster.MaxLife);
                await play.SendAttackAsync(nearest, sustained: false, ct: session.Token, sequence: ++_attackSequence);
                notice.Text = "";
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
            {
                notice.Text = "Não foi possível atacar.";
            }
            finally
            {
                _attackPending = false;
            }
        }
    }

    private void SetDirection(Vector2 input)
    {
        _stick = input;
        if (input != Vector2.Zero)
        {
            SetAutoHunt(false);
            ClearRoute();
        }
    }

    private void ClearRoute()
    {
        _routeRequest++;
        _route = [];
        _routeStep = 0;
        map.SetRoute([]);
    }

    private Vector2 MoveDirection()
    {
        if (_routeStep >= _route.Length)
        {
            return map.MovementDirection(_stick);
        }

        while (_routeStep < _route.Length)
        {
            var point = _route[_routeStep];
            var offset = new Vector2(point.X - _position.X, point.Z - _position.Z);
            if (offset.LengthSquared() > 16f)
            {
                return Vector2.Normalize(offset);
            }
            _routeStep++;
            map.SetRoute(_route[_routeStep..]);
        }
        ClearRoute();
        return Vector2.Zero;
    }

    private async Task<PresenceUpdate?> ReceiveAsync(PresenceUpdate? initial, CancellationToken ct)
    {
        if (initial is not null)
        {
            ApplyPresence(initial, ct);
        }

        await foreach (var update in play.ReadAllAsync(ct))
        {
            if (update.Warp is { } warp && warp.FieldId != fieldId)
            {
                return update;
            }

            try
            {
                ApplyPresence(update, ct);
            }
            finally
            {
                play.Recycle(update);
            }
        }

        return null;
    }

    private void ApplyPresence(PresenceUpdate update, CancellationToken ct)
    {
        chat.Observe(update.Chat);
        if (update.Warp is { } warp)
        {
            _pendingGate = null;
            _awaitingGate = false;
            ClearRoute();
            _position = warp.Position;
            minimap?.UpdatePosition(_position, _facing);
            _walkVelocity = Vector2.Zero;
            map.UpdatePlayer(_position, _facing, _owner?.MoveSpeed ?? 1, moving: false, running: false, snap: true);
            notice.Text = "Posição corrigida pelo servidor.";
        }
        if (update.Quest is { } quest)
        {
            if (_quest?.Code != quest.Code || _quest?.State != quest.State || quest.TargetQuestCode <= 0 || quest.Kills >= quest.KillsNeeded)
            {
                SetAutoHunt(false);
            }
            _quest = quest;
            journal.Update(quest);
            hud.UpdateQuest(quest);
        }

        if (update.QuestPrompt is { } prompt)
        {
            questDialog.ShowWorldPrompt(prompt, ct);
        }

        if (update.Owner is { } owner)
        {
            if (_owner is null)
            {
                notice.Text = "Joystick para andar. Arraste à direita para mover a câmera.";
            }
            _owner = owner;
            if (owner.Life <= 0)
            {
                SetAutoHunt(false);
            }
            hud.UpdateOwner(owner);
            shortcuts.Update(owner);
        }
        foreach (var spawn in update.Spawns)
        {
            if (spawn.Kind == EntityKind.Monster && spawn.Life > 0 && spawn.OwnerId == 0)
            {
                _monsters[spawn.EntityId] = (spawn.Position, spawn.Name, spawn.Life, spawn.MaxLife, spawn.QuestCode);
            }
            if (spawn.IsSelf && !_arrived)
            {
                _arrived = true;
                map.ShowArrival(_position);
                playArrivalSound();
            }
            else if (!spawn.IsSelf)
            {
                map.SetEntity(spawn.EntityId, spawn.Position, spawn.Kind, spawn.TemplateId, spawn.Facing, spawn.ModelScale, spawn.MoveState, spawn.Player);
            }
        }
        foreach (var state in update.States)
        {
            if (state.Life <= 0)
            {
                _monsters.Remove(state.EntityId);
            }
            else if (_monsters.TryGetValue(state.EntityId, out var monster))
            {
                _monsters[state.EntityId] = (state.Position, monster.Name, state.Life, monster.MaxLife, monster.QuestCode);
                if (state.EntityId == _target)
                {
                    hud.SetTarget(monster.Name, state.Life, monster.MaxLife);
                }
            }

            map.MoveEntity(state.EntityId, state.Position, state.Facing, state.MoveState);
        }
        foreach (var removed in update.Removed)
        {
            _monsters.Remove(removed);
            map.RemoveEntity(removed);
        }
        if (_target != 0 && !_monsters.ContainsKey(_target))
        {
            _target = 0;
            hud.ClearTarget();
            shortcuts.SetTarget(0);
        }
        map.PublishEntities();
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

            if (await UpdatePortalAsync(owner.Level, seconds, ct))
            {
                continue;
            }

            await UpdateAutoHuntAsync(owner, now, ct);
            var running = !owner.Overweight;
            var speed = running
                ? Locomotion.RunUnitsPerSecond(owner.MoveSpeed)
                : Locomotion.WalkUnitsPerSecond(owner.MoveSpeed, inVillage: fieldId is FieldCodes.Ricarten or FieldCodes.Pillai);
            var direction = MoveDirection();
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
                if (_route.Length > 0 && direction != Vector2.Zero)
                {
                    ClearRoute();
                }
                _walkVelocity = Vector2.Zero;
            }
            map.UpdatePlayer(_position, _facing, owner.MoveSpeed, moving, running, snap: false);
            minimap?.UpdatePosition(_position, _facing);
            if (moving != _reportedMoving || moving && Stopwatch.GetElapsedTime(previousReport, now) >= ReportInterval)
            {
                previousReport = now;
                await ReportAsync(moving, running, ct);
            }
        }
    }

    private void SetAutoHunt(bool enabled)
    {
        if (enabled && (_quest is not { TargetQuestCode: > 0 } quest || quest.Kills >= quest.KillsNeeded))
        {
            notice.Text = "Nenhuma caçada compatível em andamento.";
            return;
        }
        if (_autoHunting != enabled)
        {
            ClearRoute();
        }
        _autoHunting = enabled;
        _nextAutoAction = 0;
        autoHunt.Text = enabled ? "Parar caça" : "Auto-caça";
    }

    private async Task GuideToAsync(Vector2 goal, CancellationToken ct, float? elevation = null)
    {
        if (_owner is not { Life: > 0 })
        {
            return;
        }

        ClearRoute();
        var revision = _routeRequest;
        var origin = _position;
        try
        {
            var route = await Task.Run(() =>
            {
                if (!terrain.TryPlaceOnFloor(new Vector3(goal.X, 0f, goal.Y), out var target))
                {
                    return [];
                }
                if (elevation is { } height && height != 0f)
                {
                    target = terrain.SettleNear(target with { Y = height }, FieldTerrain.ArrivalTolerance);
                }
                return FieldRoute.Find(terrain, origin, target, ct).ToArray();
            }, ct);
            if (revision != _routeRequest || ct.IsCancellationRequested)
            {
                return;
            }

            _route = route;
            map.SetRoute(route);
            if (route.Length == 0)
            {
                notice.Text = "Nenhum caminho encontrado.";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task UpdateAutoHuntAsync(OwnerState owner, long now, CancellationToken ct)
    {
        if (!_autoHunting || _quest is not { TargetQuestCode: > 0 } quest || now < _nextAutoAction || _attackPending)
        {
            return;
        }

        _nextAutoAction = now + Stopwatch.Frequency;
        var nearest = 0UL;
        var distance = float.PositiveInfinity;
        foreach (var (id, monster) in _monsters)
        {
            if (monster.QuestCode != quest.TargetQuestCode)
            {
                continue;
            }
            var next = Vector3.DistanceSquared(_position, monster.Position);
            if (next < distance)
            {
                nearest = id;
                distance = next;
            }
        }
        if (nearest == 0)
        {
            notice.Text = "Nenhum alvo da missão à vista.";
            return;
        }

        var target = _monsters[nearest];
        if (distance > owner.AttackRange * owner.AttackRange)
        {
            if (_route.Length == 0)
            {
                await GuideToAsync(new Vector2(target.Position.X, target.Position.Z), ct);
            }
            return;
        }

        ClearRoute();
        _target = nearest;
        shortcuts.SetTarget(nearest);
        hud.SetTarget(target.Name, target.Life, target.MaxLife);
        await play.SendAttackAsync(nearest, sustained: false, ct: ct, sequence: ++_attackSequence);
    }

    private async Task<bool> UpdatePortalAsync(int level, float seconds, CancellationToken ct)
    {
        if (_awaitingGate)
        {
            _gateReplySeconds -= seconds;
            if (_gateReplySeconds <= 0f)
            {
                _awaitingGate = false;
                _pendingGate = null;
            }
            return true;
        }

        if (_pendingGate is null)
        {
            if (!Fields.TryGetFieldByCode(fieldId, out var field))
            {
                return false;
            }

            foreach (var gate in field.WarpGates)
            {
                if (WalkingDestination(gate, level) is null
                    || gate.Y != 0 && MathF.Abs(_position.Y - gate.Y) >= gate.Height
                    || Vector2.DistanceSquared(new Vector2(_position.X, _position.Z), new Vector2(gate.X, gate.Z)) > gate.Radius * gate.Radius)
                {
                    continue;
                }

                _pendingGate = gate;
                _gateDelay = gate.SpecialEffect == WingGates.DelayedEffect ? WingGates.DelayedWarpSeconds : 0f;
                await ReportAsync(false, false, ct);
                map.UpdatePlayer(_position, _facing, _owner?.MoveSpeed ?? 1, moving: false, running: false, snap: false);
                break;
            }
        }

        if (_pendingGate is not { } pending)
        {
            return false;
        }

        _gateDelay -= seconds;
        if (_gateDelay <= 0f)
        {
            var exit = pending.Exit(Random.Shared.Next(pending.ExitCount));
            _awaitingGate = true;
            _gateReplySeconds = 5f;
            await play.SendMovementAsync(new Vector3(exit.X, exit.Y, exit.Z), _facing,
                moving: false, running: false, actionTarget: 0, Fields.GetField(pending.DestinationFieldId).FieldCode, ct);
        }

        return true;
    }

    private FieldDef? WalkingDestination(FieldWarpGate gate, int level)
    {
        if (WingGates.IsStone(gate) || level < gate.LimitLevel
            || !Fields.TryGetField(gate.DestinationFieldId, out var destination) || destination.FieldCode == fieldId)
        {
            return null;
        }

        return destination.FieldCode is FieldCodes.Pillai or FieldCodes.ForeverFall1 ? destination : null;
    }

    private async Task ReportAsync(bool moving, bool running, CancellationToken ct)
    {
        await play.SendMovementAsync(_position, _facing, moving, moving && running, 0, fieldId, ct);
        _reportedMoving = moving;
    }
}
