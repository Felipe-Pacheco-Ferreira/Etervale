using PristonTale.Contracts.Characters;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Browser;

// The files the game reads before it enters a field, listed in bootstrap.txt by the publish (EtervaleBootstrapFiles in
// the project). They are fetched from the site and written to the runtime's in-memory file system under the
// application's base directory, where MonoGame's content and AssetFiles read them as on the desktop. They come in three
// waves: the opening scene before the game starts, the existing character bootstrap and arrival effect during the
// opening, and the rest after a character enters the world. The class-specific models are loaded separately on demand.
internal static class BootstrapFiles
{
    private const string ListFile = "bootstrap.txt";
    private const string CharacterListFile = "character-previews.txt";
    private const string AssetsFolder = "assets";
    private const string CharacterFolder = AssetsFolder + "/char/tmABCD/";
    private const string SkyManifest = AssetsFolder + "/" + PristonSkyManifest.AssetPath;

    private static readonly string[] ScenePrefixes =
    [
        "Content/",
        "assets/image/Sinimage/Cursor/",
        "assets/image/flame"
    ];

    private static readonly HashSet<string> SceneTextures = new(StringComparer.OrdinalIgnoreCase)
    {
        "assets/Field/forest/badak_panja_2.bmp",
        "assets/Field/forest/stairtop.bmp",
        "assets/Field/forest/stone-34.bmp",
        "assets/Field/forest/stone-35.bmp",
        "assets/Field/forest/dirt.bmp",
        "assets/Field/forest/Grass.bmp",
        "assets/Field/forest/bark01.bmp",
        "assets/Field/forest/t_nwall-04.bmp",
        "assets/Field/forest/avalon-banner-emblem.png",
    };

    private static readonly HashSet<string> RosterEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        "assets/Effect/Particle/warp_magicsquare01a.tga",
        "assets/Effect/Particle/flare.tga",
    };

    private static Uri? _clientAddress;
    private static string[] _remainingPaths = [];
    private static string[] _deferredPaths = [];
    private static readonly SemaphoreSlim CharacterFetch = new(1);
    private static Dictionary<PlayerClass, List<string>>? _characterPaths;

    private static string Root => AppContext.BaseDirectory;

    public static string AssetRoot => Path.Combine(Root, AssetsFolder);

    public static async Task FetchAsync(Uri clientAddress)
    {
        using var http = new HttpClient { BaseAddress = clientAddress };
        var list = await FetchWithRetryAsync(() => http.GetStringAsync(ListFile));
        var paths = list.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _clientAddress = clientAddress;
        var manifestEntry = paths.FirstOrDefault(entry => SkyManifest.Equals(EntryPath(entry), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"{ListFile} is missing {SkyManifest}.");
        await FetchFileAsync(http, manifestEntry);
        var skyFolders = OpeningSkyFolders(Target(EntryPath(manifestEntry)));
        var scenePaths = new List<string>();
        var remainingPaths = new List<string>();
        var deferredPaths = new List<string>();
        foreach (var entry in paths)
        {
            if (entry == manifestEntry)
            {
                continue;
            }

            var path = EntryPath(entry);
            if (IsScene(path, skyFolders))
            {
                scenePaths.Add(entry);
            }
            else if (IsRosterAsset(path))
            {
                remainingPaths.Add(entry);
            }
            else
            {
                deferredPaths.Add(entry);
            }
        }

        _remainingPaths = remainingPaths.ToArray();
        _deferredPaths = deferredPaths.ToArray();
        await FetchPathsAsync(http, scenePaths, BrowserHost.ReportLoginProgress);
    }

    private static string[] OpeningSkyFolders(string path)
    {
        var manifest = PristonSkyManifest.Read(path);
        return manifest[PristonSkyProfiles.Forest.Night]
            // Sky materials load their textures relative to their model directory.
            .Select(layer => AssetsFolder + "/" + layer.ModelPath[..(layer.ModelPath.LastIndexOf('/') + 1)])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // Runs while the opening plays, so it keeps few requests in flight: each finished file is written on the game's own
    // thread, between its frames.
    public static async Task FetchRemainingAsync()
    {
        using var http = new HttpClient { BaseAddress = _clientAddress };
        await FetchPathsAsync(http, _remainingPaths, BrowserHost.ReportLoginProgress, BackgroundFetches);
    }

    public static async Task FetchWorldAssetsAsync()
    {
        using var http = new HttpClient { BaseAddress = _clientAddress };
        await FetchPathsAsync(http, _deferredPaths);
    }

    public static async Task FetchCharacterAssetsAsync(IReadOnlyList<PlayerClass> classes, CancellationToken cancellationToken)
    {
        if (classes.Count == 0)
        {
            return;
        }

        await CharacterFetch.WaitAsync(cancellationToken);
        try
        {
            using var http = new HttpClient { BaseAddress = _clientAddress };
            _characterPaths ??= ReadCharacterPaths(await FetchWithRetryAsync(
                () => http.GetStringAsync(CharacterListFile, cancellationToken), cancellationToken));
            var entries = new HashSet<string>(StringComparer.Ordinal);
            foreach (var characterClass in classes)
            {
                if (!_characterPaths.TryGetValue(characterClass, out var paths))
                {
                    throw new InvalidDataException($"{CharacterListFile} is missing files for {characterClass}.");
                }

                entries.UnionWith(paths);
            }

            var largeFilesFirst = entries.OrderByDescending(static entry => EntryPath(entry).EndsWith(".smb", StringComparison.OrdinalIgnoreCase));
            await FetchPathsAsync(http, largeFilesFirst, parallel: BackgroundFetches, cancellationToken: cancellationToken);
        }
        finally
        {
            CharacterFetch.Release();
        }
    }

    private static Dictionary<PlayerClass, List<string>> ReadCharacterPaths(string list)
    {
        var result = new Dictionary<PlayerClass, List<string>>();
        foreach (var line in list.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('\t');
            if (separator < 1 || !Enum.TryParse<PlayerClass>(line.AsSpan(0, separator), out var characterClass) || !Enum.IsDefined(characterClass))
            {
                throw new InvalidDataException($"{CharacterListFile} has an invalid class entry.");
            }

            var entry = line[(separator + 1)..];
            if (!EntryPath(entry).StartsWith(CharacterFolder, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{CharacterListFile} has an invalid asset path for {characterClass}.");
            }

            if (!result.TryGetValue(characterClass, out var files))
            {
                files = [];
                result.Add(characterClass, files);
            }

            files.Add(entry);
        }

        return result;
    }

    private static bool IsScene(string path, string[] skyFolders) =>
        SceneTextures.Contains(path)
        || ScenePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        || skyFolders.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsRosterAsset(string path) =>
        path.StartsWith(CharacterFolder, StringComparison.OrdinalIgnoreCase) || RosterEffects.Contains(path);

    // Requests kept in flight at once: a slow file holds one of them, not a whole batch. Before the game starts nothing
    // else runs, so many; behind the running opening, few.
    private const int ParallelFetches = 24;
    private const int BackgroundFetches = 4;

    private static async Task FetchPathsAsync(HttpClient http, IEnumerable<string> entries, Action<int, int>? progress = null,
        int parallel = ParallelFetches, CancellationToken cancellationToken = default)
    {
        var files = entries.Distinct(StringComparer.Ordinal).ToArray();
        var next = -1;
        var completed = 0;
        progress?.Invoke(completed, files.Length);
        // One thread: the workers interleave at their awaits, so the shared counters need no locking.
        async Task WorkAsync()
        {
            while (++next < files.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await FetchFileAsync(http, files[next], cancellationToken);
                completed++;
                progress?.Invoke(completed, files.Length);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Min(parallel, files.Length)).Select(_ => WorkAsync()));
    }

    // entry: a line of the list, the path and, after a tab, the file's hash (bootstrap.txt from the publish).
    private static async Task FetchFileAsync(HttpClient http, string entry, CancellationToken cancellationToken = default)
    {
        var separator = entry.IndexOf('\t');
        var path = EntryPath(entry);
        var target = Target(path);
        if (File.Exists(target))
        {
            return;
        }

        // The hash names this version of the file, so the site lets the browser keep it for good.
        var url = string.Join('/', path.Split('/').Select(Uri.EscapeDataString))
            + (separator < 0 ? string.Empty : "?v=" + entry[(separator + 1)..]);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var data = await FetchWithRetryAsync(() => http.GetByteArrayAsync(url, cancellationToken), cancellationToken);
        await File.WriteAllBytesAsync(target, data);
    }

    private static string EntryPath(string entry)
    {
        var separator = entry.IndexOf('\t');
        return separator < 0 ? entry : entry[..separator];
    }

    private static async Task<T> FetchWithRetryAsync<T>(Func<Task<T>> fetch, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await fetch();
            }
            catch (HttpRequestException error) when (attempt < 2
                && (error.StatusCode is null || (int)error.StatusCode >= 500))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
            }
        }
    }

    // A listed path stays inside the root: relative, '/'-separated, without "." or ".." segments.
    private static string Target(string relativePath)
    {
        var segments = relativePath.Split('/');
        if (relativePath.StartsWith('/') || segments.Any(static segment => segment is "" or "." or ".." || segment.Contains('\\')))
        {
            throw new InvalidDataException($"{ListFile} lists a path outside the client: {relativePath}");
        }

        return Path.Combine([Root, .. segments]);
    }
}
