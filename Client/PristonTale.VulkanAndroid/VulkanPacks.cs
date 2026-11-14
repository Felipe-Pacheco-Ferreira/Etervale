using System.Reflection;
using PristonTale.Assets;
using PristonTale.Client.Net.Packs;
using PristonTale.Simulation.World;

// The asset packs on the phone (docs/packs.md): the field and model packs the APK does not carry are downloaded from the
// pack address baked into the APK (EtervalePackAddress) when a field or a monster first needs them. A field whose stage is
// already on the phone (the APK's fields, or one downloaded before) never waits: its packs are brought up to date quietly.
// Any other field waits for its stage pack, shown on the download card (State); models and minimaps download quietly.
// Called from the game thread and the UI thread.
internal static class VulkanPacks
{
    // A field download that failed is tried again after this long, when the field is asked for again.
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(8);
    // A manifest read that failed is tried again after this long.
    private static readonly TimeSpan ManifestRetryAfter = TimeSpan.FromSeconds(30);

    private static readonly object Gate = new();
    private static readonly WorldCatalog Catalog = new();
    private static readonly Dictionary<string, Task> QuietDownloads = new(StringComparer.Ordinal);
    // Fields whose stage is on the phone, and those whose packs were already brought up to date this run.
    private static readonly HashSet<int> StagesPresent = [];
    private static readonly HashSet<int> Refreshed = [];
    private static PackStore store;
    private static Task manifest;
    private static DateTime manifestRetryAt;
    private static Task fieldDownload;
    private static CancellationTokenSource fieldCancel;
    private static int fieldCode;
    private static bool fieldFailed;
    private static DateTime retryAt;
    private static DownloadState state;
    private const long StageCheckEveryMs = 1000;
    private static int stageMissCode = -1;
    private static long stageMissUntil;

    // What the download card shows: the field, the bytes so far and in all, and the last error (null while it runs).
    internal sealed record DownloadState(int FieldCode, string FieldName, long Done, long Total, string Error);

    public static DownloadState State => Volatile.Read(ref state);

    private static string PackagedAddress => typeof(VulkanPacks).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "EtervalePackAddress")?.Value;

    // Reads the manifest (again, ManifestRetryAfter after a failure). Without a usable pack address there is none to read.
    public static Task PrepareAsync()
    {
        lock (Gate)
        {
            if (manifest is { IsFaulted: false } || manifest is not null && DateTime.UtcNow < manifestRetryAt)
            {
                return manifest;
            }

            if (store is null && CreateStore() is null)
            {
                return manifest = Task.CompletedTask;
            }

            manifestRetryAt = DateTime.UtcNow + ManifestRetryAfter;
            return manifest = store.LoadManifestAsync(CancellationToken.None);
        }
    }

    // Whether the field can load; when its stage is missing, starts its download (shown on the download card).
    public static bool FieldReady(FieldDef field) => FieldReady(field.FieldCode, field.Name);

    // A field by its code alone (world entry): named as the catalog names it.
    public static bool FieldReady(int code) => FieldReady(code, null);

    private static bool FieldReady(int code, string name)
    {
        lock (Gate)
        {
            if (StagesPresent.Contains(code))
            {
                Refresh(code);
                return true;
            }

            if (fieldDownload is { IsCompleted: false } && fieldCode == code)
            {
                return false;
            }
        }

        if (StageOnPhone(code))
        {
            lock (Gate)
            {
                StagesPresent.Add(code);
                if (fieldCode == code)
                {
                    Volatile.Write(ref state, null);
                }

                Refresh(code);
            }

            return true;
        }

        name ??= Catalog.FieldName(code);
        var loading = PrepareAsync();
        if (!loading.IsCompleted)
        {
            return false;
        }

        lock (Gate)
        {
            if (!loading.IsCompletedSuccessfully || store is not { Manifest: not null } packs)
            {
                Show(code, name, "Sem conexão com o servidor de mapas. Tentando de novo…");
                return false;
            }

            if (packs.Field(code) is null)
            {
                Show(code, name, "Este mapa ainda não está disponível no celular.");
                return false;
            }

            var missing = packs.MissingForField(code);
            if (missing.Count == 0)
            {
                // Installed by the record but its stage not found: the load itself will say what is wrong.
                return true;
            }

            // One field downloads at a time; a failed one waits for its retry time.
            if (fieldDownload is { IsCompleted: false } || fieldFailed && fieldCode == code && DateTime.UtcNow < retryAt)
            {
                return false;
            }

            fieldFailed = false;
            fieldCode = code;
            fieldCancel = new CancellationTokenSource();
            var total = missing.Sum(pack => pack.Size);
            var download = new FieldProgress(code, name);
            Volatile.Write(ref state, new DownloadState(code, name, 0, total, null));
            fieldDownload = Run(packs, missing, download, fieldCancel.Token);
            return false;
        }
    }

    // Stops the field download the card shows; it starts again only from RetryField.
    public static void CancelField()
    {
        lock (Gate)
        {
            fieldCancel?.Cancel();
        }
    }

    // The card's Tentar de novo: the field's download (or the manifest) is tried again the next time the field is asked for.
    public static void RetryField()
    {
        lock (Gate)
        {
            retryAt = DateTime.MinValue;
            manifestRetryAt = DateTime.MinValue;
            if (State is { Error: not null } failed)
            {
                Volatile.Write(ref state, failed with { Error = "Tentando de novo…" });
            }
        }
    }

    // The download a model needs, or null when its files are there (or there is no manifest to fetch it by).
    public static Task ModelAssets(string modelPath)
    {
        lock (Gate)
        {
            return store is { Manifest: not null } packs && packs.MissingForModel(modelPath) is { } pack ? Quietly(packs, pack) : null;
        }
    }

    // The field's minimap file prefix on the phone, or null while it is not there: its pack is then fetched quietly and
    // the HUD asks again. Fields whose minimap the APK carries are not asked for here (MobileMinimap.Open).
    public static string MinimapOf(int code)
    {
        lock (Gate)
        {
            if (store is not { Manifest: not null } packs || packs.Field(code) is not { Minimap.Length: > 0 } field)
            {
                return null;
            }

            if (packs.MissingPack(field.MinimapPack) is { } pack)
            {
                Quietly(packs, pack);
                return null;
            }

            return Path.Combine(VulkanAssets.Root, field.Minimap);
        }
    }

    private static PackStore CreateStore()
    {
        if (PackagedAddress is not { Length: > 0 } address)
        {
            return null;
        }

        try
        {
            // The packs' SHA-256 values come from the manifest, so it must come over TLS (a local test server excepted).
            var uri = new Uri(address);
            if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            {
                throw new ArgumentException("the pack address must be https");
            }

            return store = new PackStore(VulkanAssets.Root, PackStore.FolderOf(uri), PackStore.PinnedHandler(uri));
        }
        catch (Exception error) when (error is UriFormatException or ArgumentException)
        {
            Android.Util.Log.Error("Etervale.Packs", $"pack address {address} refused: {error.Message}");
            return null;
        }
    }

    // Once per field and run, with the manifest read: its stage pack (a newer build), its monsters and its minimap, quietly.
    private static void Refresh(int code)
    {
        if (store is not { Manifest: not null } packs || !Refreshed.Add(code))
        {
            return;
        }

        foreach (var pack in packs.MissingForField(code))
        {
            Quietly(packs, pack);
        }

        foreach (var monster in packs.MissingMonstersOf(code))
        {
            Quietly(packs, monster);
        }
    }

    private static Task Quietly(PackStore packs, PackEntry pack)
    {
        if (!QuietDownloads.TryGetValue(pack.Id, out var download) || download.IsFaulted)
        {
            download = packs.InstallAsync([pack], null, CancellationToken.None);
            QuietDownloads[pack.Id] = download;
        }

        return download;
    }

    // A field still missing is looked for on disk at most once per StageCheckEvery, not every frame it is waited for.
    private static bool StageOnPhone(int code)
    {
        var now = System.Environment.TickCount64;
        lock (Gate)
        {
            if (code == stageMissCode && now < stageMissUntil)
            {
                return false;
            }
        }

        var present = Catalog.TryGetFieldByCode(code, out var field)
            && File.Exists(AssetFiles.Resolve(Path.Combine(VulkanAssets.Root, field.StageSmd)));
        if (!present)
        {
            lock (Gate)
            {
                stageMissCode = code;
                stageMissUntil = now + StageCheckEveryMs;
            }
        }

        return present;
    }

    private static void Show(int code, string name, string error)
    {
        if (State is not { Error: { } shown } || State.FieldCode != code || shown != error)
        {
            fieldCode = code;
            Volatile.Write(ref state, new DownloadState(code, name, 0, 0, error));
        }
    }

    private static async Task Run(PackStore packs, IReadOnlyList<PackEntry> missing, FieldProgress progress, CancellationToken ct)
    {
        try
        {
            await packs.InstallAsync(missing, progress, ct);
            progress.Finish();
            lock (Gate)
            {
                StagesPresent.Add(progress.Code);
            }

            Volatile.Write(ref state, null);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            // A cancel waits for Tentar de novo; anything else is tried again by itself after RetryAfter.
            progress.Finish();
            Android.Util.Log.Warn("Etervale.Packs", $"field {progress.Code}: {error.Message}");
            var cancelled = error is OperationCanceledException;
            lock (Gate)
            {
                fieldFailed = true;
                retryAt = cancelled ? DateTime.MaxValue : DateTime.UtcNow + RetryAfter;
            }

            var shown = State;
            Volatile.Write(ref state, new DownloadState(progress.Code, progress.Name, shown?.Done ?? 0, shown?.Total ?? 0, error switch
            {
                OperationCanceledException => "Download cancelado.",
                PackSpaceException => "Pouco espaço livre no celular para este mapa.",
                _ => "Sem conexão com o servidor de mapas. Tentando de novo…"
            }));
        }
    }

    // Reports straight onto the card's state, on the download's thread, and nothing once the download has ended (a late
    // report must not cover its result).
    private sealed class FieldProgress(int code, string name) : IProgress<PackProgress>
    {
        private int _finished;

        public int Code => code;

        public string Name => name;

        public void Finish() => Volatile.Write(ref _finished, 1);

        public void Report(PackProgress value)
        {
            if (Volatile.Read(ref _finished) == 0)
            {
                Volatile.Write(ref state, new DownloadState(code, name, value.Done, value.Total, null));
            }
        }
    }
}
