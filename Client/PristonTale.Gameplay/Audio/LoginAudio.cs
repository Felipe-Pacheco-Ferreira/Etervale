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
    private float _fade;

    private LoginAudio(PristonAudioLibrary library) => _library = library;

    public static LoginAudio Opening(PristonAudioLibrary library) =>
        Start(library, library?.OpeningMusic, []);

    public static LoginAudio CharacterSelect(PristonAudioLibrary library) =>
        Start(library, library?.CharacterSelectMusic, library?.CharacterSelectLoops ?? []);

    private static LoginAudio Start(
        PristonAudioLibrary library,
        SoundEffect music,
        IReadOnlyList<(SoundEffect Sound, float Volume)> loops)
    {
        var audio = new LoginAudio(library);
        audio.Loop(music, FieldAudioController.MusicVolume);
        foreach (var (sound, volume) in loops)
        {
            audio.Loop(sound, volume);
        }

        return audio;
    }

    private void Loop(SoundEffect sound, float volume)
    {
        if (sound is null)
        {
            return;
        }

        var instance = sound.CreateInstance();
        instance.IsLooped = true;
        instance.Volume = 0f;
        instance.Play();
        _loops.Add((instance, volume));
    }

    public void Update(float dt)
    {
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
    }
}
