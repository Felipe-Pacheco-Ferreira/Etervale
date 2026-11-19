using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.Screens.Opening;
using PristonTale.Gameplay.UI;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

// The browser's character screen (WebCharacterSelectOverlay): the page draws the lists, panels and buttons over the 3D
// stage, so this screen draws no 2D layer, takes the page's commands in place of clicks on that layer, and reports its
// state as JSON whenever it changes. Clicks on the stage itself (a character, a class in the lineup) still pick.
public sealed partial class CharacterSelectScreen
{
    private readonly bool _webOverlay = WebCharacterSelectOverlay.Active;
    private readonly ArrayBufferWriter<byte> _webBuffer = new();
    private CharacterClassRegistry _webClasses;
    private int _webReported;
    private WebStageClickTracker _webStageClicks;

    private const float WebArrivalStagger = 0.17f;
    private const float WebArrivalLead = 0.11f;
    private bool _webRosterArrivalPlayed;
    private bool _webRosterArriving;
    private float _webRosterArrivalSeconds;
    private int _webRosterEffectsStarted;
    private int _webRosterActorsShown;
    private const float WebJobArrivalStagger = 0.055f;
    private const float WebLineupFadeOutSeconds = 0.55f;
    private const float WebLineupFadeInSeconds = 0.32f;
    private float _webLineupOpacity = 1f;
    private bool _webLineupFading;
    private Task<bool> _webFacePreparation;
    private int _webFaceToApply = -1;
    private bool _webFacePreparationFailed;
    private string _webNameToSubmit;
    private bool _webJobsArriving;
    private float _webJobArrivalSeconds;
    private int _webJobEffectsStarted;
    private int _webJobActorsShown;
    private string _webCreatedName;
    private int _webCreatedActor = -1;
    private float _webCreatedArrivalSeconds;

    private void BeginWebJobArrival()
    {
        if (Backdrop is null || _scene.JobCount == 0)
        {
            return;
        }

        _webJobsArriving = true;
        _webJobArrivalSeconds = 0f;
        _webJobEffectsStarted = 0;
        _webJobActorsShown = 0;
        for (var index = 0; index < _scene.JobCount; index++)
        {
            _scene.SetJobHidden(index, true);
        }
    }

    private void FinishWebJobArrival()
    {
        if (!_webJobsArriving)
        {
            return;
        }

        for (var index = _webJobActorsShown; index < _scene.JobCount; index++)
        {
            _scene.SetJobHidden(index, false);
        }
        _webJobsArriving = false;
    }

    private void PlayWebCreatedArrival(int index)
    {
        if (Backdrop is null)
        {
            return;
        }

        _webCreatedActor = index;
        _webCreatedArrivalSeconds = -1f;
        _scene.SetRosterHidden(index, true);
    }

    private void BeginWebLineupFade() => _webLineupFading = Backdrop is not null;

    private void UpdateWebLineupFade(float dt)
    {
        if (!_webLineupFading)
        {
            return;
        }

        var showLineup = _state is not State.FaceSelect and not State.Name;
        var speed = showLineup ? 1f / WebLineupFadeInSeconds : -1f / WebLineupFadeOutSeconds;
        _webLineupOpacity = Math.Clamp(_webLineupOpacity + speed * dt, 0f, 1f);
        var opacity = MathHelper.SmoothStep(0f, 1f, _webLineupOpacity);
        var flash = MathF.Sin(_webLineupOpacity * MathHelper.Pi) * 0.55f;
        for (var index = 0; index < _scene.JobCount; index++)
        {
            if (index != _jobSelected)
            {
                _scene.SetWebJobAppearance(index, opacity, flash);
            }
        }

        _webLineupFading = opacity != (showLineup ? 1f : 0f);
    }

    private async Task<bool> PrepareWebFacesAsync(int index, CancellationToken cancellationToken)
    {
        try
        {
            await _scene.PrepareJobFacesAsync(index, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            Context.Log.Error("[CharSelect] face preparation failed", exception);
            return false;
        }
    }

    private void UpdateWebFacePreparation()
    {
        if (_webFacePreparation is not { IsCompletedSuccessfully: true })
        {
            return;
        }

        var ready = _webFacePreparation.Result;
        _webFacePreparationFailed = !ready;
        _webFacePreparation = null;
        if (_webFaceToApply >= 0 && _state is State.FaceSelect or State.Name)
        {
            if (ready)
            {
                ApplyWebFace(_webFaceToApply);
            }
            else
            {
                _faceIndex = 0;
                _errorMessage = ClientText.Get("CharSelect.ModelsFailed");
            }
        }

        _webFaceToApply = -1;
        var name = _webNameToSubmit;
        _webNameToSubmit = null;
        if (ready && name is not null && _state is State.FaceSelect or State.Name)
        {
            SubmitWebCharacter(name);
        }
    }

    private void ApplyWebFace(int index)
    {
        _scene.ApplyFace(_jobSelected, index);
        Backdrop?.SpawnCharacter(_scene.PreviewFootWorld(_jobSelected));
    }

    private void UpdateWebClassSelection()
    {
        var leavingLineup = (_state is State.FaceSelect or State.Name) && _webLineupOpacity > 0.65f;
        var inLineup = _state == State.JobSelect && (!_webJobsArriving || _jobSelected < _webJobActorsShown);
        var markActive = _jobSelected >= 0 && (inLineup || leavingLineup);
        Backdrop?.SetClassSelection(markActive ? _scene.PreviewFootWorld(_jobSelected) : null);
    }

    private void UpdateWebCreationEffects(float dt)
    {
        UpdateWebLineupFade(dt);
        if (_webCreatedActor >= 0 && _cameraCtl.HasReached((int)State.CharacterSelect))
        {
            if (_webCreatedArrivalSeconds < 0f)
            {
                Backdrop.SpawnCharacter(_scene.RosterFootWorld(_webCreatedActor));
                _webCreatedArrivalSeconds = 0f;
            }

            _webCreatedArrivalSeconds += dt;
            if (_webCreatedArrivalSeconds >= WebArrivalLead)
            {
                _scene.SetRosterHidden(_webCreatedActor, false);
                _webCreatedActor = -1;
            }
        }

        if (!_webJobsArriving)
        {
            return;
        }

        _webJobArrivalSeconds += dt;
        var count = _scene.JobCount;
        while (_webJobEffectsStarted < count && _webJobArrivalSeconds >= _webJobEffectsStarted * WebJobArrivalStagger)
        {
            Backdrop.SpawnCharacter(_scene.JobFootWorld(_webJobEffectsStarted));
            _webJobEffectsStarted++;
        }

        while (_webJobActorsShown < count && _webJobArrivalSeconds >= WebArrivalLead + _webJobActorsShown * WebJobArrivalStagger)
        {
            _scene.SetJobHidden(_webJobActorsShown, false);
            _webJobActorsShown++;
        }

        _webJobsArriving = _webJobActorsShown < count;
    }

    private void SetWebRosterHidden(bool hidden)
    {
        for (var index = 0; index < _scene.RosterCount; index++)
        {
            _scene.SetRosterHidden(index, hidden);
        }
    }

    private void BeginWebRosterArrival()
    {
        if (Backdrop is null || _webRosterArrivalPlayed || _scene.RosterCount == 0)
        {
            return;
        }

        _webRosterArrivalPlayed = true;
        _webRosterArriving = true;
        SetWebRosterHidden(true);
    }

    private void UpdateWebRosterArrival(float dt)
    {
        if (!_webRosterArriving)
        {
            return;
        }

        _webRosterArrivalSeconds += dt;
        var count = _scene.RosterCount;
        while (_webRosterEffectsStarted < count && _webRosterArrivalSeconds >= _webRosterEffectsStarted * WebArrivalStagger)
        {
            Backdrop.SpawnCharacter(_scene.RosterFootWorld(_webRosterEffectsStarted));
            _webRosterEffectsStarted++;
        }

        while (_webRosterActorsShown < count && _webRosterArrivalSeconds >= WebArrivalLead + _webRosterActorsShown * WebArrivalStagger)
        {
            _scene.SetRosterHidden(_webRosterActorsShown, false);
            _webRosterActorsShown++;
        }

        _webRosterArriving = _webRosterActorsShown < count;
    }

    private void UpdateWeb(MouseState mouse)
    {
        while (WebCharacterSelectOverlay.TryTake(out var command))
        {
            if (_busy == Busy.None && !Context.Presentation.AwaitingConfirmation)
            {
                RunWebCommand(command);
            }
            else if (WebCharacterSelectOverlay.ProfileStageInput)
            {
                Context.Log.Info(FormattableString.Invariant($"[StagePick] discard command={command.Kind} eventMs={command.ClickSeconds * 1000:F3} ")
                    + $"state={_state} busy={_busy} awaitingConfirmation={Context.Presentation.AwaitingConfirmation}");
            }
        }

        if (_busy == Busy.None && !Context.Presentation.AwaitingConfirmation)
        {
            PickOnStage(mouse);
        }
    }

    private void RunWebCommand(WebCharacterCommand command)
    {
        if (command.Kind != WebCharacterCommandKind.PickOnStage)
        {
            ResetWebStagePick("command", command.Kind);
        }

        var stateBefore = _state;

        switch (command.Kind)
        {
            case WebCharacterCommandKind.Select when _state == State.CharacterSelect && command.Index >= 0 && command.Index < _scene.RosterCount:
                _selectedIndex = command.Index;
                _audio.Play(LoginCue.PickCharacter);
                _scene.SetRosterHighlight(-1, _selectedIndex);
                break;
            case WebCharacterCommandKind.Enter when _state == State.CharacterSelect:
                BeginEnterWorld();
                break;
            case WebCharacterCommandKind.Delete when _state == State.CharacterSelect:
                _audio.Play(LoginCue.Button);
                BeginDelete();
                break;
            case WebCharacterCommandKind.NewCharacter when _state == State.CharacterSelect && HasFreeSlot:
                EnterRaceSelect();
                break;
            case WebCharacterCommandKind.Tribe when _state == State.RaceSelect:
                ChooseBrood(command.Index == 1 ? CharacterBrood.Morion : CharacterBrood.Tempskron);
                break;
            case WebCharacterCommandKind.PickOnStage:
                PickWebStage(command.Stage, command.StagePosition, command.ClickSeconds);
                break;
            case WebCharacterCommandKind.Class when _state == State.JobSelect && command.Index >= 0 && command.Index < _scene.JobCount:
                SelectWebJob(command.Index);
                break;
            case WebCharacterCommandKind.Next when _state == State.JobSelect:
                OnCreateOk();
                break;
            case WebCharacterCommandKind.Face when _state is State.FaceSelect or State.Name
                && command.Index >= 0 && command.Index < CharSelectLayout.FacesPerJob && command.Index != _faceIndex:
                if (_webFacePreparationFailed && command.Index != 0)
                {
                    _errorMessage = ClientText.Get("CharSelect.ModelsFailed");
                    break;
                }

                _errorMessage = string.Empty;
                _faceIndex = command.Index;
                if (_webFacePreparation is not null && _faceIndex != 0)
                {
                    _webFaceToApply = _faceIndex;
                }
                else
                {
                    _webFaceToApply = -1;
                    ApplyWebFace(_faceIndex);
                }
                break;
            case WebCharacterCommandKind.Create when _state is State.FaceSelect or State.Name:
                if (_webFaceToApply >= 0)
                {
                    _webNameToSubmit = command.Text;
                }
                else
                {
                    SubmitWebCharacter(command.Text);
                }
                break;
            case WebCharacterCommandKind.Back when _state != State.CharacterSelect:
                _audio.Play(LoginCue.Back);
                StepBack();
                break;
            case WebCharacterCommandKind.Exit:
                _audio.Play(LoginCue.Button);
                Context.RequestScreen(new OpeningScreen(Context));
                break;
        }
        if (WebCharacterSelectOverlay.ProfileStageInput && command.Kind != WebCharacterCommandKind.PickOnStage)
        {
            Context.Log.Info($"[StagePick] command={command.Kind} state={stateBefore}->{_state} busy={_busy} tribe={_chosenBrood} selectedClass={_jobSelected}");
        }
    }

    private void ResetWebStagePick(string reason, WebCharacterCommandKind command = WebCharacterCommandKind.PickOnStage)
    {
        if (WebCharacterSelectOverlay.ProfileStageInput)
        {
            var previous = _webStageClicks.Snapshot;
            Context.Log.Info($"[StagePick] reset reason={reason} command={command} state={_state} busy={_busy} pending={previous.Pending} priorIndex={previous.Index}");
        }
        _webStageClicks.Reset();
    }

    private void SubmitWebCharacter(string name)
    {
        _nameField.Set(name);
        BeginCreate();
    }

    private void SelectWebJob(int index)
    {
        if (_jobSelected != index)
        {
            _jobSelected = index;
            _scene.StepWebJob(index);
            if (_webLineupFading)
            {
                _scene.SetWebJobAppearance(index, 1f, 0f);
            }
        }

        _scene.SetJobHighlight(-1, _jobSelected);
        _audio.Play(LoginCue.PickCharacter);
    }

    private void PickWebStage(WebCharacterStage stage, Vector2 position, double clickSeconds)
    {
        var roster = stage == WebCharacterStage.Roster;
        if (roster ? _state != State.CharacterSelect : _state != State.JobSelect)
        {
            ResetWebStagePick("phase-mismatch");
            return;
        }
        var stateBefore = _state;
        var viewport = Context.GraphicsDevice.Viewport;
        var cursor = new Vector2(viewport.X + position.X * viewport.Width, viewport.Y + position.Y * viewport.Height);
        var hit = roster ? _scene.PickRoster(cursor, viewport, PickScale) : _scene.PickWebJob(cursor, viewport);
        var selectedBefore = roster ? _selectedIndex : _jobSelected;
        var previous = WebCharacterSelectOverlay.ProfileStageInput ? _webStageClicks.Snapshot : default;
        var confirm = _webStageClicks.Pick(hit, selectedBefore, clickSeconds, stage);
        if (hit >= 0)
        {
            if (roster)
            {
                _selectedIndex = hit;
                _audio.Play(LoginCue.PickCharacter);
                _scene.SetRosterHighlight(-1, _selectedIndex);
                if (confirm)
                {
                    BeginEnterWorld();
                }
            }
            else
            {
                SelectWebJob(hit);
                if (confirm)
                {
                    OnCreateOk();
                }
            }
        }
        if (WebCharacterSelectOverlay.ProfileStageInput)
        {
            var deltaMs = previous.Pending ? (clickSeconds - previous.Seconds) * 1000 : double.NaN;
            Context.Log.Info(FormattableString.Invariant($"[StagePick] pick stage={stage} eventMs={clickSeconds * 1000:F3} hit={hit} selectedBefore={selectedBefore} confirm={confirm} ")
                + FormattableString.Invariant($"pending={previous.Pending} priorIndex={previous.Index} priorStage={previous.Stage} deltaMs={deltaMs:F3} maxMs={WebStageClickTracker.DoublePressSeconds * 1000:F3} ")
                + FormattableString.Invariant($"x={position.X:F5} y={position.Y:F5} viewport={viewport.Width}x{viewport.Height} state={stateBefore}->{_state} busy={_busy} tribe={_chosenBrood} selectedClass={_jobSelected}"));
        }
    }

    // The page queues stage clicks with their event times. The polled pointer handles hover and preview dragging.
    private void PickOnStage(MouseState mouse)
    {
        var cursor = new Vector2(mouse.X, mouse.Y);
        var viewport = Context.GraphicsDevice.Viewport;
        if (_state == State.CharacterSelect)
        {
            _hoveredIndex = _scene.PickRoster(cursor, viewport, PickScale);
            _scene.SetRosterHighlight(_hoveredIndex, _selectedIndex);
        }
        else if (_state == State.JobSelect)
        {
            _jobHovered = _scene.PickWebJob(cursor, viewport);
            _scene.SetJobHighlight(_jobHovered, _jobSelected);
        }
        else if ((_state is State.FaceSelect or State.Name) && mouse.LeftButton == ButtonState.Pressed
            && _lastMouse.LeftButton == ButtonState.Pressed)
        {
            _scene.RotateForwardJob((mouse.X - _lastMouse.X) * 0.012f);
        }
    }

    // Reports the state when something the page shows changed; nothing is built on the other frames.
    private void ReportWeb()
    {
        if (Backdrop is not null &&
            (!_webRosterArrivalPlayed && _busy is Busy.LoadingRoster or Busy.PreparingRoster ||
             _webRosterArriving && _webRosterActorsShown == 0 || _webJobsArriving && _webJobActorsShown == 0))
        {
            return;
        }

        var version = HashCode.Combine(_state, _busy, _selectedIndex, _jobSelected, _faceIndex, _scene.RosterCount, _scene.JobCount, _chosenBrood);
        version = HashCode.Combine(version, _errorMessage);
        if (version == _webReported)
        {
            return;
        }

        _webReported = version;
        WebCharacterSelectOverlay.Show(WebStateJson());
    }

    private string WebStateJson()
    {
        _webClasses ??= new CharacterClassRegistry();
        _webBuffer.Clear();
        using (var json = new Utf8JsonWriter(_webBuffer))
        {
            json.WriteStartObject();
            json.WriteString("screen", _state switch
            {
                State.RaceSelect => "tribe",
                State.JobSelect => "class",
                State.FaceSelect or State.Name => "appearance",
                _ => "roster"
            });
            json.WriteString("busy", _busy switch
            {
                Busy.LoadingRoster or Busy.PreparingRoster => "loading",
                Busy.PreparingJobLineup => "preparing",
                Busy.Entering => "entering",
                Busy.Deleting => "deleting",
                Busy.Creating => "creating",
                _ => ""
            });
            json.WriteString("message", _errorMessage ?? string.Empty);
            json.WriteNumber("max", _scene.MaxRoster);
            json.WriteNumber("selected", _selectedIndex);
            json.WriteStartArray("roster");
            for (var index = 0; index < _scene.RosterCount; index++)
            {
                var summary = _scene.RosterSummary(index);
                if (!Enum.IsDefined(summary.Class))
                {
                    continue;
                }

                json.WriteStartObject();
                json.WriteString("name", summary.Name);
                WriteClass(json, summary.Class, summary.Tier);
                json.WriteNumber("level", summary.Level);
                json.WriteString("clan", summary.ClanName ?? string.Empty);
                json.WriteStartObject("equipment");
                json.WriteString("weapon", summary.WeaponKey);
                json.WriteString("armor", summary.ArmorKey);
                json.WriteString("offHand", summary.OffHandKey);
                json.WriteEndObject();
                var faces = CharacterSelectArt.Faces(summary.Class);
                json.WriteString("face", FaceAddress(faces.Directory, faces.Normal[Math.Clamp(summary.FaceIndex, 0, faces.Normal.Count - 1)]));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteString("tribe", _state is State.JobSelect or State.FaceSelect or State.Name ? _chosenBrood.ToString() : string.Empty);
            json.WriteBoolean("retainedLineup", _state == State.RaceSelect && _scene.JobCount > 0);
            json.WriteNumber("job", _jobSelected);
            json.WriteStartArray("jobs");
            for (var index = 0; index < _scene.JobCount; index++)
            {
                var playerClass = _scene.JobClass(index);
                json.WriteStartObject();
                WriteClass(json, playerClass, 0);
                var attributes = _webClasses.Get(playerClass).Definition.BaseAttributes;
                json.WriteStartArray("stats");
                json.WriteNumberValue(attributes.Strength);
                json.WriteNumberValue(attributes.Spirit);
                json.WriteNumberValue(attributes.Talent);
                json.WriteNumberValue(attributes.Agility);
                json.WriteNumberValue(attributes.Health);
                json.WriteEndArray();
                var faces = CharacterSelectArt.Faces(playerClass);
                json.WriteStartArray("faces");
                foreach (var face in faces.Normal)
                {
                    json.WriteStringValue(FaceAddress(faces.Directory, face));
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteNumber("face", _faceIndex);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(_webBuffer.WrittenSpan);
    }

    private void WriteClass(Utf8JsonWriter json, PlayerClass playerClass, int tier)
    {
        json.WriteString("cls", playerClass.ToString());
        json.WriteString("title", DataText.ClassTitle(playerClass, tier));
        json.WriteString("brood", _webClasses.Get(playerClass).Definition.Brood.ToString());
    }

    // The thumbnail's address under the client's folder, where the publish puts the asset files.
    private static string FaceAddress(string directory, string file) => "assets/" + directory + "/" + file;

    private static void CloseWeb() => WebCharacterSelectOverlay.Show("{\"screen\":\"closed\"}");
}
