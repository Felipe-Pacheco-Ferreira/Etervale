using System.Diagnostics;
using System.Numerics;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

// The automatic quest (docs/android.md, "Automatic quest"): from what the server says of the Adventurer Journey, it walks
// the player to the tribe's guide and talks to it, readies the weapon and the belt the guide asks for, travels to the
// hunting field, hunts the quest's monsters and picks up their drops until the crystal is in the bag, and returns. It
// runs on the game thread, once a frame, and only moves the player as a tap would; the server decides everything.
internal sealed class QuestNavigator(VulkanWorldSession session)
{
    private const float WaypointReach = 40f;
    // A route's point counts as passed this close: the player walks the legs the search checked and walked, never
    // cutting a corner into a planter or a lamp post.
    private const float PointReach = GameScene.RoutePointReach;
    // The guide is talked to from this close; the walk there follows a route, never a straight line.
    private const float TalkReach = 110f;
    private const float LootRadius = 260f;
    private const float StuckDistance = 8f;
    // A walk that has not moved this long searches again from where it stands (a crack in the floor finer than the
    // route's samples, a player in the way).
    private const double StuckSeconds = 1;
    private const double TalkCooldownSeconds = 6;
    // A guide that answered without moving the quest on is asked again only after this long.
    private const double RepeatTalkSeconds = 30;
    private const double InventoryCooldownSeconds = 2;
    private const int MostRouteFailures = 3;
    // A monster this close is fought first, whatever the quest hunts: it is the one hitting the player.
    private const float DefendRange = 140f;
    // Life fraction under which a belt potion is drunk, and the pause between two.
    private const float DrinkBelow = .45f;
    private const double DrinkCooldownSeconds = 3;
    // An attack that has not moved the player this long while its target is out of reach walks there along a route.
    private const double AttackStuckSeconds = 1.5;
    private const float AttackReach = 90f;

    private enum Step { Idle, Guide, Prepare, Hunt, Collect, Done }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _enabled;
    private int _replan;
    // A search that found no way from here is tried again only after this long.
    private const double NoRouteRetrySeconds = 2;
    private double _retryAt;
    private string _status = "";
    private Step _step;

    private IReadOnlyList<XnaVector2> _route = [];
    private int _routeIndex;
    private int _routeField;
    private Vector2 _routeGoal;
    private Task<GroundRoute> _routing;
    private int _routeFailures;
    private Vector3 _lastPosition;
    private double _drankAt = double.NegativeInfinity;
    private Task _drinking = Task.CompletedTask;
    private RouteMarks _marks = new(0, []);
    // A monster the attack could not reach in a straight line, walked to along a route instead.
    private ulong _chasing;
    private double _attackStillSince = double.NaN;
    private Vector3 _attackFrom;
    private double _lastProgressAt;

    private double _talkedAt = double.NegativeInfinity;
    private (QuestCode, int, long) _talkedAbout;
    private double _inventoryAt = double.NegativeInfinity;
    private Task _inventory = Task.CompletedTask;
    private int _huntPoint;
    private int? _crystalBaseline;
    private bool _collectAfterTalk;

    public bool Enabled => Volatile.Read(ref _enabled) != 0;

    // The route being walked, in field coordinates, for the minimap's dots; read on the UI thread.
    public RouteMarks Marks => Volatile.Read(ref _marks);

    // What the automatic quest is doing, for the HUD.
    public string Status => Volatile.Read(ref _status);

    // Called on the UI thread; the game thread drops the old route on its next update, so every start plans afresh from
    // where the player stands.
    public void SetEnabled(bool value)
    {
        Volatile.Write(ref _replan, 1);
        Volatile.Write(ref _marks, new RouteMarks(0, []));
        Volatile.Write(ref _enabled, value ? 1 : 0);
        if (!value)
        {
            session.SetAutoHunt(false);
            session.KeepHunting = false;
            Show("Missão automática pausada");
        }
    }

    // Whether there is a step to take: the journey is running or waiting to start.
    public bool HasWork(QuestView quest) => Plan(quest) is not (Step.Idle or Step.Done);

    // Answers a quest dialog by itself while it runs, so the conversation does not wait for a tap.
    public bool AnswersDialogs => Enabled;

    public void Update(GameScene scene, bool manualMovement)
    {
        if (!Enabled)
        {
            return;
        }

        if (manualMovement)
        {
            scene.StopAutoMove();
            SetEnabled(false);
            return;
        }

        if (Interlocked.Exchange(ref _replan, 0) != 0)
        {
            scene.StopAutoMove();
            ClearRoute();
            _routeGoal = default;
            _chasing = 0;
            _retryAt = 0;
        }

        if (scene.PlayerDead || scene.Warping || session.Quest is not { } quest || session.MapPose is not { } pose)
        {
            return;
        }

        DrinkIfHurt();

        var route = AdventurerJourneyRoute.For(session.Character.Class);
        var step = Plan(quest);
        if (step != _step)
        {
            _step = step;
            ClearRoute();
            _huntPoint = 0;
        }

        switch (step)
        {
            case Step.Done:
                session.KeepHunting = false;
                Show("Jornada do Aventureiro concluída");
                SetEnabled(false);
                return;
            case Step.Idle:
                Show("Nenhuma missão automática");
                return;
            case Step.Prepare when ReadyEquipment(quest):
                return;
            case Step.Prepare:
            case Step.Guide:
                session.SetAutoHunt(false);
                session.KeepHunting = false;
                GoToGuide(scene, pose, route, quest);
                return;
            default:
                Hunt(scene, pose, route, quest, step == Step.Collect);
                return;
        }
    }

    private Step Plan(QuestView quest)
    {
        if (quest is null)
        {
            return Step.Idle;
        }

        if (quest.Code != AdventurerJourneyRoute.Code)
        {
            var welcome = quest.Progress.FirstOrDefault()?.Description;
            return welcome is AdventurerJourneyRoute.MeetSkadiDescription or AdventurerJourneyRoute.MeetJinDescription ? Step.Guide
                : quest.Finished.Count > 0 || quest.Progress.Count > 0 ? Step.Done : Step.Idle;
        }

        switch (quest.State)
        {
            case 0:
                return Step.Guide;
            case 1:
                return Step.Prepare;
        }

        if (quest.Kills < quest.KillsNeeded)
        {
            _crystalBaseline ??= CrystalCount();
            return Step.Hunt;
        }

        // The guide takes the crystal only when one more is in the bag than when the hunt began; the baseline is the
        // count this session first saw during the hunt, or unknown after a restart, when the guide says whether it fits.
        if (_collectAfterTalk && _crystalBaseline is { } gathered && CrystalCount() > gathered)
        {
            _collectAfterTalk = false;
        }

        if (_collectAfterTalk || _crystalBaseline is { } baseline && CrystalCount() <= baseline)
        {
            return Step.Collect;
        }

        return Step.Guide;
    }

    private void GoToGuide(GameScene scene, VulkanWorldSession.WorldMapPose pose, TribeRoute route, QuestView quest)
    {
        if (pose.FieldId != route.Guide.FieldCode)
        {
            Travel(scene, pose, route, route.Guide.FieldCode, $"Voltando para {GuideName(route)}");
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;
        var about = (quest.Code, quest.State, quest.Revision);
        if (about == _talkedAbout && now - _talkedAt < TalkCooldownSeconds)
        {
            Show($"Falando com {GuideName(route)}");
            return;
        }

        if (about == _talkedAbout && quest.Code == AdventurerJourneyRoute.Code && quest.State == 2)
        {
            // The guide kept the hunt open: it wants a crystal gathered since the hunt began.
            _collectAfterTalk = true;
            _crystalBaseline = CrystalCount();
            return;
        }

        if (about == _talkedAbout && now - _talkedAt < RepeatTalkSeconds)
        {
            Show(quest.State == 1 ? "Equipe uma arma e ponha uma poção no cinto" : $"Fale com {GuideName(route)}");
            return;
        }

        // Walk to the guide along a route (its place in view, else the route data's), then talk from beside it. The talk
        // walks straight, so it starts only where that walk is clear: near the guide but above it on a terrace, or behind
        // a wall, the route goes on (down the stairs, around the wall).
        var known = session.TryFindNpc(route.GuideName, out var guide, out var at);
        var target = known ? new Vector2(at.X, at.Z) : new Vector2(route.Guide.X, route.Guide.Z);
        var distance = Vector2.Distance(new Vector2(pose.Position.X, pose.Position.Z), target);
        var beside = known && distance <= TalkReach
            && (distance <= WaypointReach || scene.WalksStraightTo(new XnaVector2(target.X, -target.Y)));
        if (!beside)
        {
            Walk(scene, pose, route.Guide.FieldCode, target, 0, $"Indo até {GuideName(route)}", 0f);
            return;
        }

        ClearRoute();
        if (!scene.AutoMoving && scene.ApproachNpc(guide))
        {
            _talkedAt = now;
            _talkedAbout = about;
        }

        Show($"Falando com {GuideName(route)}");
    }

    private void Hunt(GameScene scene, VulkanWorldSession.WorldMapPose pose, TribeRoute route, QuestView quest, bool collecting)
    {
        if (pose.FieldId != route.HuntField)
        {
            session.SetAutoHunt(false);
            Travel(scene, pose, route, route.HuntField, $"Indo caçar {quest.TargetName}");
            return;
        }

        session.KeepHunting = collecting;
        Show(collecting ? $"Procurando o Cristal de {quest.TargetName}" : $"Caçando {quest.TargetName}  {quest.Kills}/{quest.KillsNeeded}");
        // A monster at the player's side is fought before the quest's own, so the hunt is not worn down on the way.
        if (session.ClosestMonster(DefendRange, quest.TargetQuestCode) is { } attacker)
        {
            session.SetAutoHunt(false);
            ClearRoute();
            if (scene.AttackTargetId != attacker)
            {
                scene.SetQuestHuntTarget(attacker);
            }

            return;
        }

        if (_chasing != 0)
        {
            Chase(scene, pose, route);
            return;
        }

        session.SetAutoHunt(true);
        if (scene.AttackTargetId != 0)
        {
            WatchAttack(scene, pose);
            return;
        }

        _attackStillSince = double.NaN;
        if (scene.AutoMoving && _route.Count == 0)
        {
            return;
        }

        if (scene.PickUpNear(LootRadius) || session.HasHuntTarget(quest.TargetQuestCode))
        {
            ClearRoute();
            return;
        }

        var point = route.HuntPoints[_huntPoint % route.HuntPoints.Count];
        if (Walk(scene, pose, route.HuntField, new Vector2(point.X, point.Z), 0, Status))
        {
            _huntPoint++;
            ClearRoute();
        }
    }

    // The attack walks straight at its target; when that leaves the player standing against something, the target is
    // reached along a route and the attack starts again from beside it.
    private void WatchAttack(GameScene scene, VulkanWorldSession.WorldMapPose pose)
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (double.IsNaN(_attackStillSince) || Vector3.Distance(pose.Position, _attackFrom) > StuckDistance)
        {
            _attackStillSince = now;
            _attackFrom = pose.Position;
            return;
        }

        var target = scene.AttackTargetId;
        if (now - _attackStillSince > AttackStuckSeconds && scene.TryGetEntityPlanar(target, out var at)
            && XnaVector2.Distance(at, scene.PlayerPlanar) > AttackReach)
        {
            _chasing = target;
            _attackStillSince = double.NaN;
            session.SetAutoHunt(false);
            scene.SetQuestHuntTarget(0);
            ClearRoute();
        }
    }

    private void Chase(GameScene scene, VulkanWorldSession.WorldMapPose pose, TribeRoute route)
    {
        if (!scene.TryGetEntityPlanar(_chasing, out var at) || !scene.TrackQuestMonster(_chasing, out _))
        {
            _chasing = 0;
            ClearRoute();
            return;
        }

        if (Walk(scene, pose, route.HuntField, new Vector2(at.X, -at.Y), 0, Status, AttackReach))
        {
            ClearRoute();
            scene.SetQuestHuntTarget(_chasing);
            _chasing = 0;
        }
    }

    // Walks towards another field through the exits of the route; false while it is under way.
    private void Travel(GameScene scene, VulkanWorldSession.WorldMapPose pose, TribeRoute route, int destination, string status)
    {
        var exit = route.ExitTowards(pose.FieldId, destination);
        if (exit is null)
        {
            Show($"Vá para {(destination == route.Guide.FieldCode ? GuideName(route) : "a área de caça")} para continuar");
            return;
        }

        Walk(scene, pose, exit.From, new Vector2(exit.Point.X, exit.Point.Z), exit.Seam ? exit.To : 0, status);
    }

    // Follows a route to a point of a field (its own planar x, z); true once there.
    private bool Walk(GameScene scene, VulkanWorldSession.WorldMapPose pose, int field, Vector2 goal, int joinWith, string status,
        float reach = WaypointReach)
    {
        Show(status);
        var here = new Vector2(pose.Position.X, pose.Position.Z);
        if (Vector2.Distance(here, goal) <= reach)
        {
            return true;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (_routeField != field || _routeGoal != goal || (_route.Count == 0 && _routing is null && now >= _retryAt))
        {
            _routeField = field;
            _routeGoal = goal;
            _route = [];
            var renderGoal = new XnaVector2(goal.X, -goal.Y);
            _routing = scene.PlanRouteAsync(renderGoal, session.NpcPoints(renderGoal, TalkReach));
        }

        if (_routing is { IsCompleted: true } search)
        {
            var routing = search.IsCompletedSuccessfully ? search.Result : null;
            _routing = null;
            if (routing is not { Points.Count: > 0 })
            {
                // No walkable way from here: never walk blind at the goal, which runs into the walls between.
                _retryAt = now + NoRouteRetrySeconds;
                Show("Sem caminho daqui; tentando de novo");
                return false;
            }

            // A route that stops short of the goal leads as near as the loaded ground allows; the next search starts there.
            _route = routing.Points;
            _routeIndex = 0;
            _lastProgressAt = _clock.Elapsed.TotalSeconds;
            Volatile.Write(ref _marks, new RouteMarks(field, _route.Select(point => new Vector2(point.X, -point.Y)).ToArray()));
        }

        if (_route.Count == 0)
        {
            if (_routing is not null)
            {
                Show("Calculando a rota…");
            }

            return false;
        }

        var player = scene.PlayerPlanar;
        while (_routeIndex < _route.Count && XnaVector2.Distance(player, _route[_routeIndex]) <= PointReach)
        {
            _routeIndex++;
        }

        if (_routeIndex >= _route.Count)
        {
            // The end of a route that stopped short: search again from here.
            _route = [];
            return false;
        }

        scene.WalkTo(_route[_routeIndex], PointReach);
        if (Vector3.Distance(pose.Position, _lastPosition) > StuckDistance)
        {
            _lastPosition = pose.Position;
            _lastProgressAt = now;
        }
        else if (now - _lastProgressAt > StuckSeconds)
        {
            // Something the search could not see (a monster, another player) is in the way: search again from here.
            _routeFailures++;
            _lastProgressAt = now;
            _route = [];
            if (_routeFailures > MostRouteFailures)
            {
                _routeFailures = 0;
                _routeGoal = default;
            }
        }

        return false;
    }

    // Drinks the first belt potion when life runs low.
    private void DrinkIfHurt()
    {
        if (session.Owner is not { Life: > 0 } owner || owner.MaxLife <= 0 || owner.Life > owner.MaxLife * DrinkBelow
            || session.Play is not { } play || !_drinking.IsCompleted || _clock.Elapsed.TotalSeconds - _drankAt < DrinkCooldownSeconds)
        {
            return;
        }

        var potion = (owner.Items ?? []).FirstOrDefault(item => item.BeltBox >= 0 && item.Kind == ItemKind.Potion);
        if (potion is null)
        {
            return;
        }

        _drankAt = _clock.Elapsed.TotalSeconds;
        var command = new ItemUseCommand(Guid.NewGuid(), owner.InventoryRevision, potion.InstanceId);
        _drinking = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await play.UseItemAsync(command, timeout.Token);
        });
    }

    private void ClearRoute()
    {
        _route = [];
        _routing = null;
        _routeField = 0;
        _routeFailures = 0;
        Volatile.Write(ref _marks, new RouteMarks(0, []));
    }

    // Puts a weapon in the hand and a potion in the belt when the guide asks for them; true while it does.
    private bool ReadyEquipment(QuestView quest)
    {
        if (session.Owner is not { } owner || session.Play is not { } play)
        {
            return false;
        }

        if (!_inventory.IsCompleted || _clock.Elapsed.TotalSeconds - _inventoryAt < InventoryCooldownSeconds)
        {
            Show("Preparando o equipamento");
            return true;
        }

        var items = owner.Items ?? [];
        OwnedItemView move = null;
        var action = InventoryAction.Equip;
        var destination = 0;
        var slot = EquipmentSlot.Weapon;
        if (!items.Any(item => item.Equipped && ItemEquipmentMetadata.SlotOf(item.Kind) == EquipmentSlot.Weapon))
        {
            move = items.FirstOrDefault(item => item.InBag && ItemEquipmentMetadata.SlotOf(item.Kind) == EquipmentSlot.Weapon);
            if (move is not null)
            {
                slot = ItemEquipmentMetadata.DefaultSlot(move.Kind, EquipmentSlot.Weapon,
                    candidate => OwnedItemView.InSlot(items, (int)candidate) is not null) ?? EquipmentSlot.Weapon;
            }
        }

        if (move is null && !items.Any(item => item.BeltBox >= 0))
        {
            move = items.FirstOrDefault(item => item.InBag && item.Kind == ItemKind.Potion);
            action = InventoryAction.BeltPut;
        }

        if (move is null)
        {
            return false;
        }

        _inventoryAt = _clock.Elapsed.TotalSeconds;
        Show(action == InventoryAction.Equip ? $"Equipando {move.Name}" : $"Colocando {move.Name} no cinto");
        var command = new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, action, move.InstanceId, destination, 0, slot);
        _inventory = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await play.ApplyInventoryAsync(command, timeout.Token);
        });
        return true;
    }

    private int CrystalCount() =>
        session.Owner?.Items?.Where(item => item.Code == AdventurerJourneyRoute.Crystal).Sum(item => item.Count) ?? 0;

    private static string GuideName(TribeRoute route) => route.GuideName.Replace("Adventurer ", "", StringComparison.Ordinal);

    private void Show(string status) => Volatile.Write(ref _status, status);
}

// A route's points in a field's own planar coordinates.
internal sealed record RouteMarks(int FieldCode, IReadOnlyList<Vector2> Points);
