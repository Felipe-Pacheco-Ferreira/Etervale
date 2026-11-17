using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Assets;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.Screens.Gameplay;
using PristonTale.Gameplay.Screens.Opening;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

public sealed partial class CharacterSelectScreen : GameScreen
{
    internal const string LoginStagePath = CharacterSelectStage.StagePath;

    private static readonly Color LoginClearColor = new Color((int)CharacterSelectStage.ClearColor.R, CharacterSelectStage.ClearColor.G, CharacterSelectStage.ClearColor.B);

    private const int MaxNameLength = CharacterNames.MaxLength;

    private readonly PristonAssetCache _assetCache = new();
    private readonly PristonSmdReader _reader;
    private readonly PristonActorLoader _actorLoader;

    private PristonRenderer _renderer;
    private CharacterSelectCamera _cameraCtl;
    private CharacterSelectScene _scene;
    private CharacterSelectPortrait _portrait;

    private VfxSystem _vfx;
    private VfxRenderer _vfxRenderer;
    private CharSelectFlames _flames;

    // Where the green flames burn (CharSelectEffects).
    public static readonly Vector3[] BrazierPositions = [.. CharacterSelectStage.Braziers.Select(static brazier => new Vector3(brazier.X, brazier.Y, brazier.Z))];

    private LoginAudio _audio;
    private MouseState _lastMouse;
    private KeyboardState _lastKeyboard;
    private double _lastClickSeconds = -1d;
    private int _lastClickIndex = -1;
    private double _elapsedSeconds;

    private CharSelectAssets _assets;
    private CharacterSelectView _view;
    private readonly CharSelectViewModel _viewModel = new();

    private int _hoveredIndex = -1;
    private int _selectedIndex = -1;
    private bool _hoverNewCharacter;

    private CharacterBrood _chosenBrood;
    private int _jobHovered = -1;
    private int _jobSelected = -1;
    private int _faceIndex;
    private int _faceHovered = -1;
    private int _raceHovered = -1;
    private TextInputField _nameField;

    private enum State { CharacterSelect = 0, RaceSelect = 1, JobSelect = 2, FaceSelect = 3, Name = 4 }
    private enum Busy { None, LoadingRoster, PreparingRoster, PreparingJobLineup, Entering, Deleting, Creating }
    private State _state = State.CharacterSelect;
    private Busy _busy = Busy.None;
    private string _errorMessage = string.Empty;

    private CancellationTokenSource _backendCts;
    private Task<IReadOnlyList<CharacterSummary>> _rosterTask;
    private Task _rosterPreparationTask;
    private IReadOnlyList<CharacterSummary> _pendingRoster;
    private Task _jobPreparationTask;
    private Task<EnterWorldOutcome> _enterTask;
    private CharacterSummary _enteringSummary;
    private Task<DeleteCharacterOutcome> _deleteTask;
    private Task<CreateCharacterOutcome> _createTask;

    public CharacterSelectScreen(ScreenContext context) : base(context)
    {
        _reader = new PristonSmdReader(_assetCache);
        _actorLoader = new PristonActorLoader(_assetCache);
    }

    public Func<MouseState> PointerInput { get; init; }
    public float ButtonScale { get; init; } = 1f;
    // How many times wider than for a cursor a character's pick box is (touch).
    public float PickScale { get; init; } = 1f;
    public float CameraDistanceScale { get; init; } = 1f;
    // The 2D layer (frame, marks, captions, faces, buttons) is laid out on a screen this many times smaller and drawn this
    // many times larger, so a phone shows the PC layout at a readable size; the 3D picks keep the real screen.
    public float UiScale { get; init; } = 1f;
    // A touch screen's keyboard: the name takes typing only after a tap on it, and a tap elsewhere ends it.
    public bool TouchKeyboard { get; init; }
    public Action<CharacterSummary, EnterWorldOutcome> WorldEntered { get; init; }
    public bool IsNameInputActive => _nameField?.Focused == true && _state is State.FaceSelect or State.Name;
    // The name was tapped this frame: the keyboard opens again even when the system closed it.
    public bool NameFieldTapped { get; private set; }

    private Viewport UiViewport
    {
        get
        {
            var viewport = Context.GraphicsDevice.Viewport;
            return new Viewport(0, 0, (int)(viewport.Width / UiScale), (int)(viewport.Height / UiScale));
        }
    }

    private Point UiPoint(in MouseState mouse) => new((int)(mouse.X / UiScale), (int)(mouse.Y / UiScale));

    private Vector2 UiCursor(in MouseState mouse) => new(mouse.X / UiScale, mouse.Y / UiScale);

    public override string StatusText => ClientText.Get("Status.CharacterSelect");
    internal WebArrivalBackdrop Backdrop { get; init; }

    public override void Enter()
    {
        var stage = Backdrop?.Stage;
        if (Backdrop is null)
        {
            _renderer = new PristonRenderer(Context.GraphicsDevice, Context.Content, Context.Log, Context.Presentation);
            ConfigureLighting(_renderer);
            stage = _reader.ReadStage(AssetPath(LoginStagePath));
            _renderer.AddStage(stage, Array.Empty<PristonModel>());
            InitEffects();
        }
        else
        {
            _renderer = Backdrop.Renderer;
        }

        _cameraCtl = new CharacterSelectCamera(Context.GraphicsDevice, (int)State.CharacterSelect, CameraDistanceScale, Backdrop?.Camera);
        _scene = new CharacterSelectScene(_renderer, _actorLoader, Context.AssetRoot, _cameraCtl, stage, Context.Log);

        _nameField = new TextInputField { MaxLength = MaxNameLength };

        if (!_webOverlay)
        {
            _assets = new CharSelectAssets(Context.GraphicsDevice, AssetPath);
            _portrait = new CharacterSelectPortrait(Context.GraphicsDevice);
            _view = new CharacterSelectView(Context.SpriteBatch, Context.Font, _assets, _cameraCtl, ButtonScale, UiScale);
        }
        _audio = LoginAudio.CharacterSelect(Context.Audio);
        if (_webOverlay)
        {
            WebCharacterSelectOverlay.Clear();
        }

        BeginLoadRoster();
    }

    private void InitEffects()
    {
        var library = Context.Effects?.Vfx.Library;
        if (library is null)
        {
            return;
        }

        library.Register(CharSelectEffects.GreenBeamDefinition());
        if (!library.TryGet(CharSelectEffects.GreenBeam, out var beam))
        {
            return;
        }

        _vfx = new VfxSystem(VfxNoAnchors.Instance, new TransientLightServices(_renderer.TransientLights), library);
        _vfxRenderer = new VfxRenderer(Context.GraphicsDevice, Context.Content);
        _flames = new CharSelectFlames(_vfx, beam, BrazierPositions);
    }

    public override void Update(GameTime gameTime)
    {
        _elapsedSeconds = gameTime.TotalGameTime.TotalSeconds;
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _audio.Update(dt);

        var cameraStep = Backdrop is not null && _busy == Busy.PreparingJobLineup ? State.JobSelect : _state;
        _cameraCtl.Update(Context.GraphicsDevice, (int)cameraStep, dt);

        _scene.UpdateRun(dt);

        if (Backdrop is not null)
        {
            Backdrop.UpdateScene(gameTime);
        }
        else
        {
            _renderer.Update(gameTime);
            _flames?.Update(dt);
        }

        var mouse = PointerInput?.Invoke() ?? Mouse.GetState();
        var keyboard = Keyboard.GetState();
        // Drained every frame, so keys typed before the name box opens never reach it.
        var typed = Context.TypedText.Drain();

        if (_busy != Busy.None)
        {
            PollBusy();
        }

        if (_webOverlay)
        {
            UpdateWebRosterArrival(dt);
            UpdateWebCreationEffects(dt);
            UpdateWeb(mouse);
            ReportWeb();
        }
        else if (_busy == Busy.None && !Context.Presentation.AwaitingConfirmation)
        {
            switch (_state)
            {
                case State.CharacterSelect:
                    UpdateCharacterSelect(mouse);
                    break;
                case State.RaceSelect:
                    UpdateRaceSelect(mouse);
                    break;
                case State.JobSelect:
                    UpdateJobSelect(mouse);
                    break;
                case State.FaceSelect:
                case State.Name:
                    UpdateFaceSelect(mouse, typed, gameTime.ElapsedGameTime.TotalSeconds);
                    break;
            }
        }

        _lastMouse = mouse;
        _lastKeyboard = keyboard;
    }

    // The scene's light: an even interior ambient and no lamps, so the statues keep their own colours under the green
    // flames (also used by the regression's capture probe).
    internal static void ConfigureLighting(PristonRenderer renderer)
    {
        renderer.Underground = true;
        renderer.UndergroundAmbient = new Vector3(CharacterSelectStage.Ambient.R, CharacterSelectStage.Ambient.G, CharacterSelectStage.Ambient.B);
        renderer.ActorFloorLight = false;
    }

    private void BeginLoadRoster()
    {
        _busy = Busy.LoadingRoster;
        _errorMessage = string.Empty;
        ResetBackendCall();
        _rosterTask = Context.Backend.ListCharactersAsync(_backendCts.Token);
    }

    private void PollBusy()
    {
        switch (_busy)
        {
            case Busy.LoadingRoster:
                PollRoster();
                break;
            case Busy.PreparingRoster:
                PollRosterPreparation();
                break;
            case Busy.PreparingJobLineup:
                PollJobPreparation();
                break;
            case Busy.Entering:
                PollEnterWorld();
                break;
            case Busy.Deleting:
                PollDelete();
                break;
            case Busy.Creating:
                PollCreate();
                break;
        }
    }

    private void PollRoster()
    {
        if (_rosterTask is not { IsCompleted: true })
        {
            return;
        }

        if (_rosterTask.IsCompletedSuccessfully)
        {
            _pendingRoster = _rosterTask.Result;
            _rosterTask = null;
            _rosterPreparationTask = PrepareRosterAsync(_pendingRoster, _backendCts.Token);
            _busy = Busy.PreparingRoster;
            return;
        }

        Context.Log.Error("[CharSelect] character-list request failed", _rosterTask.Exception?.GetBaseException());
        _rosterTask = null;
        _errorMessage = ClientText.Get("CharSelect.LoadFailed");
        _busy = Busy.None;
        _state = State.CharacterSelect;
    }

    private async Task PrepareRosterAsync(IReadOnlyList<CharacterSummary> roster, CancellationToken cancellationToken)
    {
        if (Context.PrepareCharacterAssetsAsync is { } prepare)
        {
            var classes = roster.Select(character => character.Class).Where(Enum.IsDefined).Distinct().ToArray();
            await prepare(classes, cancellationToken);
        }

        await _scene.PrepareRosterAsync(roster, cancellationToken);
    }

    private void PollRosterPreparation()
    {
        if (_rosterPreparationTask is not { IsCompleted: true })
        {
            return;
        }

        if (_rosterPreparationTask.IsCompletedSuccessfully)
        {
            BuildCharacters(_pendingRoster);
            _errorMessage = string.Empty;
        }
        else
        {
            Context.Log.Error("[CharSelect] roster actor preparation failed", _rosterPreparationTask.Exception?.GetBaseException());
            _errorMessage = ClientText.Get("CharSelect.ModelsFailed");
        }

        _pendingRoster = null;
        _rosterPreparationTask = null;
        _busy = Busy.None;
        _state = State.CharacterSelect;
    }

    private void BuildCharacters(IReadOnlyList<CharacterSummary> roster)
    {
        _scene.BuildRoster(roster);
        _hoveredIndex = -1;
        // The page's list always shows a chosen character (the first) and its details.
        _selectedIndex = _webOverlay && _scene.RosterCount > 0 ? 0 : -1;
        _scene.SetRosterHighlight(-1, _selectedIndex);
        BeginWebRosterArrival();
        if (_webCreatedName is { } createdName)
        {
            _webCreatedName = null;
            for (var index = 0; index < _scene.RosterCount; index++)
            {
                if (string.Equals(_scene.RosterSummary(index).Name, createdName, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedIndex = index;
                    _scene.SetRosterHighlight(-1, index);
                    PlayWebCreatedArrival(index);
                    break;
                }
            }
        }
    }

    private void UpdateCharacterSelect(MouseState mouse)
    {
        var cursor = new Vector2(mouse.X, mouse.Y);
        _hoveredIndex = _scene.PickRoster(cursor, Context.GraphicsDevice.Viewport, PickScale);
        _hoverNewCharacter = HasFreeSlot && CharSelectLayout.NewCharHotspot(UiViewport).Contains(UiPoint(mouse));
        _scene.SetRosterHighlight(_hoveredIndex, _selectedIndex);

        if (!Clicked(mouse))
        {
            return;
        }

        if (_hoverNewCharacter)
        {
            EnterRaceSelect();
            return;
        }

        if (HandleSelectButtons(UiCursor(mouse)))
        {
            return;
        }

        if (_hoveredIndex >= 0)
        {
            var now = _elapsedSeconds;
            var isDouble = _lastClickIndex == _hoveredIndex && (now - _lastClickSeconds) < CharacterSelectStage.DoublePressSeconds;
            _selectedIndex = _hoveredIndex;
            _audio.Play(LoginCue.PickCharacter);
            if (isDouble)
            {
                BeginEnterWorld();
            }

            _lastClickSeconds = now;
            _lastClickIndex = _hoveredIndex;
        }
        else
        {
            _selectedIndex = -1;
        }

        _scene.SetRosterHighlight(_hoveredIndex, _selectedIndex);
    }

    private bool HasFreeSlot => _scene.RosterCount < _scene.MaxRoster;

    private bool HandleSelectButtons(Vector2 cursor)
    {
        var viewport = UiViewport;
        var y = CharSelectLayout.ButtonRowY(viewport);
        var hasChars = _scene.RosterCount > 0;

        var primary = hasChars ? _assets.BtSelect : _assets.BtNewChar;
        var primaryKind = hasChars ? CharacterSelectButton.Select : CharacterSelectButton.NewCharacter;
        if (InButton(cursor, primary, CharSelectLayout.TwoButtonPrimaryX(viewport), y, primaryKind))
        {
            if (hasChars)
            {
                _audio.Play(LoginCue.Button);
                if (_selectedIndex >= 0)
                {
                    BeginEnterWorld();
                }
            }
            else
            {
                EnterRaceSelect();
            }
            return true;
        }
        if (InButton(cursor, _assets.BtExit, CharSelectLayout.TwoButtonQuitX(viewport), y, CharacterSelectButton.Exit))
        {
            _audio.Play(LoginCue.Button);
            Context.RequestScreen(new OpeningScreen(Context));
            return true;
        }
        if (_selectedIndex >= 0 &&
            InButton(cursor, _assets.BtDelete, CharSelectLayout.TwoButtonDeleteX, y, CharacterSelectButton.Delete))
        {
            _audio.Play(LoginCue.Button);
            BeginDelete();
            return true;
        }
        return false;
    }

    private void BeginEnterWorld()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _scene.RosterCount)
        {
            return;
        }

        _audio.Play(LoginCue.GameStart);
        _enteringSummary = _scene.RosterSummary(_selectedIndex);
        var name = _enteringSummary.Name;
        _errorMessage = string.Empty;
        _busy = Busy.Entering;
        ResetBackendCall();
        _enterTask = EnterWithAssetsAsync(name, _backendCts.Token);
    }

    private async Task<EnterWorldOutcome> EnterWithAssetsAsync(string name, CancellationToken cancellationToken)
    {
        if (Context.PrepareWorldAssetsAsync is { } prepare)
        {
            await prepare().WaitAsync(cancellationToken);
        }

        return await Context.Backend.EnterWorldAsync(name, cancellationToken);
    }

    private void PollEnterWorld()
    {
        if (_enterTask is not { IsCompleted: true })
        {
            return;
        }

        if (_enterTask.IsCompletedSuccessfully && _enterTask.Result.Ok)
        {
            var outcome = _enterTask.Result;
            var s = _enteringSummary;
            var entered = new EnteredCharacter(
                s.Class, s.Name ?? string.Empty, s.Level, s.FaceIndex,
                outcome.FieldId, outcome.SpawnX, outcome.SpawnZ, s.Tier, s.HairIndex);
            _enterTask = null;
            if (WorldEntered is { } onWorldEntered)
            {
                onWorldEntered(s, outcome);
            }
            else
            {
                Context.RequestScreen(new GameplayScreen(Context, entered));
            }
            return;
        }
        _errorMessage = _enterTask.IsCompletedSuccessfully
            ? ClientText.Get("CharSelect.EnterFailure." + _enterTask.Result.Failure)
            : ClientText.Get("CharSelect.Unreachable");
        _enterTask = null;
        _busy = Busy.None;
        _state = State.CharacterSelect;
    }

    private void BeginDelete()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _scene.RosterCount)
        {
            return;
        }

        var name = _scene.RosterSummary(_selectedIndex).Name;
        _errorMessage = string.Empty;
        _busy = Busy.Deleting;
        ResetBackendCall();
        _deleteTask = Context.Backend.DeleteCharacterAsync(name, _backendCts.Token);
    }

    private void PollDelete()
    {
        if (_deleteTask is not { IsCompleted: true })
        {
            return;
        }

        var ok = _deleteTask.IsCompletedSuccessfully && _deleteTask.Result.Ok;
        _deleteTask = null;
        if (ok)
        {
            _audio.Play(LoginCue.Delete);
            BeginLoadRoster();
        }
        else
        {
            _errorMessage = ClientText.Get("CharSelect.DeleteFailed");
            _busy = Busy.None;
            _state = State.CharacterSelect;
        }
    }

    private void EnterRaceSelect()
    {
        _audio.Play(LoginCue.NewCharacter);
        ClearJobActors();
        if (Backdrop is not null)
        {
            _webRosterArriving = false;
            _webCreatedActor = -1;
            SetWebRosterHidden(true);
        }

        _state = State.RaceSelect;
        _errorMessage = string.Empty;
    }

    private void UpdateRaceSelect(MouseState mouse)
    {
        var cursor = UiCursor(mouse);
        var point = UiPoint(mouse);
        var viewport = UiViewport;
        _raceHovered = -1;
        if (CharSelectLayout.TempscronMark(viewport).Contains(point))
        {
            _raceHovered = 0;
        }
        else if (CharSelectLayout.MorionMark(viewport).Contains(point))
        {
            _raceHovered = 1;
        }

        if (!Clicked(mouse))
        {
            return;
        }

        if (_raceHovered == 0)
        {
            ChooseBrood(CharacterBrood.Tempskron);
            return;
        }

        if (_raceHovered == 1)
        {
            ChooseBrood(CharacterBrood.Morion);
            return;
        }

        if (HandleCreateButtons(cursor))
        {
            return;
        }
    }

    private void ChooseBrood(CharacterBrood brood)
    {
        _audio.Play(LoginCue.NewCharacter);
        _chosenBrood = brood;
        _errorMessage = string.Empty;
        ResetBackendCall();
        _jobPreparationTask = PrepareJobLineupAsync(brood, _backendCts.Token);
        _busy = Busy.PreparingJobLineup;
    }

    private async Task PrepareJobLineupAsync(CharacterBrood brood, CancellationToken cancellationToken)
    {
        if (Context.PrepareCharacterAssetsAsync is { } prepare)
        {
            await prepare(CharacterSelectScene.JobsForBrood(brood), cancellationToken);
        }

        await _scene.PrepareJobLineupAsync(brood, cancellationToken);
    }

    private void PollJobPreparation()
    {
        if (_jobPreparationTask is not { IsCompleted: true })
        {
            return;
        }

        if (_jobPreparationTask.IsCompletedSuccessfully)
        {
            if (Backdrop is not null && !_cameraCtl.HasReached((int)State.JobSelect))
            {
                return;
            }

            BuildJobLineup();
            _state = State.JobSelect;
            BeginWebJobArrival();
        }
        else
        {
            Context.Log.Error("[CharSelect] job-lineup actor preparation failed", _jobPreparationTask.Exception?.GetBaseException());
            _errorMessage = ClientText.Get("CharSelect.ClassModelsFailed");
        }

        _jobPreparationTask = null;
        _busy = Busy.None;
    }

    private void BuildJobLineup()
    {
        _scene.BuildJobLineup(_chosenBrood);
        _jobHovered = -1;
        _lastClickIndex = -1;
        _jobSelected = -1;
        _faceIndex = 0;
    }

    private void UpdateJobSelect(MouseState mouse)
    {
        var cursor = new Vector2(mouse.X, mouse.Y);
        _jobHovered = _scene.PickJob(cursor, Context.GraphicsDevice.Viewport, PickScale);
        _scene.SetJobHighlight(_jobHovered, _jobSelected);

        if (!Clicked(mouse))
        {
            return;
        }

        if (HandleCreateButtons(UiCursor(mouse)))
        {
            return;
        }

        if (_jobHovered >= 0)
        {
            var now = _elapsedSeconds;
            var isDouble = _lastClickIndex == _jobHovered && (now - _lastClickSeconds) < CharacterSelectStage.DoublePressSeconds;
            _jobSelected = _jobHovered;
            _audio.Play(LoginCue.PickCharacter);
            if (isDouble)
            {
                EnterFaceSelect();
            }

            _lastClickSeconds = now;
            _lastClickIndex = _jobHovered;
            _scene.SetJobHighlight(_jobHovered, _jobSelected);
        }
    }

    private void EnterFaceSelect()
    {
        if (_jobSelected < 0 || _jobSelected >= _scene.JobCount)
        {
            return;
        }

        _faceIndex = 0;
        _nameField.Clear();
        _nameField.Focused = true;
        FinishWebJobArrival();
        _assets?.LoadFaceThumbs(_scene.JobClass(_jobSelected));
        if (!_webOverlay)
        {
            _scene.ApplyFace(_jobSelected, _faceIndex);
            _scene.OpenFacePreview(_jobSelected, _faceIndex);
        }
        _scene.StartForwardRun(_jobSelected);
        _scene.SetJobHighlight(-1, -1);
        if (Backdrop is not null)
        {
            for (var index = 0; index < _scene.JobCount; index++)
            {
                _scene.SetJobHidden(index, index != _jobSelected);
            }
        }

        _state = State.FaceSelect;
        _errorMessage = string.Empty;
    }

    private void ReturnChosenJobToLineup() => _scene.ReturnToLineup(_jobSelected);

    private void UpdateFaceSelect(MouseState mouse, ReadOnlySpan<char> typed, double dtSeconds)
    {
        NameFieldTapped = false;
        _nameField.Update(typed, dtSeconds);
        var viewport = UiViewport;
        var point = UiPoint(mouse);
        _faceHovered = -1;
        for (var index = 0; index < CharSelectLayout.FacesPerJob; index++)
        {
            if (CharSelectLayout.FaceThumbHit(viewport, index).Contains(point))
            {
                _faceHovered = index;
                break;
            }
        }

        if (!Clicked(mouse))
        {
            return;
        }

        // On a touch screen, as on the login panel, a tap on the name opens the keyboard and a tap anywhere else closes it.
        if (TouchKeyboard)
        {
            _nameField.Focused = CharSelectLayout.NameField(viewport).Contains(point);
            NameFieldTapped = _nameField.Focused;
        }

        for (var i = 0; i < CharSelectLayout.FacesPerJob; i++)
        {
            if (CharSelectLayout.FaceThumbHit(viewport, i).Contains(point))
            {
                if (_faceIndex != i)
                {
                    _faceIndex = i;
                    _scene.ApplyFace(_jobSelected, i);
                }
                return;
            }
        }

        HandleCreateButtons(UiCursor(mouse));
    }

    private bool HandleCreateButtons(Vector2 cursor)
    {
        var viewport = UiViewport;
        var y = CharSelectLayout.ButtonRowY(viewport);

        if (InButton(cursor, _assets.BtSelect, CharSelectLayout.ThreeButtonOkX(viewport), y, CharacterSelectButton.Select))
        {
            _audio.Play(LoginCue.Button);
            OnCreateOk();
            return true;
        }

        if (InButton(cursor, _assets.BtBack, CharSelectLayout.ThreeButtonBackX(viewport), y, CharacterSelectButton.Back))
        {
            _audio.Play(LoginCue.Back);
            StepBack();
            return true;
        }

        if (InButton(cursor, _assets.BtExit, CharSelectLayout.ThreeButtonQuitX(viewport), y, CharacterSelectButton.Exit))
        {
            _audio.Play(LoginCue.Button);
            Context.RequestScreen(new OpeningScreen(Context));
            return true;
        }

        return false;
    }

    private void OnCreateOk()
    {
        switch (_state)
        {
            case State.RaceSelect:
                _errorMessage = ClientText.Get("CharSelect.ChooseTribe");
                break;
            case State.JobSelect:
                if (_jobSelected >= 0)
                {
                    EnterFaceSelect();
                }
                else
                {
                    _errorMessage = ClientText.Get("CharSelect.ChooseClass");
                }

                break;
            case State.FaceSelect:
            case State.Name:
                BeginCreate();
                break;
        }
    }

    private void StepBack()
    {
        switch (_state)
        {
            case State.RaceSelect:
                ClearJobActors();
                if (Backdrop is not null)
                {
                    SetWebRosterHidden(false);
                }
                _state = State.CharacterSelect;
                break;
            case State.JobSelect:
                ClearJobActors();
                _state = State.RaceSelect;
                break;
            case State.FaceSelect:
            case State.Name:
                _scene.CloseFacePreview();
                if (Backdrop is not null)
                {
                    _scene.ApplyFace(_jobSelected, 0);
                }
                ReturnChosenJobToLineup();
                if (Backdrop is not null)
                {
                    for (var index = 0; index < _scene.JobCount; index++)
                    {
                        _scene.SetJobHidden(index, false);
                    }
                    _faceIndex = 0;
                    _scene.SetJobHighlight(-1, _jobSelected);
                }
                else
                {
                    _jobSelected = -1;
                }
                _lastClickIndex = -1;
                _nameField.Focused = false;
                _assets?.ClearFaceThumbs();
                _state = State.JobSelect;
                break;
        }
        _errorMessage = string.Empty;
    }

    private void BeginCreate()
    {
        if (_jobSelected < 0 || _jobSelected >= _scene.JobCount)
        {
            _errorMessage = ClientText.Get("CharSelect.ChooseClass");
            return;
        }
        var name = _nameField.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _errorMessage = ClientText.Get("CharSelect.EnterName");
            return;
        }
        _audio.Play(LoginCue.GameStart);
        _errorMessage = string.Empty;
        _busy = Busy.Creating;
        ResetBackendCall();
        _createTask = Context.Backend.CreateCharacterAsync(name, _scene.JobClass(_jobSelected), _faceIndex, _backendCts.Token);
    }

    private void PollCreate()
    {
        if (_createTask is not { IsCompleted: true })
        {
            return;
        }

        var ok = _createTask.IsCompletedSuccessfully && _createTask.Result.Ok;
        var failure = _createTask.IsCompletedSuccessfully ? _createTask.Result.Failure : CreateCharacterFailure.Rejected;
        _createTask = null;
        if (ok)
        {
            if (_webOverlay)
            {
                _webCreatedName = _nameField.Text.Trim();
            }
            ClearJobActors();
            _state = State.CharacterSelect;
            BeginLoadRoster();
        }
        else
        {
            _errorMessage = ClientText.Get("CharSelect.CreateFailure." + failure);
            _busy = Busy.None;
        }
    }

    private void ClearJobActors()
    {
        FinishWebJobArrival();
        _scene.ClearJobLineup();
        _jobHovered = -1;
        _jobSelected = -1;
    }

    public override void Draw()
    {
        if (Backdrop is not null)
        {
            Backdrop.Draw();
            return;
        }

        var viewport = Context.GraphicsDevice.Viewport;
        var viewModel = _webOverlay ? null : BuildViewModel();
        if (viewModel is not null)
        {
            viewModel.FacePortrait = RenderFacePortrait();
        }

        Context.GraphicsDevice.Clear(LoginClearColor);
        _renderer.Draw(_cameraCtl.Camera);

        // Effects retain scene depth; the 2D view must remain last.
        if (_vfx is not null)
        {
            _vfxRenderer.Draw(_vfx, _cameraCtl.View, _cameraCtl.Projection, _cameraCtl.Position, _renderer.SceneLinearDepth);
        }

        _renderer.ComposeScene();

        // In the browser the page draws the 2D layer (CharacterSelectScreen.Web.cs).
        if (viewModel is not null)
        {
            _view.Draw(viewport, viewModel);
        }
    }

    private Texture2D RenderFacePortrait()
    {
        if (_state is not State.FaceSelect and not State.Name || _jobSelected < 0 || _jobSelected >= _scene.JobCount)
        {
            return null;
        }

        // Rendered at the size it is shown on the real screen, so the enlarged layer does not blur it.
        var size = CharSelectLayout.FacePortrait(UiViewport).Size;
        return _portrait.Render(_renderer, _scene.FacePreviewHandles, (int)(size.X * UiScale), (int)(size.Y * UiScale));
    }

    private CharSelectViewModel BuildViewModel()
    {
        _viewModel.StateIndex = (int)_state;
        _viewModel.ErrorMessage = _busy == Busy.Entering ? ClientText.Get("Status.EnteringWorld") : _errorMessage;
        _viewModel.ElapsedSeconds = _elapsedSeconds;
        _viewModel.RosterHasChars = _scene.RosterCount > 0;
        _viewModel.HasSelectedRoster = _selectedIndex >= 0;
        _viewModel.ShowNewCharacterPrompt = HasFreeSlot && (_hoverNewCharacter || _scene.RosterCount == 0);
        _viewModel.Info = _scene.Info(_hoveredIndex);
        _viewModel.HoveredRace = _raceHovered;
        _viewModel.JobCount = Math.Min(_scene.JobCount, _viewModel.Jobs.Length);
        for (var index = 0; index < _viewModel.JobCount; index++)
        {
            _viewModel.Jobs[index] = new CharSelectJobView(
                _scene.JobClass(index),
                _scene.JobHeadWorld(index),
                index == _jobHovered,
                index == _jobSelected);
        }

        _viewModel.FaceIndex = _faceIndex;
        _viewModel.HoveredFace = _faceHovered;
        _viewModel.FaceBrood = _chosenBrood;
        _viewModel.NameDisplay = _nameField.DisplayText;
        _viewModel.CaretVisible = _nameField.CaretVisible;
        return _viewModel;
    }

    private bool InButton(
        Vector2 cursor,
        Texture2D texture,
        int x,
        int y,
        CharacterSelectButton kind)
    {
        if (texture == null)
        {
            return false;
        }

        var hit = CharSelectLayout.ButtonHit(texture, x, y, kind);
        return CharSelectLayout.ScaleButton(hit, UiViewport, ButtonScale, kind == CharacterSelectButton.Delete)
            .Contains((int)cursor.X, (int)cursor.Y);
    }

    private bool Clicked(in MouseState mouse) =>
        mouse.LeftButton == ButtonState.Pressed && _lastMouse.LeftButton == ButtonState.Released;

    private void ResetBackendCall()
    {
        _backendCts?.Cancel();
        _backendCts?.Dispose();
        _backendCts = new CancellationTokenSource();
    }

    public override void Dispose()
    {
        if (_webOverlay)
        {
            CloseWeb();
        }

        _backendCts?.Cancel();
        _backendCts?.Dispose();
        _audio?.Dispose();
        _vfxRenderer?.Dispose();
        _portrait?.Dispose();
        _assets?.Dispose();
        if (Backdrop is not null)
        {
            Backdrop.Dispose();
        }
        else
        {
            _renderer?.Dispose();
        }
    }
}
