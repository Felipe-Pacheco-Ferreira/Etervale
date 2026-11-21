using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.World;
using PristonTale.Engine3D;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.Screens.Gameplay;
public sealed partial class GameplayScreen
{
    private readonly HashSet<ulong> _questNpcs = [];
    private RoutePoint? _questDestination;
    private Task<GroundRoute> _questRouteTask;
    private IReadOnlyList<Vector2> _questRoutePoints = [];
    private int _questRouteIndex;
    private int _questRouteField;
    private Vector2 _questLastProgress;
    private float _questProgressClock;
    private int _questStalledChecks;
    private int _questPlannedStretches;
    private void ObserveQuestNavigation(PresenceUpdate update)
    {
        foreach (var entity in update.Spawns)
        {
            if (entity.Kind == EntityKind.Npc)
            {
                _questNpcs.Add(entity.EntityId);
            }
        }

        foreach (var entity in update.Removed)
        {
            _questNpcs.Remove(entity);
        }
    }

    private void NavigateToQuest(RoutePoint destination)
    {
        if (!WebWorldHud.Enabled)
        {
            return;
        }

        if (_scene.ActiveFieldDefinition?.FieldCode != destination.FieldCode)
        {
            var catalog = new WorldCatalog();
            var name = catalog.TryGetFieldByCode(destination.FieldCode, out var field) ? field.Name : destination.FieldCode.ToString();
            QuestRouteRefused(ClientText.Format("QuestRoute.EnterField", name));
            return;
        }

        if (_questRouteTask is { IsCompleted: false })
        {
            _hud.QuestNavigationNotice(ClientText.Get("QuestRoute.WaitCalculation"));
            return;
        }

        CancelQuestNavigation();
        _questDestination = destination;
        _questRouteField = destination.FieldCode;
        _questLastProgress = _scene.PlayerPlanar;
        _questProgressClock = 0;
        _questStalledChecks = 0;
        _questPlannedStretches = 0;
        PlanQuestRoute();
    }

    private void PlanQuestRoute()
    {
        if (_questDestination is not { } destination)
        {
            return;
        }

        if (++_questPlannedStretches > 16)
        {
            QuestRouteRefused(ClientText.Get("QuestRoute.Unreachable"));
            return;
        }

        _questRouteTask = _scene.PlanRouteAsync(WorldSpace.Planar(destination.X, destination.Z), mostCells: GroundRoute.Stretch);
        _questRoutePoints = [];
        _questRouteIndex = 0;
        _hud.QuestNavigationNotice(ClientText.Get("QuestRoute.Calculating"));
    }

    private void UpdateQuestNavigation(GameTime gameTime)
    {
        if (_questDestination is null)
        {
            if (_questRouteTask?.IsCompleted == true)
            {
                _questRouteTask = null;
            }

            return;
        }

        var mouse = Mouse.GetState();
        var manualInput = !_hud.PointerCaptured && (mouse.LeftButton == ButtonState.Pressed || mouse.RightButton == ButtonState.Pressed);
        if (manualInput || _scene.PlayerDead || !Context.IsWindowActive || Context.Presentation.AwaitingConfirmation || _scene.ActiveFieldDefinition?.FieldCode != _questRouteField)
        {
            CancelQuestNavigation();
            _hud.CancelQuestGuide();
            return;
        }

        if (_questRouteTask is { IsCompleted: false })
        {
            return;
        }

        if (TryApproachQuestGuide())
        {
            return;
        }

        if (_questRouteTask is { } completed)
        {
            _questRouteTask = null;
            if (!completed.IsCompletedSuccessfully || completed.Result is not { } route || route.Points.Count == 0)
            {
                _ = completed.Exception;
                if (TryApproachQuestGuide())
                {
                    return;
                }

                QuestRouteRefused(ClientText.Get("QuestRoute.NoSafeRoute"));
                return;
            }

            if (!route.ReachesGoal && Vector2.DistanceSquared(_scene.PlayerPlanar, new Vector2(route.End.X, route.End.Z)) < 64f)
            {
                QuestRouteRefused(ClientText.Get("QuestRoute.Unreachable"));
                return;
            }

            _questRoutePoints = route.Points;
            _questRouteIndex = 0;
            _hud.QuestNavigationNotice(ClientText.Get("QuestRoute.Following"));
        }

        while (_questRouteIndex < _questRoutePoints.Count && Vector2.DistanceSquared(_scene.PlayerPlanar, _questRoutePoints[_questRouteIndex]) <= 64f)
        {
            _questRouteIndex++;
        }

        if (_questRouteIndex >= _questRoutePoints.Count)
        {
            if (TryApproachQuestGuide())
            {
                return;
            }

            var destination = _questDestination.Value;
            if (Vector2.DistanceSquared(_scene.PlayerPlanar, WorldSpace.Planar(destination.X, destination.Z)) <= 55f * 55f)
            {
                CancelQuestNavigation();
                _hud.QuestNavigationNotice(ClientText.Get("QuestRoute.Arrived"));
                return;
            }

            PlanQuestRoute();
            return;
        }

        if (!_scene.WalkTo(_questRoutePoints[_questRouteIndex], arriveWithin: 6f))
        {
            QuestRouteRefused(ClientText.Get("QuestRoute.CannotWalk"));
            return;
        }

        _questProgressClock += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_questProgressClock >= 1f)
        {
            _questProgressClock = 0;
            _questStalledChecks = Vector2.DistanceSquared(_questLastProgress, _scene.PlayerPlanar) < 1f ? _questStalledChecks + 1 : 0;
            _questLastProgress = _scene.PlayerPlanar;
            if (_questStalledChecks >= 3)
            {
                QuestRouteRefused(ClientText.Get("QuestRoute.Obstacle"));
            }
        }
    }

    private bool TryApproachQuestGuide()
    {
        if (_questDestination is not { } destination)
        {
            return false;
        }

        var guide = AdventurerJourneyRoute.For(_entered.Class).Guide;
        if (destination != guide || Vector2.DistanceSquared(_scene.PlayerPlanar, WorldSpace.Planar(guide.X, guide.Z)) > 95f * 95f)
        {
            return false;
        }

        foreach (var entity in _questNpcs)
        {
            if (_scene.TryGetEntityPlanar(entity, out var point) && Vector2.DistanceSquared(point, WorldSpace.Planar(guide.X, guide.Z)) < 35f * 35f && _scene.WalksStraightTo(point))
            {
                if (!_scene.ApproachNpc(entity))
                {
                    return false;
                }

                CancelQuestNavigation(stopMovement: false);
                _hud.QuestNavigationNotice(ClientText.Get("QuestRoute.ApproachingGuide"));
                return true;
            }
        }

        return false;
    }

    private void QuestRouteRefused(string message)
    {
        CancelQuestNavigation();
        _hud.CancelQuestGuide();
        _hud.QuestNavigationNotice(message);
    }

    private void CancelQuestNavigation() => CancelQuestNavigation(stopMovement: true);

    private void CancelQuestNavigation(bool stopMovement)
    {
        _questDestination = null;
        _questRoutePoints = [];
        _questRouteIndex = 0;
        if (stopMovement)
        {
            _scene?.StopAutoMove();
        }
        _hud?.ClearQuestNavigationNotice();
        if (_questRouteTask is { } task)
        {
            _ = task.ContinueWith(static failed =>
            {
                _ = failed.Exception;
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
