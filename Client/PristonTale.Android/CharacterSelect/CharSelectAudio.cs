using Android.Content;
using Android.Content.Res;
using Android.Media;
using PristonTale.Assets;

namespace PristonTale.Mobile.CharacterSelect;

// The screen's music, loops and cues (CharacterSelectSounds), as the desktop LoginAudio plays them: the music at 0.6
// and each loop at its own volume, all fading in over half a second.
internal sealed class CharSelectAudio : IDisposable
{
    private const string Directory = "CharSelect";
    private const string MusicAsset = Directory + "/music.wav";
    private const float MusicVolume = 0.6f;
    private const float FadeSeconds = 0.5f;

    private readonly List<(MediaPlayer Player, float Volume)> _loops = [];
    private readonly SoundPool _pool;
    private readonly Dictionary<string, int> _cues = new();
    private float _fade;
    private bool _paused;

    public CharSelectAudio(Context context)
    {
        using var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Game)!.SetContentType(AudioContentType.Sonification)!.Build()!;
        _pool = new SoundPool.Builder().SetMaxStreams(4)!.SetAudioAttributes(attributes)!.Build()!;
        Loop(context, MusicAsset, MusicVolume);
        foreach (var (path, volume) in CharacterSelectSounds.Loops)
        {
            Loop(context, AssetName(path), volume);
        }

        foreach (var cue in CharacterSelectSounds.Cues)
        {
            using var descriptor = Open(context, AssetName(cue));
            if (descriptor is not null)
            {
                _cues[cue] = _pool.Load(descriptor, 1);
            }
        }
    }

    private static string AssetName(string sourcePath) => $"{Directory}/{CharacterSelectSounds.PackagedName(sourcePath)}";

    private static AssetFileDescriptor? Open(Context context, string asset)
    {
        try
        {
            return context.Assets!.OpenFd(asset);
        }
        catch (Java.IO.IOException)
        {
            return null;
        }
    }

    private void Loop(Context context, string asset, float volume)
    {
        using var descriptor = Open(context, asset);
        if (descriptor is null)
        {
            return;
        }

        var player = new MediaPlayer();
        try
        {
            player.SetDataSource(descriptor.FileDescriptor, descriptor.StartOffset, descriptor.Length);
            player.Looping = true;
            player.SetVolume(0f, 0f);
            player.Prepare();
            player.Start();
            _loops.Add((player, volume));
        }
        catch (Java.IO.IOException)
        {
            player.Release();
        }
    }

    public void Update(float dt)
    {
        if (_fade >= 1f)
        {
            return;
        }

        _fade = Math.Min(1f, _fade + dt / FadeSeconds);
        foreach (var (player, volume) in _loops)
        {
            player.SetVolume(volume * _fade, volume * _fade);
        }
    }

    // A cue by its CharacterSelectSounds path.
    public void Play(string cue)
    {
        if (!_paused && _cues.TryGetValue(cue, out var id))
        {
            _pool.Play(id, 1f, 1f, 1, 0, 1f);
        }
    }

    public void Pause()
    {
        _paused = true;
        foreach (var (player, _) in _loops)
        {
            player.Pause();
        }
    }

    public void Resume()
    {
        _paused = false;
        foreach (var (player, _) in _loops)
        {
            player.Start();
        }
    }

    public void Dispose()
    {
        foreach (var (player, _) in _loops)
        {
            player.Stop();
            player.Release();
        }

        _loops.Clear();
        _pool.Release();
    }
}
