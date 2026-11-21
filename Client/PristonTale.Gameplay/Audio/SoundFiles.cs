using PristonTale.Assets;
using PristonTale.Engine3D.IO;

namespace PristonTale.Gameplay.Audio;

// Where the game's sound files come from. The desktop and Android clients read them from disk; the browser client
// installs a source that lists them from its audio index and downloads each one the first time it is read.
public interface ISoundFileSource
{
    // True when a file arrives only once it is asked for: sounds then load as they are needed, not all at the start.
    bool OnDemand { get; }

    // The .wav files directly in an asset-relative folder, as '/'-separated asset-relative paths in case-insensitive
    // order; none when the folder is absent.
    IReadOnlyList<string> Waves(string assetRoot, string relativeDirectory);

    // Completes once the asset-relative file can be read under assetRoot.
    Task EnsureAsync(string assetRoot, string relativePath);
}

public static class SoundFiles
{
    public static ISoundFileSource Source { get; private set; } = DiskSoundFiles.Instance;

    public static void Use(ISoundFileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    public static IReadOnlyList<string> Waves(string assetRoot, string relativeDirectory) => Source.Waves(assetRoot, relativeDirectory);

    // A wave file's bytes with the original's header normalized. Completes synchronously on disk.
    public static async Task<byte[]> ReadAsync(string assetRoot, string relativePath)
    {
        await Source.EnsureAsync(assetRoot, relativePath).ConfigureAwait(false);
        return PristonWaveDecoder.ReadAllBytes(Path.Combine(assetRoot, relativePath));
    }
}

internal sealed class DiskSoundFiles : ISoundFileSource
{
    public static readonly DiskSoundFiles Instance = new();

    public bool OnDemand => false;

    public IReadOnlyList<string> Waves(string assetRoot, string relativeDirectory)
    {
        var directory = AssetFiles.Resolve(Path.Combine(assetRoot, relativeDirectory));
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.wav", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetRelativePath(assetRoot, path).Replace('\\', '/'))
            .ToArray();
    }

    public Task EnsureAsync(string assetRoot, string relativePath) => Task.CompletedTask;
}
