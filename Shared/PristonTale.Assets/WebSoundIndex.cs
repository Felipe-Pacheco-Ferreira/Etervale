namespace PristonTale.Assets;

// audio.txt, beside bootstrap.txt: the sound files the browser client downloads one at a time, the first time the game
// reads each, in bootstrap.txt's "path<TAB>SHA-256" form with every path under assets/. Lookups take asset-relative
// paths ('/' or '\' separated) and ignore case, as AssetFiles does.
public sealed class WebSoundIndex
{
    public const string FileName = "audio.txt";
    private const string AssetsFolder = "assets/";
    private const string WaveExtension = ".wav";

    private readonly Dictionary<string, (string Path, string Hash)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _waves = new(StringComparer.OrdinalIgnoreCase);

    private WebSoundIndex()
    {
    }

    public static WebSoundIndex Empty { get; } = new();

    public int Count => _files.Count;

    // Null when a line is malformed (ReadBootstrap's rules) or a path is outside assets/.
    public static WebSoundIndex? Read(byte[] bytes)
    {
        var entries = WebAssetPacks.ReadBootstrap(WebAssetPacks.BootstrapLines(bytes));
        if (entries is null)
        {
            return null;
        }

        var index = new WebSoundIndex();
        foreach (var (path, hash) in entries)
        {
            if (!path.StartsWith(AssetsFolder, StringComparison.Ordinal) || path.Length == AssetsFolder.Length)
            {
                return null;
            }

            var relative = path[AssetsFolder.Length..];
            index._files.Add(relative, (path, hash));
            var slash = relative.LastIndexOf('/');
            if (relative.EndsWith(WaveExtension, StringComparison.OrdinalIgnoreCase))
            {
                var directory = slash < 0 ? string.Empty : relative[..slash];
                (index._waves.TryGetValue(directory, out var waves) ? waves : index._waves[directory] = []).Add(relative);
            }
        }

        foreach (var waves in index._waves.Values)
        {
            waves.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return index;
    }

    // The listed file of an asset-relative path: its client path (under assets/, with the index's spelling) and hash.
    public bool TryFind(string relativePath, out string path, out string hash)
    {
        if (_files.TryGetValue(Normalize(relativePath), out var entry))
        {
            (path, hash) = entry;
            return true;
        }

        path = string.Empty;
        hash = string.Empty;
        return false;
    }

    // The .wav files directly in an asset-relative folder, as asset-relative paths in case-insensitive order.
    public IReadOnlyList<string> Waves(string relativeDirectory) =>
        _waves.TryGetValue(Normalize(relativeDirectory), out var waves) ? waves : [];

    private static string Normalize(string relativePath) => relativePath.Replace('\\', '/').Trim('/');
}
