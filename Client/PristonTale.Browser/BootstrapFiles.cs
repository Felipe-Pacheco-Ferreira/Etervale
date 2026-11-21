using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.World;
using System.Security.Cryptography;

namespace PristonTale.Browser;

// The files the game reads before it enters a field, listed in bootstrap.txt by the publish (EtervaleBootstrapFiles in
// the project). They are fetched from the site and written to the runtime's in-memory file system under the
// application's base directory, where MonoGame's content and AssetFiles read them as on the desktop. They come in three
// waves: the opening scene before the game starts, the existing character bootstrap and arrival effect during the
// opening, and the rest after a character enters the world. Class models are warmed after the opening's downloads.
internal static class BootstrapFiles
{
    private const string ListFile = WebAssetPacks.BootstrapFile;
    private const string CharacterListFile = "character-previews.txt";
    private const string AssetsFolder = "assets";
    private const string CharacterFolder = AssetsFolder + "/char/tmABCD/";
    private const string SkyManifest = AssetsFolder + "/" + PristonSkyManifest.AssetPath;
    private const string ContentFolder = "Content/";

    private static readonly string[] ScenePrefixes =
    [
        ContentFolder,
        "assets/image/Sinimage/Cursor/",
        "assets/image/flame"
    ];

    private static readonly HashSet<string> SceneAssets = new(StringComparer.OrdinalIgnoreCase)
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
        "assets/Effect/Vfx/Common/Progression.vfx.json",
        "assets/Effect/ObjAnimationData/GameStart/GameStart1.smd",
        "assets/Effect/ObjAnimationData/GameStart/tt.bmp",
    };

    private static readonly HashSet<string> RosterEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        "assets/Effect/Particle/warp_magicsquare01a.tga",
        "assets/Effect/Particle/flare.tga",
    };

    private static Uri? _clientAddress;
    private static string[] _remainingPaths = [];
    private static string[] _deferredPaths = [];
    private static string _bootstrapSha256 = string.Empty;
    private static Dictionary<string, string>? _bootstrapHashes;
    private static HttpClient? _characterHttp;
    private static Task<Dictionary<PlayerClass, List<string>>>? _characterPaths;
    private static readonly Dictionary<string, Task> CharacterDownloads = new(StringComparer.Ordinal);
    private static CancellationTokenSource? _characterWarmup;
    private static readonly HttpRequestOptionsKey<IDictionary<string, object>> FetchOptions = new("WebAssemblyFetchOptions");

    private static string Root => AppContext.BaseDirectory;
    private static HttpClient CharacterHttp => _characterHttp ??= new HttpClient { BaseAddress = _clientAddress };

    public static string AssetRoot => Path.Combine(Root, AssetsFolder);

    public static async Task FetchAsync(Uri clientAddress, string language)
    {
        using var http = new HttpClient { BaseAddress = clientAddress };
        var bytes = await FetchWithRetryAsync(() => http.GetByteArrayAsync(ListFile));
        var paths = WebAssetPacks.BootstrapLines(bytes);
        _bootstrapSha256 = Convert.ToHexString(SHA256.HashData(bytes));
        _bootstrapHashes = WebAssetPacks.ReadBootstrap(paths);
        _clientAddress = clientAddress;
        var sounds = FetchSoundIndexAsync(http);
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
            if (IsOtherLanguageFont(path, language))
            {
                continue;
            }

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
        _deferredPaths = deferredPaths.Distinct(StringComparer.Ordinal).ToArray();
        await FetchPathsAsync(http, scenePaths, BrowserHost.ReportLoginProgress);
        SoundFiles.Use(new BrowserSoundFiles(await sounds));
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

    // The sound files' list (BrowserSoundFiles). Without it the game runs silent rather than not at all.
    private static async Task<WebSoundIndex> FetchSoundIndexAsync(HttpClient http)
    {
        try
        {
            var index = WebSoundIndex.Read(await FetchWithRetryAsync(() => http.GetByteArrayAsync(WebSoundIndex.FileName)));
            if (index is not null)
            {
                return index;
            }

            Console.Error.WriteLine($"[Audio] {WebSoundIndex.FileName} is malformed; the game plays no sound.");
        }
        catch (HttpRequestException error)
        {
            Console.Error.WriteLine($"[Audio] {WebSoundIndex.FileName} could not be downloaded; the game plays no sound: {error.Message}");
        }

        return WebSoundIndex.Empty;
    }

    // A file listed outside bootstrap.txt (a line of audio.txt), downloaded when the game first reads it.
    public static Task FetchListedFileAsync(string entry) => FetchFileAsync(CharacterHttp, entry);

    // Runs while the opening plays, so it keeps few requests in flight: each finished file is written on the game's own
    // thread, between its frames.
    public static async Task FetchRemainingAsync()
    {
        using var http = new HttpClient { BaseAddress = _clientAddress };
        await FetchPathsAsync(http, _remainingPaths, BrowserHost.ReportLoginProgress, BackgroundFetches);
    }

    // The field's files before entering; other class families' motion banks, other fields' minimaps and art the world
    // does not draw once the field shows (DeferredWorldFiles).
    public static async Task FetchWorldAssetsAsync(PlayerClass playerClass)
    {
        _characterWarmup?.Cancel();
        string? ownMotionBank = null;
        if (await CharacterPathsAsync(CancellationToken.None) is { } catalog && catalog.TryGetValue(playerClass, out var classFiles))
        {
            ownMotionBank = classFiles.Select(entry => DeferredWorldFiles.FullMotionBank(EntryPath(entry))).FirstOrDefault(path => path is not null);
        }

        var entryWave = new List<string>();
        var later = new List<string>();
        foreach (var entry in _deferredPaths)
        {
            (DeferredWorldFiles.CanWait(EntryPath(entry), ownMotionBank) ? later : entryWave).Add(entry);
        }

        await FetchWaveAsync(entryWave, WebWorldLoading.DownloadProgress);
        DeferredWorldFiles.Defer(() => FetchWaveAsync(later, null));
    }

    private static async Task FetchWaveAsync(List<string> entries, Action<int, int>? progress)
    {
        using var http = new HttpClient { BaseAddress = _clientAddress };
        // A few ZIP chunks instead of thousands of requests; without valid packs, one request per file as before.
        if (_bootstrapHashes is not null
            && await WebAssetPacks.TryFetchAsync(http, Root, _bootstrapSha256, _bootstrapHashes, entries.Select(EntryPath), progress))
        {
            return;
        }

        // Files a pack already wrote count as done, so the bar keeps its place.
        var missing = entries.Where(entry => !File.Exists(Target(EntryPath(entry)))).ToArray();
        var present = entries.Count - missing.Length;
        await FetchPathsAsync(http, missing, progress is null ? null : (done, _) => progress(present + done, entries.Count));
    }

    public static async Task PrefetchCharacterAssetsAsync()
    {
        if (_characterWarmup is not null)
        {
            return;
        }

        using var warmup = new CancellationTokenSource();
        _characterWarmup = warmup;
        try
        {
            var catalog = await CharacterPathsAsync(warmup.Token);
            // Download complete model dependencies in turns, so the scene can start preparing each finished class.
            var entries = CharacterSelectArt.PreviewOrder
                .Where(catalog.ContainsKey)
                .SelectMany(job => catalog[job].OrderByDescending(IsAnimation));
            await FetchPathsAsync(entries, (entry, token) => FetchCharacterFileAsync(entry, true, token),
                parallel: BackgroundFetches, cancellationToken: warmup.Token);
        }
        catch (OperationCanceledException) when (warmup.IsCancellationRequested)
        {
        }
        finally
        {
            _characterWarmup = null;
        }
    }

    public static async Task FetchCharacterAssetsAsync(IReadOnlyList<PlayerClass> classes, CancellationToken cancellationToken)
    {
        if (classes.Count == 0)
        {
            return;
        }

        var catalog = await CharacterPathsAsync(cancellationToken);
        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var characterClass in classes)
        {
            if (!catalog.TryGetValue(characterClass, out var paths))
            {
                throw new InvalidDataException($"{CharacterListFile} is missing files for {characterClass}.");
            }

            entries.UnionWith(paths);
        }

        var largeFilesFirst = entries.OrderByDescending(IsAnimation);
        await FetchPathsAsync(largeFilesFirst, (entry, token) => FetchCharacterFileAsync(entry, false, token),
            parallel: BackgroundFetches, cancellationToken: cancellationToken);
    }

    public static bool AreCharacterAssetsReady(PlayerClass playerClass)
    {
        if (_characterPaths is not { IsCompletedSuccessfully: true } catalog || !catalog.Result.TryGetValue(playerClass, out var entries))
        {
            return false;
        }

        foreach (var entry in entries)
        {
            if (!CharacterDownloads.TryGetValue(entry, out var download) || !download.IsCompletedSuccessfully)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAnimation(string entry) => EntryPath(entry).EndsWith(".smb", StringComparison.OrdinalIgnoreCase);

    private static Task<Dictionary<PlayerClass, List<string>>> CharacterPathsAsync(CancellationToken cancellationToken)
    {
        if (_characterPaths is null || !_characterPaths.IsCompletedSuccessfully && _characterPaths.IsCompleted)
        {
            _characterPaths = LoadCharacterPathsAsync();
            ObserveFault(_characterPaths);
        }

        return _characterPaths.WaitAsync(cancellationToken);
    }

    private static async Task<Dictionary<PlayerClass, List<string>>> LoadCharacterPathsAsync() =>
        ReadCharacterPaths(await FetchWithRetryAsync(() => CharacterHttp.GetStringAsync(CharacterListFile)));

    private static Task FetchCharacterFileAsync(string entry, bool lowPriority, CancellationToken cancellationToken)
    {
        // Downloads belong to the shared file, so a picker leaving does not cancel another caller's request.
        if (!CharacterDownloads.TryGetValue(entry, out var download) || download.IsFaulted || download.IsCanceled)
        {
            download = FetchFileAsync(CharacterHttp, entry, lowPriority: lowPriority);
            CharacterDownloads[entry] = download;
            ObserveFault(download);
        }

        return download.WaitAsync(cancellationToken);
    }

    private static void ObserveFault(Task task)
    {
        // A canceled caller can stop awaiting a shared fetch before the network itself answers.
        _ = task.ContinueWith(static failed => { _ = failed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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
        SceneAssets.Contains(path)
        || ScenePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        || skyFolders.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    // Another language's own interface font (UiFonts): the game draws only the page's language, so the others are never
    // fetched.
    private static bool IsOtherLanguageFont(string path, string language) =>
        WebAssetPacks.IsLanguageFont(path)
        && !path.Equals(ContentFolder + UiFonts.AssetOf(language) + ".xnb", StringComparison.OrdinalIgnoreCase);

    private static bool IsRosterAsset(string path) =>
        path.StartsWith(CharacterFolder, StringComparison.OrdinalIgnoreCase) && !DeferredWorldFiles.IsFullMotionBank(path)
        || RosterEffects.Contains(path);

    // Requests kept in flight at once: a slow file holds one of them, not a whole batch. Before the game starts nothing
    // else runs, so many; behind the running opening, few.
    private const int ParallelFetches = 24;
    private const int BackgroundFetches = 4;

    private static Task FetchPathsAsync(HttpClient http, IEnumerable<string> entries, Action<int, int>? progress = null,
        int parallel = ParallelFetches) =>
        FetchPathsAsync(entries, (entry, token) => FetchFileAsync(http, entry, token), progress, parallel);

    private static async Task FetchPathsAsync(IEnumerable<string> entries, Func<string, CancellationToken, Task> fetch,
        Action<int, int>? progress = null,
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
                await fetch(files[next], cancellationToken);
                completed++;
                progress?.Invoke(completed, files.Length);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Min(parallel, files.Length)).Select(_ => WorkAsync()));
    }

    // entry: a line of the list, the path and, after a tab, the file's hash (bootstrap.txt from the publish).
    private static async Task FetchFileAsync(HttpClient http, string entry, CancellationToken cancellationToken = default,
        bool lowPriority = false)
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
        var data = await FetchWithRetryAsync(() => DownloadAsync(http, url, lowPriority, cancellationToken), cancellationToken);
        await File.WriteAllBytesAsync(target, data);
    }

    private static async Task<byte[]> DownloadAsync(HttpClient http, string url, bool lowPriority, CancellationToken cancellationToken)
    {
        if (!lowPriority)
        {
            return await http.GetByteArrayAsync(url, cancellationToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(FetchOptions, new Dictionary<string, object> { ["priority"] = "low" });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
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

    private static string Target(string relativePath) => WebAssetPacks.LocalPath(Root, relativePath);
}
