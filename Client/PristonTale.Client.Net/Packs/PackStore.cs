using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using PristonTale.Assets;
using PristonTale.Protocol.Transport;

namespace PristonTale.Client.Net.Packs;

public readonly record struct PackProgress(long Done, long Total);

// Too little room on the device for a pack, its download and its unpacked files.
public sealed class PackSpaceException(long needed, long free)
    : IOException($"Not enough free space: {needed >> 20} MB needed, {free >> 20} MB free.");

// The asset packs installed under one asset root (docs/packs.md): reads packs.json from the pack address (kept beside the
// installed files for when the address cannot be reached), tells which packs a field or a model needs, and downloads,
// checks and unpacks the missing ones. A pack counts as installed when installed.json names it with its SHA-256, or when
// every file it lists is already there at its size (the files the APK itself brings). One install runs at a time.
public sealed class PackStore : IDisposable
{
    public const string InstalledFileName = "packs-installed.json";
    private const string DownloadFolder = ".packs";
    private const int CopyBufferBytes = 1 << 16;
    private const int PartHashLength = 16;
    // Room kept free besides a pack and its unpacked files.
    private const long SpareBytes = 64L << 20;
    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(20);
    // A download that receives nothing this long is given up (and resumed on the next try).
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private readonly string _root;
    private readonly Uri _address;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private readonly object _gate = new();
    private Dictionary<string, string> _installed;
    private Dictionary<string, PackEntry> _packs = new(StringComparer.Ordinal);
    private Dictionary<int, FieldPacks> _fields = [];
    private PackManifest? _manifest;

    // address: the folder that serves packs.json and the pack zips; handler: how to reach it (PinnedHandler for the game's).
    public PackStore(string root, Uri address, HttpMessageHandler handler)
    {
        _root = Path.GetFullPath(root);
        _address = address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/");
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _installed = ReadInstalled();
    }

    public PackManifest? Manifest => Volatile.Read(ref _manifest);

    // A handler trusting what the game server's address trusts: "cert=<sha-256>" in its query pins the certificate.
    public static HttpMessageHandler PinnedHandler(Uri address)
    {
        var policy = ServerCertificatePolicy.FromAddress(address);
        return new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            SslOptions = { RemoteCertificateValidationCallback = (_, certificate, _, errors) => policy.Accepts(certificate, errors) }
        };
    }

    // The address without its query, which only carries the certificate pin.
    public static Uri FolderOf(Uri address) => new UriBuilder(address) { Query = "" }.Uri;

    // The manifest from the pack address, kept for the next start; the kept one when the address does not answer in time.
    public async Task<PackManifest> LoadManifestAsync(CancellationToken ct)
    {
        var cached = Path.Combine(_root, PackManifest.FileName);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(ManifestTimeout);
            var bytes = await _http.GetByteArrayAsync(new Uri(_address, PackManifest.FileName), deadline.Token);
            var fresh = Use(PackManifest.Read(new MemoryStream(bytes)));
            Directory.CreateDirectory(_root);
            WriteAtomically(cached, bytes);
            return fresh;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidDataException
            or OperationCanceledException && !ct.IsCancellationRequested)
        {
            if (File.Exists(cached))
            {
                using var stream = File.OpenRead(cached);
                return Use(PackManifest.Read(stream));
            }

            throw;
        }
    }

    // The packs a field needs that are not installed; empty when the field needs none or the manifest does not know it.
    public IReadOnlyList<PackEntry> MissingForField(int fieldCode)
    {
        lock (_gate)
        {
            return _fields.TryGetValue(fieldCode, out var field) ? Missing(field.Packs) : [];
        }
    }

    // The packs of the monsters that spawn in a field and are not installed.
    public IReadOnlyList<PackEntry> MissingMonstersOf(int fieldCode)
    {
        lock (_gate)
        {
            return _fields.TryGetValue(fieldCode, out var field) ? Missing(field.Monsters) : [];
        }
    }

    public FieldPacks? Field(int fieldCode)
    {
        lock (_gate)
        {
            return _fields.GetValueOrDefault(fieldCode);
        }
    }

    // The pack a model the server names needs, when it is not installed.
    public PackEntry? MissingForModel(string modelPath)
    {
        lock (_gate)
        {
            if (_manifest is null || !_manifest.Models.TryGetValue(PackManifest.ModelKey(modelPath), out var id))
            {
                return null;
            }

            return MissingOne(id);
        }
    }

    // A pack by id when it is not installed (a field's minimap pack).
    public PackEntry? MissingPack(string packId)
    {
        lock (_gate)
        {
            return MissingOne(packId);
        }
    }

    // One pack installs at a time, so a field's packs wait at most for the one pack already under way, not for a batch.
    public async Task InstallAsync(IReadOnlyList<PackEntry> packs, IProgress<PackProgress>? progress, CancellationToken ct)
    {
        var total = packs.Sum(pack => pack.Size);
        var done = 0L;
        foreach (var pack in packs)
        {
            await _installGate.WaitAsync(ct);
            try
            {
                if (!IsInstalled(pack))
                {
                    var before = done;
                    await InstallOneAsync(pack, bytes => progress?.Report(new PackProgress(before + bytes, total)), ct);
                }
            }
            finally
            {
                _installGate.Release();
            }

            done += pack.Size;
            progress?.Report(new PackProgress(done, total));
        }
    }

    public bool IsInstalled(PackEntry pack)
    {
        lock (_gate)
        {
            if (_installed.TryGetValue(pack.Id, out var sha) && sha == pack.Sha256)
            {
                return true;
            }

            // Installed with another SHA-256: rebuilt since, so it is fetched again.
            if (_installed.ContainsKey(pack.Id))
            {
                return false;
            }

            foreach (var file in pack.Files)
            {
                var path = AssetFiles.Resolve(Path.Combine(_root, file.Path));
                if (!File.Exists(path) || new FileInfo(path).Length != file.Size)
                {
                    return false;
                }
            }

            _installed[pack.Id] = pack.Sha256;
            SaveInstalled();
            return true;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _installGate.Dispose();
    }

    private PackManifest Use(PackManifest manifest)
    {
        if (manifest.Version != PackManifest.CurrentVersion)
        {
            throw new InvalidDataException($"Pack manifest version {manifest.Version}, expected {PackManifest.CurrentVersion}.");
        }

        lock (_gate)
        {
            _packs = manifest.Packs.ToDictionary(pack => pack.Id, StringComparer.Ordinal);
            _fields = manifest.Fields.ToDictionary(field => field.Code);
            Volatile.Write(ref _manifest, manifest);
        }

        return manifest;
    }

    // Allocates only when something is missing: the game asks every few frames while a field streams in.
    private IReadOnlyList<PackEntry> Missing(IReadOnlyList<string> ids)
    {
        List<PackEntry>? missing = null;
        for (var index = 0; index < ids.Count; index++)
        {
            if (MissingOne(ids[index]) is { } pack)
            {
                (missing ??= []).Add(pack);
            }
        }

        return missing is null ? [] : missing;
    }

    private PackEntry? MissingOne(string id) => _packs.TryGetValue(id, out var pack) && !IsInstalled(pack) ? pack : null;

    private async Task InstallOneAsync(PackEntry pack, Action<long> downloaded, CancellationToken ct)
    {
        var folder = Path.Combine(_root, DownloadFolder);
        Directory.CreateDirectory(folder);
        EnsureRoom(pack.Size * 2 + pack.Files.Sum(file => file.Size));
        // Named by the build it belongs to: a part left by an earlier build of the pack is dropped, never resumed into.
        var part = Path.Combine(folder, $"{pack.Id}-{pack.Sha256[..PartHashLength]}.part");
        foreach (var stale in Directory.EnumerateFiles(folder, pack.Id + "-*.part"))
        {
            if (stale != part)
            {
                File.Delete(stale);
            }
        }

        await DownloadAsync(pack, part, downloaded, ct);
        Unpack(part);
        lock (_gate)
        {
            _installed[pack.Id] = pack.Sha256;
            SaveInstalled();
        }

        File.Delete(part);
    }

    // Resumes a partial download with a range request; a file that does not match its SHA-256 is thrown away.
    private async Task DownloadAsync(PackEntry pack, string part, Action<long> downloaded, CancellationToken ct)
    {
        var have = File.Exists(part) ? new FileInfo(part).Length : 0L;
        if (have > pack.Size)
        {
            File.Delete(part);
            have = 0;
        }

        if (have < pack.Size)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_address, PackManifest.PackFileName(pack.Id)));
            if (have > 0)
            {
                request.Headers.Range = new RangeHeaderValue(have, null);
            }

            using var answer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            answer.CancelAfter(StallTimeout);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, answer.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException($"Pack {pack.Id}: the server did not answer.");
            }

            using var _ = response;
            response.EnsureSuccessStatusCode();
            var append = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!append)
            {
                have = 0;
            }

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferBytes, useAsync: true);
            var buffer = new byte[CopyBufferBytes];
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            while (true)
            {
                stall.CancelAfter(StallTimeout);
                int read;
                try
                {
                    read = await input.ReadAsync(buffer, stall.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new IOException($"Pack {pack.Id} stopped arriving.");
                }

                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                have += read;
                downloaded(have);
            }
        }

        string sha;
        await using (var check = File.OpenRead(part))
        {
            sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct));
        }

        if (new FileInfo(part).Length != pack.Size || sha != pack.Sha256)
        {
            File.Delete(part);
            throw new InvalidDataException($"Pack {pack.Id} arrived damaged; it will be downloaded again.");
        }
    }

    // Every entry lands under the root; one whose path would leave it stops the install.
    private void Unpack(string zip)
    {
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(_root, entry.FullName));
            if (!target.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Pack entry {entry.FullName} leaves the asset folder.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private void EnsureRoom(long bytes)
    {
        long free;
        try
        {
            free = new DriveInfo(_root).AvailableFreeSpace;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (free < bytes + SpareBytes)
        {
            throw new PackSpaceException(bytes + SpareBytes, free);
        }
    }

    private Dictionary<string, string> ReadInstalled()
    {
        var path = Path.Combine(_root, InstalledFileName);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(path)) ?? new(StringComparer.Ordinal)
                : new(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A damaged list only costs a check of each pack's files again.
            return new(StringComparer.Ordinal);
        }
    }

    private void SaveInstalled()
    {
        Directory.CreateDirectory(_root);
        WriteAtomically(Path.Combine(_root, InstalledFileName), JsonSerializer.SerializeToUtf8Bytes(_installed));
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }
}
