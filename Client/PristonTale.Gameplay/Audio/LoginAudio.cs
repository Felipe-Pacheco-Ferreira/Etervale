using Microsoft.Xna.Framework.Audio;
using PristonTale.Assets;

namespace PristonTale.Gameplay.Audio;

public enum LoginCue
{
    Button,
    NewCharacter,
    Delete,
    Back,
    PickCharacter,
    GameStart,
    Login
}

// The music, loops and cues of the login and character screens. As in the original they ignore the music and sound
// options, which belong to the world. Loops fade in over the original's 35 steps of 10 at 70 ticks per second.
public sealed class LoginAudio : IDisposable
{
    internal const float FadeSeconds = 0.5f;

    internal static readonly (LoginCue Cue, string Path)[] CueFiles =
    [
        (LoginCue.Button, CharacterSelectSounds.Button),
        (LoginCue.NewCharacter, CharacterSelectSounds.NewCharacter),
        (LoginCue.Delete, CharacterSelectSounds.Delete),
        (LoginCue.Back, CharacterSelectSounds.Back),
        (LoginCue.PickCharacter, CharacterSelectSounds.PickCharacter),
        (LoginCue.GameStart, CharacterSelectSounds.GameStart),
        (LoginCue.Login, "wav/Login.wav")
    ];

    internal static IReadOnlyList<(string Path, float Volume)> CharacterSelectLoopFiles => CharacterSelectSounds.Loops;

    private readonly PristonAudioLibrary _library;
    private readonly List<(SoundEffectInstance Instance, float Volume)> _loops = [];
    // Loops whose file is still arriving (the browser): each starts, at the fade's current volume, once it is there.
    private readonly List<(string Path, float Volume)> _waiting = [];
    private float _fade;

    private LoginAudio(PristonAudioLibrary library) => _library = library;

    public static LoginAudio Opening(PristonAudioLibrary library) => Start(library, library?.OpeningLoops ?? []);

    public static LoginAudio CharacterSelect(PristonAudioLibrary library) => Start(library, library?.CharacterSelectLoops ?? []);

    private static LoginAudio Start(PristonAudioLibrary library, IReadOnlyList<(string Path, float Volume)> loops)
    {
        var audio = new LoginAudio(library);
        audio._waiting.AddRange(loops);
        audio.StartWaiting();
        return audio;
    }

    private void StartWaiting()
    {
        for (var index = _waiting.Count - 1; index >= 0; index--)
        {
            var (path, volume) = _waiting[index];
            if (_library.LoginSound(path) is not { } sound)
            {
                continue;
            }

            _waiting.RemoveAt(index);
            var instance = sound.CreateInstance();
            instance.IsLooped = true;
            instance.Volume = volume * _fade;
            instance.Play();
            _loops.Add((instance, volume));
        }
    }

    public void Update(float dt)
    {
        if (_waiting.Count > 0)
        {
            StartWaiting();
        }

        if (_fade >= 1f)
        {
            return;
        }

        _fade = Math.Min(1f, _fade + dt / FadeSeconds);
        foreach (var (instance, volume) in _loops)
        {
            instance.Volume = volume * _fade;
        }
    }

    public void Play(LoginCue cue) => _library?.Cue(cue)?.Play();

    public void Dispose()
    {
        foreach (var (instance, _) in _loops)
        {
            instance.Stop();
            instance.Dispose();
        }

        _loops.Clear();
        _waiting.Clear();
    }
}
