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

    private const float WebArrivalStagger = 0.17f;
    private const float WebArrivalLead = 0.11f;
    private bool _webRosterArrivalPlayed;
    private bool _webRosterArriving;
    private float _webRosterArrivalSeconds;
    private int _webRosterEffectsStarted;
    private int _webRosterActorsShown;
    private const float WebJobArrivalStagger = 0.055f;
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

    private void UpdateWebCreationEffects(float dt)
    {
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
        }

        if (_busy == Busy.None && !Context.Presentation.AwaitingConfirmation)
        {
            PickOnStage(mouse);
        }
    }

    private void RunWebCommand(WebCharacterCommand command)
    {
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
            case WebCharacterCommandKind.Class when _state == State.JobSelect && command.Index >= 0 && command.Index < _scene.JobCount:
                _jobSelected = command.Index;
                _audio.Play(LoginCue.PickCharacter);
                _scene.SetJobHighlight(-1, _jobSelected);
                break;
            case WebCharacterCommandKind.Next when _state == State.JobSelect:
                OnCreateOk();
                break;
            case WebCharacterCommandKind.Face when _state is State.FaceSelect or State.Name
                && command.Index >= 0 && command.Index < CharSelectLayout.FacesPerJob && command.Index != _faceIndex:
                _faceIndex = command.Index;
                _scene.ApplyFace(_jobSelected, _faceIndex);
                Backdrop?.SpawnCharacter(_scene.PreviewFootWorld(_jobSelected));
                break;
            case WebCharacterCommandKind.Create when _state is State.FaceSelect or State.Name:
                _nameField.Set(command.Text);
                BeginCreate();
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
    }

    // A click on the stage: a character of the list (a second click enters the world) or a class of the lineup.
    private void PickOnStage(MouseState mouse)
    {
        var cursor = new Vector2(mouse.X, mouse.Y);
        var viewport = Context.GraphicsDevice.Viewport;
        if (_state == State.CharacterSelect)
        {
            _hoveredIndex = _scene.PickRoster(cursor, viewport, PickScale);
            _scene.SetRosterHighlight(_hoveredIndex, _selectedIndex);
            if (Clicked(mouse) && _hoveredIndex >= 0)
            {
                var again = _hoveredIndex == _selectedIndex && _lastClickIndex == _hoveredIndex
                    && _elapsedSeconds - _lastClickSeconds < CharacterSelectStage.DoublePressSeconds;
                _selectedIndex = _hoveredIndex;
                _audio.Play(LoginCue.PickCharacter);
                _lastClickSeconds = _elapsedSeconds;
                _lastClickIndex = _hoveredIndex;
                if (again)
                {
                    BeginEnterWorld();
                }
            }
        }
        else if (_state == State.JobSelect)
        {
            _jobHovered = _scene.PickJob(cursor, viewport, PickScale);
            _scene.SetJobHighlight(_jobHovered, _jobSelected);
            if (Clicked(mouse) && _jobHovered >= 0)
            {
                var again = _jobSelected == _jobHovered && _lastClickIndex == _jobHovered
                    && _elapsedSeconds - _lastClickSeconds < CharacterSelectStage.DoublePressSeconds;
                _jobSelected = _jobHovered;
                _audio.Play(LoginCue.PickCharacter);
                _lastClickIndex = _jobHovered;
                _lastClickSeconds = _elapsedSeconds;
                if (again)
                {
                    EnterFaceSelect();
                }
            }
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
