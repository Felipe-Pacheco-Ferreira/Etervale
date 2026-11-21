using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Contracts.Motion;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.Screens.Gameplay;

public sealed partial class GameplayScreen : GameScreen
{
    private readonly EnteredCharacter _entered;
    private GameScene _scene;
    private GameplayHud _hud;
    private readonly List<GroundLabel> _groundLabels = [];
    private KeyboardState _lastKeyboard;
    private bool _worldLoadUpdated;
    private bool _showRenderStats = true;
    private volatile PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog _pendingHudCatalog;

    public GameplayScreen(ScreenContext context, EnteredCharacter entered) : base(context)
    {
        _entered = entered;
    }

    public override void Enter()
    {
        WebWorldLoading.InitializingScene();
        _scene = new GameScene(Context.GraphicsDevice, Context.Content, Context.Log, Context.Sky, Context.Effects, Context.Presentation, Context.Audio)
        {
            WeatherDiagnostic = Context.WeatherDiagnostic
        };
        _scene.LoadEntered(Context.AssetRoot, _entered);
        _hud = new GameplayHud(
            Context,
            _scene.ApplyInventoryAsync,
            _scene.ApplyProgressionAsync,
            _scene.InteractNpcAsync,
            _scene.PlayNpcInteractionAudio,
            _scene.StopNpcTalk,
            _scene.RequestNpcServiceAsync,
            _scene.PrepareField,
            _scene.RequestQuestAsync);
        _hud.CharacterName = _scene.Player.Name;
        _hud.CharacterClass = _scene.Player.Class.Class;
        _hud.PortraitScene = _scene;
        _scene.IsPartyMember = _hud.InParty;
        _scene.PresenceObserved += _hud.Combat.Observe;
        _scene.PresenceObserved += _hud.StringEffect.Observe;
        _scene.PresenceObserved += _hud.ObserveQuests;
        _scene.PresenceObserved += ObserveHuntTargets;
        _hud.QuestFanfare += _scene.PlayQuestFanfare;
        _hud.QuestDestinationRequested += NavigateToQuest;
        _hud.QuestNavigationCanceled += CancelQuestNavigation;
        _scene.PresenceObserved += ObserveQuestNavigation;
        _hud.SiegeEnded += _scene.PlaySiegeEnd;
        _hud.SiegeClosed += _scene.RestoreFieldMusic;
        _hud.CraftResult += _scene.PlayCraftResult;
        _scene.NpcSelected += _hud.OpenNpc;
        _scene.ShopSellerSelected += _hud.ViewPersonalShop;
        _scene.PickupToCursor = _hud.ExpectPickup;
        _scene.PickupToCursorAnswered += _hud.PickupAnswered;
        _hud.PersonalShops = _scene;
        _scene.PresenceObserved += _hud.ObserveParty;
        _scene.PresenceObserved += _hud.ObserveClan;
        _scene.PresenceObserved += _hud.ObserveCastle;
        _scene.PresenceObserved += _hud.ObserveBellatra;
        _hud.Parties = _scene;
        _hud.SendClan = _scene.SendClan;
        _hud.SendCastleOwner = _scene.SendCastleOwner;
        _hud.SendBellatraClan = _scene.SendBellatraClan;
        _hud.SendBellatraResult = _scene.SendBellatraResult;
        _hud.BellatraStage = _scene;
        _hud.SendClanMark = _scene.SendClanMark;
        _scene.PresenceObserved += _hud.ObserveTrade;
        _scene.PresenceObserved += _hud.ObserveDuel;
        _scene.PresenceObserved += _hud.ObserveArena;
        _scene.PresenceObserved += _hud.ObserveRoyale;
        _scene.PresenceObserved += _hud.ObserveTitles;
        _hud.Trades = _scene;
        _hud.Duels = _scene;
        _scene.PresenceObserved += _hud.ObserveContacts;
        _hud.Contacts = _scene;
        _hud.PlayerButtonsUsed += _scene.DismissPlayerButtons;
        _scene.SkillNotice += _hud.Chat.System;
        _hud.SummonModeRequested += _scene.ToggleSummonMode;
        _hud.CrystalPetDismissRequested += _scene.DismissCrystalPets;
        _hud.RunToggleRequested += _scene.ToggleRun;
        _hud.PvpToggleRequested += _scene.TogglePvp;
        _hud.TitleChosen += _scene.ChooseTitle;
        _hud.YahooKeyPressed += _scene.PressYahooKey;
        _hud.ItemSound += _scene.PlayItemSound;
        _hud.SummonAutomatic = _scene.SummonAutomatic;
        _hud.UseItem = _scene.UseItemAsync;
        _hud.Chat.PlayerName = _scene.Player.Name;
        _hud.Chat.Send = _scene.SendChatAsync;
        _hud.SendRewards = _scene.SendRewardsAsync;
        _hud.Chat.VoiceFrame = line => _voice.Receive(line, Context.Presentation.Requested);
        _hud.Overhead = _scene.Overhead;
        _hud.GroundLabels = _groundLabels;
        _hud.GroundLabelPlaced = _scene.PlaceGroundLabel;
        _scene.PresenceObserved += _hud.ObserveChat;
        _scene.PresenceObserved += _hud.ObserveGuide;
        _hud.Chat.WhisperArrived += _scene.PlayWhisper;
        _hud.Chat.CloseRequested += RequestExit;
        _hud.ExitRequested += RequestExit;
        _hud.LobbyRequested += ReturnToLobby;
        _hud.Restart.Restart = _scene.RestartAsync;
        _hud.Restart.ExitRequested += RequestExit;
        _hud.WingGate.Travel = _scene.TravelWingGateAsync;
        _hud.WingGate.RequiredLevel = _scene.FieldLimitLevel;
        _scene.WingGateEntered += _hud.WingGate.Open;
        _scene.WingGateLeft += _hud.WingGate.Close;

        _scene.BeginRemotePresence(Context.Backend.OpenPlayChannel(), _entered);

        _ = FetchWorldCatalogAsync();
        AttachTouch();
        WebWorldHud.Attach(_hud);
        WebWorldLoading.ScenePrepared();
    }

    private async Task FetchWorldCatalogAsync()
    {
        try
        {
            var catalog = await Context.Backend.GetWorldCatalogAsync();
            _scene?.SetWorldRenderCatalog(catalog);
            _pendingHudCatalog = catalog;
        }
        catch (Exception ex)
        {
            Context.Log.Error("failed to fetch world render catalog (monsters will not render)", ex);
        }
    }

    public override string StatusText
    {
        get
        {
            if (_scene == null)
                return "Loading…";
            var shadows = _scene.ShadowsEnabled ? "on" : "OFF";
            if (!_showRenderStats)
                return $"Shadows {shadows} | {_scene.Status}";

            var stats = _scene.LastRenderStats;
            var poses = _scene.LastAnimationCounts;
            var hour = _scene.TimeOfDay;
            var clock = $"{(int)hour:00}:{(int)((hour - (int)hour) * 60):00}";
            return $"Shadows {shadows} | {clock} | Pos {_scene.PlayerPosition.X:F0} {-_scene.PlayerPosition.Z:F0} | Lights {_scene.StageLightCount} | " +
                   $"Draw {stats.DrawCalls} | Pose {poses.Posed}/{poses.Posed + poses.Reused} | " +
                   $"Shadow S{(stats.SunShadowUpdated ? 1 : 0)}/L{stats.LampShadowMapsUpdated}/D{stats.ShadowDrawCalls} | " +
                   $"Vis {stats.VisibleBatches}/{stats.TotalBatches} | Tri {stats.Triangles} | State {stats.StateChanges} | " +
                   $"View {_scene.RenderDebugView}";
        }
    }

    private readonly Dictionary<ulong, int> _huntMonsters = [];
    private ulong _autoHuntTarget;

    private void ObserveHuntTargets(PristonTale.Client.Net.Presence.PresenceUpdate update)
    {
        foreach (var spawn in update.Spawns)
        {
            if (spawn.Kind == PristonTale.Contracts.World.EntityKind.Monster && spawn.OwnerId == 0)
            {
                _huntMonsters[spawn.EntityId] = spawn.QuestCode;
            }
        }

        foreach (var entity in update.Removed)
        {
            _huntMonsters.Remove(entity);
        }
    }

    private void UpdateAutoHunt()
    {
        var code = _hud.AutoHuntQuestCode;
        if (_hud.InTown || code == 0 || _scene.PlayerDead || !Context.IsWindowActive ||
            (!_hud.PointerCaptured && Mouse.GetState().LeftButton == ButtonState.Pressed) || TouchJoystickMoving)
        {
            _hud.AutoHuntEnabled = false;
        }

        var target = 0UL;
        if (_hud.AutoHuntEnabled)
        {
            var nearest = float.PositiveInfinity;
            foreach (var (entity, questCode) in _huntMonsters)
            {
                if (questCode == code && _scene.TrackQuestMonster(entity, out var distance) && distance < nearest)
                {
                    nearest = distance;
                    target = entity;
                }
            }
        }

        if (target != _autoHuntTarget)
        {
            if (target != 0 || _scene.AttackTargetId == _autoHuntTarget)
            {
                _scene.SetQuestHuntTarget(target);
            }
            _autoHuntTarget = target;
        }
    }

    private readonly PristonTale.Gameplay.Audio.DesktopVoice _voice = new();

    public override void Update(GameTime gameTime)
    {
        var catalog = _pendingHudCatalog;
        if (catalog is not null)
        {
            _pendingHudCatalog = null;
            _hud.Combat.SetCatalog(catalog);
        }

        var field = _scene.ActiveFieldDefinition;
        _ = _scene.TryGetNeighbourMap(out var neighbour, out var neighbourMap);
        var mapHidden = field is not null && HudMinimap.HiddenOn(field.FieldCode, field.State);
        _hud.World = new HudWorldView(field?.Id, field?.Underground == true, _scene.ActiveMapRectangle, _scene.PlayerPosition,
            _scene.PlayerForward, _scene.TimeOfDay, _scene.RunFlag, mapHidden, neighbour, neighbourMap, field?.FieldCode ?? 0);
        _hud.PremiumTimers = _scene.PremiumTimers;
        _hud.InCastleField = field?.State == Simulation.World.FieldState.Castle;
        _scene.OwnClanId = _hud.Combat.OwnClan;
        _scene.CastleLeague = _hud.Castle.League;
        _scene.DuelArena = _hud.Duel?.Center;
        _scene.DuelOpponent = _hud.Duel?.OpponentId ?? 0;
        _scene.CastleOwnerClanId = _hud.Castle.OwnerClanId;
        _hud.InTown = field?.State == Simulation.World.FieldState.Village;
        _hud.ShopSigns = _scene.ShopSigns;
        _hud.PlayerDead = _scene.PlayerDead;
        _hud.PlayerActing = _scene.PlayerActing;
        _hud.PlayerActionStarts = _scene.PlayerActionStarts;
        _hud.RestartOffer = _scene.RestartOffer;
        _hud.ChatTranslationAvailable = _scene.ChatTranslationAvailable;
        _scene.UpdateChatTranslation(Context.Presentation.Requested.ChatTranslationTarget);
        _hud.Update(_scene.Owner);
        _scene.PointerCaptured = _hud.PointerCaptured;
        UpdateTouch();
        _scene.MovementLocked = _hud.HoldsPlayer;
        _scene.KeyboardCaptured = _hud.KeyboardCaptured;
        _hud.Chat.SelfId = _scene.SelfEntityId;
        _scene.CameraInputEnabled = Context.IsWindowActive && !Context.Presentation.AwaitingConfirmation;
        _scene.RightAttackEnabled = _hud.RightAttackEnabled;
        _scene.LeftSkill = _hud.Bindings.LeftSkillId;
        _scene.RightSkill = _hud.Bindings.RightSkillId;
        _hud.Statuses = _scene.Skills?.Statuses;
        _hud.Cooldowns = _scene.Skills?.Cooldowns;
        _hud.SkillClockMs = gameTime.TotalGameTime.TotalMilliseconds;
        _hud.SelfId = _scene.SelfEntityId;
        if (!Context.Presentation.AwaitingConfirmation && !_hud.KeyboardCaptured)
        {
            UpdateMotionKeys();
            UpdateDebugKeys();
        }
        var settings = Context.Presentation.Requested;
        _voice.Update(settings, _scene.SelfEntityId, Context.IsWindowActive && !Context.Presentation.AwaitingConfirmation &&
            !_hud.BlocksPushToTalk && Keyboard.GetState().IsKeyDown(HudSettingsPanel.KeyOf(settings, HudSettingsPanel.VoiceShortcut)),
            _scene.SendChatAsync);
        UpdateAutoHunt();
        UpdateQuestNavigation(gameTime);
        _scene.Update(gameTime);
        _hud.TargetId = _scene.SelectedTargetId;
        _hud.HelperPet = _scene.SelfHelperPet();
        _hud.HoveredId = _scene.HoveredTargetId;
        _hud.TracedId = _scene.TracedTargetId;
        _hud.ButtonsPlayerId = _scene.ReachedPlayerId;
        _lastKeyboard = Keyboard.GetState();
        if (WebWorldLoading.Active)
        {
            _worldLoadUpdated = true;
        }
    }

    public override void Draw()
    {
        _hud.RenderPortrait();
        RenderTouchPortrait();
        Context.GraphicsDevice.Clear(_scene.SkyColor);
        _scene.Draw();
        _scene.CollectGroundLabels(_groundLabels);
        if (!Context.Presentation.Requested.HideDamageNumbers)
        {
            _scene.DrawCombatText(Context.SpriteBatch, Context.Font, _hud.ElementArt);
        }
        _hud.Draw();
        DrawTouch();
        if (_worldLoadUpdated)
        {
            _worldLoadUpdated = false;
            WebWorldLoading.Complete();
            DeferredWorldFiles.Begin();
        }
    }

    private void UpdateMotionKeys()
    {
        var keyboard = Keyboard.GetState();
        if (!keyboard.IsKeyDown(Keys.LeftControl) && !keyboard.IsKeyDown(Keys.RightControl))
        {
            return;
        }

        if (Pressed(keyboard, Keys.D1))
        {
            _scene.DebugForceMotion(MotionCode.Stand);
        }
        if (Pressed(keyboard, Keys.D2))
        {
            _scene.DebugForceMotion(MotionCode.Walk);
        }
        if (Pressed(keyboard, Keys.D3))
        {
            _scene.DebugForceMotion(MotionCode.Run);
        }
        if (Pressed(keyboard, Keys.D4))
        {
            _scene.DebugForceMotion(MotionCode.Attack);
        }
        if (Pressed(keyboard, Keys.D5))
        {
            _scene.DebugForceMotion(MotionCode.Damage);
        }
        if (Pressed(keyboard, Keys.D6))
        {
            _scene.DebugForceMotion(MotionCode.Dead);
        }
        if (Pressed(keyboard, Keys.D7))
        {
            _scene.DebugForceMotion(MotionCode.Skill);
        }
    }

    private void UpdateDebugKeys()
    {
        var keyboard = Keyboard.GetState();
        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        if (control && Pressed(keyboard, Keys.F3))
        {
            _showRenderStats = !_showRenderStats;
        }
        if (control && Pressed(keyboard, Keys.F5))
        {
            _scene.ToggleShadows();
        }
        if (control && Pressed(keyboard, Keys.F10))
        {
            _scene.CycleRenderDebugView();
        }
    }

    private bool Pressed(KeyboardState keyboard, Keys key) =>
        keyboard.IsKeyDown(key) && !_lastKeyboard.IsKeyDown(key);

    public override void HandleEscape() => _hud?.HandleEscape();

    // The scene clears its hovered entity while the HUD has the pointer, so windows show the arrow unless the HUD imposes
    // one (a shop's mode, a dragged item).
    public override HudCursorKind Cursor =>
        _scene is null || _hud is null
            ? HudCursorKind.Default
            : _hud.CursorOverride ?? HudCursor.ForHovered(_scene.HoveredDropId != Guid.Empty, Mouse.GetState().LeftButton == ButtonState.Pressed,
                _hud.Combat.KindOf(_scene.HoveredTargetId, out var owner), owner);

    // The server's shutdown asks every client to save and quit: the game closes as from the window's close button.
    private void RequestExit() => Context.ExitRequested = true;

    private void ReturnToLobby(bool servers) => Context.RequestScreen(servers
        ? new Opening.ServerSelectScreen(Context)
        : new CharacterSelect.CharacterSelectScreen(Context));

    public override void Dispose()
    {
        WebWorldHud.Detach(_hud);
        _voice.Dispose();
        CancelQuestNavigation();
        if (_scene is not null && _hud is not null)
        {
            _scene.PresenceObserved -= _hud.Combat.Observe;
            _scene.PresenceObserved -= _hud.StringEffect.Observe;
            _scene.PresenceObserved -= _hud.ObserveQuests;
            _scene.PresenceObserved -= ObserveHuntTargets;
            _scene.PresenceObserved -= _hud.ObserveChat;
            _scene.PresenceObserved -= _hud.ObserveGuide;
            _hud.Chat.WhisperArrived -= _scene.PlayWhisper;
            _hud.Chat.CloseRequested -= RequestExit;
            _hud.ExitRequested -= RequestExit;
            _hud.LobbyRequested -= ReturnToLobby;
            _hud.Restart.ExitRequested -= RequestExit;
            _hud.QuestFanfare -= _scene.PlayQuestFanfare;
            _hud.QuestDestinationRequested -= NavigateToQuest;
            _hud.QuestNavigationCanceled -= CancelQuestNavigation;
            _scene.PresenceObserved -= ObserveQuestNavigation;
            _hud.SiegeEnded -= _scene.PlaySiegeEnd;
            _hud.SiegeClosed -= _scene.RestoreFieldMusic;
            _hud.CraftResult -= _scene.PlayCraftResult;
            _scene.NpcSelected -= _hud.OpenNpc;
            _scene.ShopSellerSelected -= _hud.ViewPersonalShop;
            _scene.PresenceObserved -= _hud.ObserveParty;
            _scene.PresenceObserved -= _hud.ObserveClan;
            _scene.PresenceObserved -= _hud.ObserveCastle;
            _scene.PresenceObserved -= _hud.ObserveBellatra;
            _scene.PresenceObserved -= _hud.ObserveTrade;
            _scene.PresenceObserved -= _hud.ObserveDuel;
            _scene.PresenceObserved -= _hud.ObserveArena;
            _scene.PresenceObserved -= _hud.ObserveRoyale;
            _scene.PresenceObserved -= _hud.ObserveTitles;
            _scene.PresenceObserved -= _hud.ObserveContacts;
            _hud.PlayerButtonsUsed -= _scene.DismissPlayerButtons;
            _scene.SkillNotice -= _hud.Chat.System;
            _hud.SummonModeRequested -= _scene.ToggleSummonMode;
            _hud.CrystalPetDismissRequested -= _scene.DismissCrystalPets;
            _hud.RunToggleRequested -= _scene.ToggleRun;
            _hud.PvpToggleRequested -= _scene.TogglePvp;
            _hud.TitleChosen -= _scene.ChooseTitle;
            _hud.YahooKeyPressed -= _scene.PressYahooKey;
            _hud.ItemSound -= _scene.PlayItemSound;
            _hud.TouchAttackRequested -= TouchAttack;
            _hud.TouchSkillRequested -= TouchSkill;
        }

        _joystick?.Dispose();
        _touchPortrait?.Dispose();
        _hud?.Dispose();
        _scene?.Dispose();
    }

    public override Task Closing => _scene?.Closing ?? Task.CompletedTask;
}
