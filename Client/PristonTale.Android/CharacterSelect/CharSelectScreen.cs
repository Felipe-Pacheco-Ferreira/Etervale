using System.Diagnostics;
using System.Numerics;
using Android.Content;
using Android.Opengl;
using Javax.Microedition.Khronos.Opengles;
using PristonTale.Assets;
using PristonTale.Client.Net;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Sessions;
using PristonTale.Simulation.Characters;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;

namespace PristonTale.Mobile.CharacterSelect;

// What the screen asks of the Android views around it; called on the GL thread.
internal interface ICharSelectHost
{
    // Opens or closes the on-screen keyboard for the name; clear empties the name first.
    void SetNameInput(bool open, bool clear);
}

// The character that entered the world, the server's answer and the character's class model when it was read.
internal sealed record CharSelectEntered(CharacterSummary Character, EnterWorldOutcome Outcome, CharacterModel? Model);

// The desktop CharacterSelectScreen on Android: the same steps, checks and server calls, with members under the same
// names. It runs whole on the GL thread, one Update and one Draw a frame as the desktop does; the views hand it the
// pointer and the typed name. Touch stands for the mouse: the finger is the pointer wherever it rests or last rested,
// and lifting it without a drag is the left press (docs/android.md, "Touch").
internal sealed class CharSelectScreen(Context context, CharSelectSceneData data, GameServerConnection backend, ICharSelectHost host)
    : Java.Lang.Object, GLSurfaceView.IRenderer
{
    private const int MaxNameLength = CharacterNames.MaxLength;
    private const float LongestFrameSeconds = 0.1f;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private enum State
    {
        CharacterSelect = CharacterSelectStage.RosterStep,
        RaceSelect = CharacterSelectStage.TribeStep,
        JobSelect = CharacterSelectStage.ClassStep,
        FaceSelect = CharacterSelectStage.FaceStep,
        Name = CharacterSelectStage.NameStep
    }

    private enum Busy { None, LoadingRoster, PreparingRoster, PreparingJobLineup, Entering, Deleting, Creating }

    private readonly TaskCompletionSource<CharSelectEntered?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CharSelectCamera _cameraCtl = new((int)State.CharacterSelect);
    private readonly CharSelectFlames _flames = new();
    private readonly CharSelectViewModel _viewModel = new();
    private readonly HashSet<PlayerClass> _neededClasses = [];
    private readonly Dictionary<PlayerClass, Task<CharacterModel>> _classLoads = new();
    private readonly object _inputGate = new();
    private CharSelectScene? _scene;
    private CharSelectGraphics? _graphics;
    private CharSelectView? _view;
    private CharSelectAudio? _audio;

    private Vector2 _pointer = new(-1f, -1f);
    private bool _pressed;
    private string _typedName = "";
    private bool _nameFocused;
    private double _caretSeconds;

    private int _viewportWidth = CharacterSelectLayout.DesignWidth;
    private int _viewportHeight = CharacterSelectLayout.DesignHeight;
    private long _lastFrame;
    private double _elapsedSeconds;
    private double _lastClickSeconds = -1d;
    private int _lastClickIndex = -1;

    private int _hoveredIndex = -1;
    private int _selectedIndex = -1;
    private bool _hoverNewCharacter;
    private CharacterBrood _chosenBrood;
    private int _jobHovered = -1;
    private int _jobSelected = -1;
    private int _faceIndex;
    private int _faceHovered = -1;
    private int _raceHovered = -1;

    private State _state = State.CharacterSelect;
    private Busy _busy = Busy.None;
    private string _errorMessage = string.Empty;
    private bool _finished;

    private CancellationTokenSource? _backendCts;
    private Task<IReadOnlyList<CharacterSummary>>? _rosterTask;
    private IReadOnlyList<CharacterSummary>? _pendingRoster;
    private Task<EnterWorldOutcome>? _enterTask;
    private CharacterSummary? _enteringSummary;
    private Task<DeleteCharacterOutcome>? _deleteTask;
    private Task<CreateCharacterOutcome>? _createTask;

    // The character that entered the world, or null when the player left the screen.
    public Task<CharSelectEntered?> Completion => _completion.Task;

    // The pointer in surface pixels, from the UI thread.
    public void SetPointer(float surfaceX, float surfaceY, int surfaceHeight)
    {
        var scale = Volatile.Read(ref _viewportHeight) / (float)Math.Max(1, surfaceHeight);
        lock (_inputGate)
        {
            _pointer = new Vector2(surfaceX * scale, surfaceY * scale);
        }
    }

    // A finger lifted where it went down, from the UI thread.
    public void Press()
    {
        lock (_inputGate)
        {
            _pressed = true;
        }
    }

    // The name as the keyboard has it, from the UI thread.
    public void SetTypedName(string name) => Volatile.Write(ref _typedName, name);

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        _graphics = new CharSelectGraphics(context, data);
        _scene ??= new CharSelectScene(data, _cameraCtl);
        _view = new CharSelectView(_graphics, _cameraCtl, _scene);
        if (_audio is null)
        {
            _audio = new CharSelectAudio(context);
            BeginLoadRoster();
        }
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        var viewportHeight = CharacterSelectLayout.DesignHeight;
        var viewportWidth = (int)MathF.Round(width * (float)viewportHeight / Math.Max(1, height));
        Volatile.Write(ref _viewportHeight, viewportHeight);
        _viewportWidth = viewportWidth;
        _graphics?.Resize(width, height, viewportWidth, viewportHeight);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        if (_graphics is null || _scene is null || _view is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var dt = _lastFrame == 0 ? 0f : Math.Min(LongestFrameSeconds, (float)Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds);
        _lastFrame = now;
        Update(dt);
        Draw();
    }

    public void Pause() => _audio?.Pause();

    public void Resume()
    {
        _lastFrame = 0;
        _audio?.Resume();
    }

    private void Update(float dt)
    {
        _elapsedSeconds += dt;
        _caretSeconds += dt;
        _audio!.Update(dt);
        _cameraCtl.Update((int)_state, dt, _viewportWidth / (float)_viewportHeight);
        _scene!.UpdateRun(dt);
        _flames.Update(dt);
        PrepareNextClass();

        Vector2 pointer;
        bool clicked;
        lock (_inputGate)
        {
            pointer = _pointer;
            clicked = _pressed;
            _pressed = false;
        }

        if (_finished)
        {
            return;
        }

        if (_busy != Busy.None)
        {
            PollBusy();
            return;
        }

        switch (_state)
        {
            case State.CharacterSelect:
                UpdateCharacterSelect(pointer, clicked);
                break;
            case State.RaceSelect:
                UpdateRaceSelect(pointer, clicked);
                break;
            case State.JobSelect:
                UpdateJobSelect(pointer, clicked);
                break;
            case State.FaceSelect:
            case State.Name:
                UpdateFaceSelect(pointer, clicked);
                break;
        }
    }

    // Each needed class's character file is read off this thread; one read class is uploaded a frame, so a lineup's
    // upload never holds a frame for long.
    private void PrepareNextClass()
    {
        foreach (var playerClass in _neededClasses)
        {
            if (_graphics!.IsPrepared(playerClass))
            {
                continue;
            }

            if (!_classLoads.TryGetValue(playerClass, out var load))
            {
                load = CharacterModel.LoadAsync(context.Assets!, playerClass);
                _classLoads.Add(playerClass, load);
            }

            if (load.IsCompletedSuccessfully)
            {
                _graphics.Prepare(playerClass, load.Result);
                return;
            }
        }
    }

    private bool ClassesPrepared()
    {
        foreach (var playerClass in _neededClasses)
        {
            // A class whose file could not be read stays undrawn instead of holding the screen.
            if (!_graphics!.IsPrepared(playerClass) && !(_classLoads.TryGetValue(playerClass, out var load) && load.IsFaulted))
            {
                return false;
            }
        }

        return true;
    }

    private void BeginLoadRoster()
    {
        _busy = Busy.LoadingRoster;
        _errorMessage = string.Empty;
        ResetBackendCall();
        _rosterTask = backend.ListCharactersAsync(_backendCts!.Token);
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
            foreach (var summary in _pendingRoster)
            {
                _neededClasses.Add(summary.Class);
            }

            _busy = Busy.PreparingRoster;
            return;
        }

        _rosterTask = null;
        _errorMessage = CharSelectText.Get("CharSelect.LoadFailed");
        _busy = Busy.None;
        _state = State.CharacterSelect;
    }

    private void PollRosterPreparation()
    {
        if (!ClassesPrepared())
        {
            return;
        }

        _scene!.BuildRoster(_pendingRoster!);
        _hoveredIndex = -1;
        _selectedIndex = -1;
        _errorMessage = string.Empty;
        _pendingRoster = null;
        _busy = Busy.None;
        _state = State.CharacterSelect;
    }

    private void UpdateCharacterSelect(Vector2 cursor, bool clicked)
    {
        _hoveredIndex = _scene!.PickRoster(cursor, _viewportWidth, _viewportHeight);
        _hoverNewCharacter = HasFreeSlot && CharacterSelectLayout.NewCharHotspot(_viewportWidth, _viewportHeight).Contains(cursor.X, cursor.Y);
        _scene.SetRosterHighlight(_hoveredIndex, _selectedIndex);
        if (!clicked)
        {
            return;
        }

        if (_hoverNewCharacter)
        {
            EnterRaceSelect();
            return;
        }

        if (HandleSelectButtons(cursor))
        {
            return;
        }

        if (_hoveredIndex >= 0)
        {
            var isDouble = _lastClickIndex == _hoveredIndex && _elapsedSeconds - _lastClickSeconds < CharacterSelectStage.DoublePressSeconds;
            _selectedIndex = _hoveredIndex;
            _audio!.Play(CharacterSelectSounds.PickCharacter);
            if (isDouble)
            {
                BeginEnterWorld();
            }

            _lastClickSeconds = _elapsedSeconds;
            _lastClickIndex = _hoveredIndex;
        }
        else
        {
            _selectedIndex = -1;
        }

        _scene.SetRosterHighlight(_hoveredIndex, _selectedIndex);
    }

    private bool HasFreeSlot => _scene!.RosterCount < _scene.MaxRoster;

    private bool HandleSelectButtons(Vector2 cursor)
    {
        var y = CharacterSelectLayout.ButtonRowY(_viewportHeight);
        var hasChars = _scene!.RosterCount > 0;
        var primary = hasChars ? CharacterSelectArt.SelectButton : CharacterSelectArt.NewCharacterButton;
        var primaryKind = hasChars ? CharacterSelectButton.Select : CharacterSelectButton.NewCharacter;
        if (InButton(cursor, primary, CharacterSelectLayout.TwoButtonPrimaryX(_viewportWidth), y, primaryKind))
        {
            if (hasChars)
            {
                _audio!.Play(CharacterSelectSounds.Button);
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

        if (InButton(cursor, CharacterSelectArt.ExitButton, CharacterSelectLayout.TwoButtonQuitX(_viewportWidth), y, CharacterSelectButton.Exit))
        {
            _audio!.Play(CharacterSelectSounds.Button);
            Leave();
            return true;
        }

        if (_selectedIndex >= 0
            && InButton(cursor, CharacterSelectArt.DeleteButton, CharacterSelectLayout.TwoButtonDeleteX, y, CharacterSelectButton.Delete))
        {
            _audio!.Play(CharacterSelectSounds.Button);
            BeginDelete();
            return true;
        }

        return false;
    }

    private void BeginEnterWorld()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _scene!.RosterCount)
        {
            return;
        }

        _audio!.Play(CharacterSelectSounds.GameStart);
        _enteringSummary = _scene.RosterSummary(_selectedIndex);
        _errorMessage = string.Empty;
        _busy = Busy.Entering;
        ResetBackendCall();
        _enterTask = backend.EnterWorldAsync(_enteringSummary.Name, _backendCts!.Token);
    }

    private void PollEnterWorld()
    {
        if (_enterTask is not { IsCompleted: true })
        {
            return;
        }

        if (_enterTask.IsCompletedSuccessfully && _enterTask.Result.Ok)
        {
            var model = _classLoads.TryGetValue(_enteringSummary!.Class, out var load) && load.IsCompletedSuccessfully ? load.Result : null;
            Finish(new CharSelectEntered(_enteringSummary, _enterTask.Result, model));
            _enterTask = null;
            return;
        }

        _errorMessage = _enterTask.IsCompletedSuccessfully
            ? CharSelectText.Get("CharSelect.EnterFailure." + _enterTask.Result.Failure)
            : CharSelectText.Get("CharSelect.Unreachable");
        _enterTask = null;
        _busy = Busy.None;
        _state = State.CharacterSelect;
    }

    private void BeginDelete()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _scene!.RosterCount)
        {
            return;
        }

        _errorMessage = string.Empty;
        _busy = Busy.Deleting;
        ResetBackendCall();
        _deleteTask = backend.DeleteCharacterAsync(_scene.RosterSummary(_selectedIndex).Name, _backendCts!.Token);
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
            _audio!.Play(CharacterSelectSounds.Delete);
            BeginLoadRoster();
        }
        else
        {
            _errorMessage = CharSelectText.Get("CharSelect.DeleteFailed");
            _busy = Busy.None;
            _state = State.CharacterSelect;
        }
    }

    private void EnterRaceSelect()
    {
        _audio!.Play(CharacterSelectSounds.NewCharacter);
        ClearJobActors();
        _state = State.RaceSelect;
        _errorMessage = string.Empty;
    }

    private void UpdateRaceSelect(Vector2 cursor, bool clicked)
    {
        _raceHovered = -1;
        if (CharacterSelectLayout.TempskronMark(_viewportWidth, _viewportHeight).Contains(cursor.X, cursor.Y))
        {
            _raceHovered = 0;
        }
        else if (CharacterSelectLayout.MorionMark(_viewportWidth, _viewportHeight).Contains(cursor.X, cursor.Y))
        {
            _raceHovered = 1;
        }

        if (!clicked)
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

        HandleCreateButtons(cursor);
    }

    private void ChooseBrood(CharacterBrood brood)
    {
        _audio!.Play(CharacterSelectSounds.NewCharacter);
        _chosenBrood = brood;
        _errorMessage = string.Empty;
        foreach (var playerClass in brood == CharacterBrood.Morion ? CharacterSelectArt.MorionLineup : CharacterSelectArt.TempskronLineup)
        {
            _neededClasses.Add(playerClass);
        }

        _busy = Busy.PreparingJobLineup;
    }

    private void PollJobPreparation()
    {
        if (!ClassesPrepared())
        {
            return;
        }

        _scene!.BuildJobLineup(_chosenBrood);
        _jobHovered = -1;
        _jobSelected = -1;
        _faceIndex = 0;
        _state = State.JobSelect;
        _busy = Busy.None;
    }

    private void UpdateJobSelect(Vector2 cursor, bool clicked)
    {
        _jobHovered = _scene!.PickJob(cursor, _viewportWidth, _viewportHeight);
        _scene.SetJobHighlight(_jobHovered, _jobSelected);
        if (!clicked || HandleCreateButtons(cursor))
        {
            return;
        }

        if (_jobHovered >= 0)
        {
            var isDouble = _lastClickIndex == _jobHovered && _elapsedSeconds - _lastClickSeconds < CharacterSelectStage.DoublePressSeconds;
            _jobSelected = _jobHovered;
            _audio!.Play(CharacterSelectSounds.PickCharacter);
            if (isDouble)
            {
                EnterFaceSelect();
            }

            _lastClickSeconds = _elapsedSeconds;
            _lastClickIndex = _jobHovered;
            _scene.SetJobHighlight(_jobHovered, _jobSelected);
        }
    }

    private void EnterFaceSelect()
    {
        if (_jobSelected < 0 || _jobSelected >= _scene!.JobCount)
        {
            return;
        }

        _faceIndex = 0;
        FocusName(clear: true);
        _scene.ApplyFace(_jobSelected, _faceIndex);
        _scene.OpenFacePreview(_jobSelected, _faceIndex);
        _scene.StartForwardRun(_jobSelected);
        _scene.SetJobHighlight(-1, -1);
        _state = State.FaceSelect;
        _errorMessage = string.Empty;
    }

    private void UpdateFaceSelect(Vector2 cursor, bool clicked)
    {
        _faceHovered = -1;
        for (var index = 0; index < CharacterFace.FaceCount; index++)
        {
            if (CharacterSelectLayout.FaceThumbHit(_viewportWidth, _viewportHeight, index).Contains(cursor.X, cursor.Y))
            {
                _faceHovered = index;
                break;
            }
        }

        if (!clicked)
        {
            return;
        }

        if (_faceHovered >= 0)
        {
            if (_faceIndex != _faceHovered)
            {
                _faceIndex = _faceHovered;
                _scene!.ApplyFace(_jobSelected, _faceIndex);
            }

            return;
        }

        if (HandleCreateButtons(cursor))
        {
            return;
        }

        // A press on the name reopens the keyboard the player closed.
        if (NameBox().Contains(cursor.X, cursor.Y))
        {
            FocusName(clear: false);
        }
    }

    private ScreenRect NameBox()
    {
        var y = CharacterSelectLayout.FaceNameDecorationY(_viewportHeight);
        return new ScreenRect(_viewportWidth / 2 - 95, y - 8, 192, 32);
    }

    private bool HandleCreateButtons(Vector2 cursor)
    {
        var y = CharacterSelectLayout.ButtonRowY(_viewportHeight);
        if (InButton(cursor, CharacterSelectArt.SelectButton, CharacterSelectLayout.ThreeButtonOkX(_viewportWidth), y, CharacterSelectButton.Select))
        {
            _audio!.Play(CharacterSelectSounds.Button);
            OnCreateOk();
            return true;
        }

        if (InButton(cursor, CharacterSelectArt.BackButton, CharacterSelectLayout.ThreeButtonBackX(_viewportWidth), y, CharacterSelectButton.Back))
        {
            _audio!.Play(CharacterSelectSounds.Back);
            StepBack();
            return true;
        }

        if (InButton(cursor, CharacterSelectArt.ExitButton, CharacterSelectLayout.ThreeButtonQuitX(_viewportWidth), y, CharacterSelectButton.Exit))
        {
            _audio!.Play(CharacterSelectSounds.Button);
            Leave();
            return true;
        }

        return false;
    }

    private void OnCreateOk()
    {
        switch (_state)
        {
            case State.RaceSelect:
                _errorMessage = CharSelectText.Get("CharSelect.ChooseTribe");
                break;
            case State.JobSelect:
                if (_jobSelected >= 0)
                {
                    EnterFaceSelect();
                }
                else
                {
                    _errorMessage = CharSelectText.Get("CharSelect.ChooseClass");
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
                _state = State.CharacterSelect;
                break;
            case State.JobSelect:
                ClearJobActors();
                _state = State.RaceSelect;
                break;
            case State.FaceSelect:
            case State.Name:
                _scene!.ReturnToLineup(_jobSelected);
                _scene.CloseFacePreview();
                _jobSelected = -1;
                BlurName();
                _state = State.JobSelect;
                break;
        }

        _errorMessage = string.Empty;
    }

    private void BeginCreate()
    {
        if (_jobSelected < 0 || _jobSelected >= _scene!.JobCount)
        {
            _errorMessage = CharSelectText.Get("CharSelect.ChooseClass");
            return;
        }

        var name = Volatile.Read(ref _typedName).Trim();
        if (name.Length == 0)
        {
            _errorMessage = CharSelectText.Get("CharSelect.EnterName");
            return;
        }

        _audio!.Play(CharacterSelectSounds.GameStart);
        _errorMessage = string.Empty;
        _busy = Busy.Creating;
        ResetBackendCall();
        _createTask = backend.CreateCharacterAsync(name, _scene.JobClass(_jobSelected), _faceIndex, _backendCts!.Token);
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
            ClearJobActors();
            BlurName();
            _state = State.CharacterSelect;
            BeginLoadRoster();
        }
        else
        {
            _errorMessage = CharSelectText.Get("CharSelect.CreateFailure." + failure);
            _busy = Busy.None;
        }
    }

    private void ClearJobActors()
    {
        _scene!.ClearJobLineup();
        _jobHovered = -1;
        _jobSelected = -1;
    }

    private void FocusName(bool clear)
    {
        if (clear)
        {
            Volatile.Write(ref _typedName, "");
        }

        _nameFocused = true;
        host.SetNameInput(open: true, clear);
    }

    private void BlurName()
    {
        _nameFocused = false;
        host.SetNameInput(open: false, clear: false);
    }

    private void Leave() => Finish(null);

    private void Finish(CharSelectEntered? entered)
    {
        _finished = true;
        BlurName();
        _completion.TrySetResult(entered);
    }

    private void Draw()
    {
        var clear = CharacterSelectStage.ClearColor;
        _graphics!.BeginFrame(clear.R / 255f, clear.G / 255f, clear.B / 255f);
        _scene!.Draw(_graphics);
        _flames.Draw(_graphics, _cameraCtl);
        _view!.Draw(_viewportWidth, _viewportHeight, BuildViewModel());
    }

    private CharSelectViewModel BuildViewModel()
    {
        var scene = _scene!;
        _viewModel.StateIndex = (int)_state;
        _viewModel.ErrorMessage = _errorMessage;
        _viewModel.ElapsedSeconds = _elapsedSeconds;
        _viewModel.RosterHasChars = scene.RosterCount > 0;
        _viewModel.HasSelectedRoster = _selectedIndex >= 0;
        _viewModel.ShowNewCharacterPrompt = HasFreeSlot && (_hoverNewCharacter || scene.RosterCount == 0);
        _viewModel.Info = scene.Info(_hoveredIndex);
        _viewModel.HoveredRace = _raceHovered;
        _viewModel.JobCount = Math.Min(scene.JobCount, _viewModel.Jobs.Length);
        for (var index = 0; index < _viewModel.JobCount; index++)
        {
            _viewModel.Jobs[index] = new CharSelectJobView(scene.JobClass(index), scene.JobHeadWorld(index), index == _jobHovered, index == _jobSelected);
        }

        var facing = _state is State.FaceSelect or State.Name && _jobSelected >= 0 && _jobSelected < scene.JobCount;
        _viewModel.FaceIndex = _faceIndex;
        _viewModel.HoveredFace = _faceHovered;
        _viewModel.FaceBrood = _chosenBrood;
        _viewModel.FaceClass = facing ? scene.JobClass(_jobSelected) : default;
        _viewModel.ShowFacePortrait = facing && scene.HasFacePreview;
        _viewModel.NameDisplay = Volatile.Read(ref _typedName);
        _viewModel.CaretVisible = _nameFocused && _caretSeconds % 1.0 < 0.5;
        return _viewModel;
    }

    private bool InButton(Vector2 cursor, string art, int x, int y, CharacterSelectButton kind)
    {
        var texture = _graphics!.Art(art);
        return texture.IsValid && CharacterSelectLayout.ButtonHit(texture.Height, x, y, kind).Contains(cursor.X, cursor.Y);
    }

    private void ResetBackendCall()
    {
        _backendCts?.Cancel();
        _backendCts?.Dispose();
        _backendCts = new CancellationTokenSource(RequestTimeout);
    }

    // Stops the sounds and the pending server call; a selection that has not finished ends as left.
    public void Close()
    {
        _backendCts?.Cancel();
        _backendCts?.Dispose();
        _backendCts = null;
        _audio?.Dispose();
        _audio = null;
        _completion.TrySetResult(null);
    }
}
