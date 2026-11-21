using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PristonTale.Assets;
using PristonTale.WebAssetPacks;
using static PristonTale.Assets.WebAssetPacks;

namespace PristonTale.Tests;

internal static class WebAssetPackTests
{
    private const long MiB = 1024 * 1024;

    // In path order: a 3 MiB chunk, then 6 MiB and 1 KiB together, a 9 MiB file alone over the budget, then 2 KiB.
    private static readonly (string Path, long Bytes)[] FixtureFiles =
    [
        ("Content/Effects/Scene.xnb", 3 * MiB),
        ("assets/Field/one.bmp", 6 * MiB),
        ("assets/Field/two.bmp", 1024),
        ("assets/char/m1.smb", 9 * MiB),
        ("assets/image/icon.tga", 2048),
    ];

    private static readonly string[] WorldFiles = FixtureFiles.Skip(1).Select(file => file.Path).ToArray();

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("world packs build reproducibly and restore only the missing files from the chunks that hold them", RoundTripAsync);
        yield return ("world packs make no request when every world file is present", AllPresentAsync);
        yield return ("world packs fall back without writing when the index is absent, stale or malformed", MissingIndexAsync);
        yield return ("world pack index refuses renamed, repeated, unknown, rehashed or misplaced files and chunks", IndexChecksAsync);
        yield return ("world packs refuse damaged chunks and leave only fully checked files", DamagedChunksAsync);
        yield return ("world packs pass the caller's cancellation on instead of falling back", CancellationAsync);
        yield return ("bootstrap lines refuse unsafe paths, malformed hashes and paths that differ only by case", BootstrapLinesAsync);
        yield return ("a language's own interface font gets a world-pack chunk of its own", LanguageFontChunkAsync);
    }

    private static async Task LanguageFontChunkAsync()
    {
        using var fixture = await PackFixture.CreateAsync([("Content/Effects/Scene.xnb", 1024), ("Content/Fonts/UI-zh-CN.xnb", 2048), ("Content/Fonts/UI.xnb", 1024)]);
        var font = fixture.Index.Files.Single(file => file.Path == "Content/Fonts/UI-zh-CN.xnb");
        Assert.True(fixture.Index.Chunks.Length == 2 && fixture.Index.Chunks[font.Chunk].Files == 1, "a language's font shared a chunk with other files, or the others did not share theirs");
    }

    private static async Task RoundTripAsync()
    {
        using var fixture = await PackFixture.CreateAsync();
        var again = Path.Combine(fixture.Folder, "again");
        await WebAssetPackBuilder.BuildAsync(fixture.Source, again);
        Assert.True(File.ReadAllBytes(Path.Combine(fixture.Packs, IndexFile)).SequenceEqual(File.ReadAllBytes(Path.Combine(again, IndexFile))),
            "a rebuild of the same files wrote a different index");
        var index = fixture.Index;
        Assert.True(index.Chunks.Length == 4 && index.Chunks.Count(chunk => chunk.RawBytes > ChunkBudgetBytes) == 1 && index.Files.Length == FixtureFiles.Length,
            "files were not grouped by the chunk budget");

        var client = fixture.NewClient();
        fixture.CopyToClient(client, "assets/Field/two.bmp");
        var server = new PackServer(fixture.Packs);
        var reports = new List<(int Done, int Total)>();
        var fetched = await fixture.FetchAsync(server, client, WorldFiles, (done, total) => reports.Add((done, total)));

        Assert.True(fetched, "valid packs fell back to per-file downloads");
        foreach (var path in WorldFiles)
        {
            Assert.True(File.ReadAllBytes(LocalPath(client, path)).SequenceEqual(File.ReadAllBytes(LocalPath(fixture.Source, path))), $"{path} differs");
        }

        Assert.True(!File.Exists(LocalPath(client, "Content/Effects/Scene.xnb")), "a file outside the requested wave was written");
        Assert.True(server.Requests.Count == 4 && server.Requests[0] == IndexFile && !server.Requests.Contains(index.Chunks[0].Path),
            "the chunk of files nobody asked for was downloaded");
        Assert.True(reports[0] == (1, 4) && reports[^1] == (4, 4) && reports.Zip(reports.Skip(1)).All(pair => pair.Second.Done == pair.First.Done + 1),
            "progress did not start from the present files and rise one file at a time");
        Assert.True(Directory.GetFiles(client, "*.tmp", SearchOption.AllDirectories).Length == 0, "a partial file was left behind");
    }

    private static async Task AllPresentAsync()
    {
        using var fixture = await PackFixture.CreateAsync();
        var client = fixture.NewClient();
        foreach (var path in WorldFiles)
        {
            fixture.CopyToClient(client, path);
        }

        var server = new PackServer(fixture.Packs);
        var reports = new List<(int Done, int Total)>();
        Assert.True(await fixture.FetchAsync(server, client, WorldFiles, (done, total) => reports.Add((done, total))), "a complete wave was not ready");
        Assert.True(server.Requests.Count == 0 && reports.SequenceEqual([(4, 4)]), "a complete wave still asked the site");
    }

    private static async Task MissingIndexAsync()
    {
        using var fixture = await PackFixture.CreateAsync();
        var client = fixture.NewClient();
        var absent = new PackServer(fixture.Packs) { Replace = (path, body) => path == IndexFile ? null : body };
        Assert.True(!await fixture.FetchAsync(absent, client, WorldFiles), "an absent index did not fall back");

        var stale = new PackServer(fixture.Packs) { Replace = (path, body) => path == IndexFile ? Serialize(fixture.Index with { BootstrapSha256 = new string('A', 64) }) : body };
        Assert.True(!await fixture.FetchAsync(stale, client, WorldFiles), "an index of another bootstrap was used");

        var malformed = new PackServer(fixture.Packs) { Replace = (path, body) => path == IndexFile ? "{\"version\":1"u8.ToArray() : body };
        Assert.True(!await fixture.FetchAsync(malformed, client, WorldFiles), "a malformed index did not fall back");

        var mixedCase = new PackServer(fixture.Packs);
        var unknownWave = WorldFiles.Append("assets/field/one.bmp").ToArray();
        Assert.True(!await fixture.FetchAsync(mixedCase, client, unknownWave) && mixedCase.Requests.Count == 0, "a path outside the bootstrap's spelling was accepted");
        Assert.True(Directory.GetFiles(client, "*", SearchOption.AllDirectories).Length == 0, "a refused pack wrote files");
    }

    private static async Task IndexChecksAsync()
    {
        using var fixture = await PackFixture.CreateAsync();
        var index = fixture.Index;
        var bootstrap = fixture.Bootstrap;
        Check(index, fixture.BootstrapSha256, bootstrap);

        PackIndex WithFile(int at, PackedFile file) => index with { Files = index.Files.Select((old, position) => position == at ? file : old).ToArray() };
        PackIndex WithChunk(int at, PackChunk chunk) => index with { Chunks = index.Chunks.Select((old, position) => position == at ? chunk : old).ToArray() };
        var first = index.Files[1];
        var refused = new (string Name, PackIndex Index)[]
        {
            ("renamed by case", WithFile(1, first with { Path = first.Path.ToUpperInvariant() })),
            ("repeated", WithFile(2, first)),
            ("unknown", WithFile(1, first with { Path = "assets/Field/other.bmp" })),
            ("rehashed", WithFile(1, first with { Sha256 = new string('0', 64) })),
            ("resized", WithFile(1, first with { Bytes = first.Bytes + 1 })),
            ("moved to a missing chunk", WithFile(1, first with { Chunk = index.Chunks.Length })),
            ("misplaced chunk", WithChunk(0, index.Chunks[0] with { Path = "../" + index.Chunks[0].Sha256 + ".zip" })),
            ("oversized shared chunk", WithChunk(1, index.Chunks[1] with { RawBytes = MaximumBytes + 1 })),
            ("another format", index with { Version = FormatVersion + 1 }),
            ("one file short", index with { Files = index.Files[1..] }),
        };
        foreach (var (name, changed) in refused)
        {
            Assert.Throws<InvalidDataException>(() => Check(changed, fixture.BootstrapSha256, bootstrap), $"a {name} entry was accepted");
        }

        await Task.CompletedTask;
    }

    private static async Task DamagedChunksAsync()
    {
        using var fixture = await PackFixture.CreateAsync();
        var big = fixture.Index.Chunks[2];
        var flipped = new PackServer(fixture.Packs) { Replace = (path, body) => path == big.Path ? Flip(body) : body };
        await RefusedAsync(fixture, flipped, "a chunk with a changed byte");

        var truncated = new PackServer(fixture.Packs) { Replace = (path, body) => path == big.Path ? body[..^16] : body };
        await RefusedAsync(fixture, truncated, "a truncated chunk");

        // A chunk the index vouches for, holding an entry longer than its listed size.
        var forged = Forge(fixture, 1, "assets/Field/two.bmp");
        var forgedIndex = fixture.Index with { Chunks = fixture.Index.Chunks.Select((chunk, at) => at == 1 ? forged.Chunk : chunk).ToArray() };
        var tampered = new PackServer(fixture.Packs)
        {
            Replace = (path, body) => path == IndexFile ? Serialize(forgedIndex) : path == forged.Chunk.Path ? forged.Body : body,
        };
        await RefusedAsync(fixture, tampered, "a resized entry");
    }

    private static async Task RefusedAsync(PackFixture fixture, PackServer server, string damage)
    {
        var client = fixture.NewClient();
        var reports = new List<(int Done, int Total)>();
        Assert.True(!await fixture.FetchAsync(server, client, WorldFiles, (done, total) => reports.Add((done, total))), $"{damage} was accepted");
        foreach (var path in WorldFiles)
        {
            var target = LocalPath(client, path);
            Assert.True(!File.Exists(target) || File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(LocalPath(fixture.Source, path))),
                $"{damage} left an unchecked {path}");
        }

        Assert.True(Directory.GetFiles(client, "*.tmp", SearchOption.AllDirectories).Length == 0, $"{damage} left a partial file");
        Assert.True(reports.Zip(reports.Skip(1)).All(pair => pair.Second.Done >= pair.First.Done), $"{damage} moved progress backwards");
    }

    private static async Task CancellationAsync()
    {
        using var fixture = await PackFixture.CreateAsync();
        using var before = new CancellationTokenSource();
        await before.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.FetchAsync(new PackServer(fixture.Packs), fixture.NewClient(), WorldFiles, cancellationToken: before.Token),
            "a canceled entry still fetched packs");

        using var during = new CancellationTokenSource();
        var server = new PackServer(fixture.Packs) { OnRequest = path => path.StartsWith(ChunkFolder + "/", StringComparison.Ordinal) ? during : null };
        var client = fixture.NewClient();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.FetchAsync(server, client, WorldFiles, cancellationToken: during.Token),
            "a cancellation during a chunk became a per-file fallback");
        Assert.True(Directory.GetFiles(client, "*.tmp", SearchOption.AllDirectories).Length == 0, "cancellation left a partial file");
    }

    private static Task BootstrapLinesAsync()
    {
        var hash = new string('a', 64);
        Assert.True(ReadBootstrap(BootstrapLines(Encoding.UTF8.GetBytes($"\uFEFFassets/a.bmp\t{hash}\r\nContent/b.xnb\t{hash}\r\n"))) is { Count: 2 } read
            && read["assets/a.bmp"] == hash.ToUpperInvariant(), "a published bootstrap was not read");
        foreach (var line in new[] { "../a.bmp", "assets/../a.bmp", "/assets/a.bmp", "assets//a.bmp", "assets\\a.bmp", "C:/assets/a.bmp", "assets/./a.bmp" })
        {
            Assert.True(ReadBootstrap([$"{line}\t{hash}"]) is null, $"unsafe path {line} was accepted");
        }

        Assert.True(ReadBootstrap([$"assets/a.bmp\t{hash[1..]}"]) is null && ReadBootstrap(["assets/a.bmp"]) is null, "a malformed hash was accepted");
        Assert.True(ReadBootstrap([$"assets/a.bmp\t{hash}", $"assets/A.bmp\t{hash}"]) is null, "paths that differ only by case were accepted");
        Assert.True(ReadBootstrap([$"assets/a.bmp\t{hash}", $"assets/a.bmp\t{hash.ToUpperInvariant()}"]) is { Count: 1 }, "a repeated line was refused");
        Assert.True(ReadBootstrap([$"assets/a.bmp\t{hash}", $"assets/a.bmp\t{new string('b', 64)}"]) is null, "one path with two hashes was accepted");
        Assert.True(ReadBootstrap(["assets/a\u0001.bmp\t" + hash]) is null && ReadBootstrap([$"assets/a%2e.bmp\t{hash}"]) is null,
            "a control character or an escape in a path was accepted");
        Assert.Throws<InvalidDataException>(() => LocalPath("root", "assets/../../x"), "a path left the client folder");
        return Task.CompletedTask;
    }

    private static byte[] Flip(byte[] body)
    {
        var copy = body.ToArray();
        copy[copy.Length / 2] ^= 0x5A;
        return copy;
    }

    private static (PackChunk Chunk, byte[] Body) Forge(PackFixture fixture, int at, string changedPath)
    {
        var original = fixture.Index.Chunks[at];
        using var output = new MemoryStream();
        using (var source = new ZipArchive(File.OpenRead(LocalPath(fixture.Packs, original.Path)), ZipArchiveMode.Read))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var read = new MemoryStream();
                using (var stream = entry.Open())
                {
                    stream.CopyTo(read);
                }

                var bytes = read.ToArray();
                if (entry.FullName == changedPath)
                {
                    bytes = [.. bytes, 0];
                }

                using var write = archive.CreateEntry(entry.FullName).Open();
                write.Write(bytes);
            }
        }

        var body = output.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(body));
        return (original with { Path = ChunkPath(sha256), Sha256 = sha256, Bytes = body.Length }, body);
    }

    private static byte[] Serialize(PackIndex index) => JsonSerializer.SerializeToUtf8Bytes(index, JsonOptions);

    private sealed class PackFixture : IDisposable
    {
        private PackFixture(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }
        public string Source => Path.Combine(Folder, "client");
        public string Packs => Path.Combine(Folder, "packs");
        public PackIndex Index { get; private set; } = null!;
        public Dictionary<string, string> Bootstrap { get; private set; } = null!;
        public string BootstrapSha256 { get; private set; } = string.Empty;
        private int _clients;

        public static async Task<PackFixture> CreateAsync(IReadOnlyList<(string Path, long Bytes)>? files = null)
        {
            var fixture = new PackFixture(Path.Combine(Path.GetTempPath(), "etervale-packs-" + Guid.NewGuid().ToString("N")));
            var lines = new StringBuilder();
            var seed = 1;
            foreach (var (path, bytes) in files ?? FixtureFiles)
            {
                var data = new byte[bytes];
                var random = new Random(seed++);
                for (var at = 0; at < data.Length; at += 64)
                {
                    // Runs with some noise: compressible like game data, yet each file distinct.
                    data[at] = (byte)random.Next(256);
                }

                var target = LocalPath(fixture.Source, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, data);
                lines.Append(path).Append('\t').Append(Convert.ToHexString(SHA256.HashData(data))).Append("\r\n");
            }

            var list = Encoding.UTF8.GetBytes(lines.ToString());
            await File.WriteAllBytesAsync(Path.Combine(fixture.Source, "bootstrap.txt"), list);
            fixture.BootstrapSha256 = Convert.ToHexString(SHA256.HashData(list));
            fixture.Bootstrap = ReadBootstrap(BootstrapLines(list))!;
            try
            {
                await WebAssetPackBuilder.BuildAsync(fixture.Source, fixture.Packs);
                fixture.Index = JsonSerializer.Deserialize<PackIndex>(File.ReadAllBytes(Path.Combine(fixture.Packs, IndexFile)), JsonOptions)!;
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public string NewClient()
        {
            var client = Path.Combine(Folder, "browser-" + ++_clients);
            Directory.CreateDirectory(client);
            return client;
        }

        public void CopyToClient(string client, string path)
        {
            var target = LocalPath(client, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(LocalPath(Source, path), target);
        }

        public async Task<bool> FetchAsync(PackServer server, string client, IReadOnlyCollection<string> required, Action<int, int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var http = new HttpClient(server) { BaseAddress = new Uri("http://packs.test/play/") };
            return await TryFetchAsync(http, client, BootstrapSha256, Bootstrap, required, progress, cancellationToken);
        }

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    // Serves the pack folder as the site would, with optional replaced bodies (null: not found) and a hook per request.
    private sealed class PackServer(string root) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public Func<string, byte[], byte[]?> Replace { get; init; } = static (_, body) => body;
        public Func<string, CancellationTokenSource?> OnRequest { get; init; } = static _ => null;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath["/play/".Length..];
            lock (Requests)
            {
                Requests.Add(path);
            }

            if (OnRequest(path) is { } cancel)
            {
                await cancel.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var file = LocalPath(root, path);
            var body = Replace(path, File.Exists(file) ? await File.ReadAllBytesAsync(file, cancellationToken) : []);
            return body is null || !File.Exists(file) && body.Length == 0
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        }
    }
}
