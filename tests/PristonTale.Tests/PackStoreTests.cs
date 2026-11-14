using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using PristonTale.Assets;
using PristonTale.Client.Net.Packs;

namespace PristonTale.Tests;

internal static class PackStoreTests
{
    private static readonly Uri Address = new("https://packs.test/packs/");

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("packs: model paths become one key and pack ids, whatever separator, case or extension", NamesAsync);
        yield return ("packs: a missing pack is downloaded, checked and unpacked; files already present count as installed", InstallAsync);
        yield return ("packs: a rebuilt pack (new SHA-256) is fetched again", RebuiltAsync);
        yield return ("packs: an interrupted download resumes with a range request", ResumeAsync);
        yield return ("packs: a damaged download is thrown away and an entry leaving the asset folder is refused", RefusedAsync);
    }

    private static Task NamesAsync()
    {
        Assert.True(PackManifest.ModelKey(@"Char\Monster\Hopy\Hopy.ini") == "char/monster/hopy/hopy.inx", "the model key");
        Assert.True(PackManifest.ModelPackId("char/monster/Hopy/Hopy.inx") == "model-char-monster-hopy-hopy", "the model pack id");
        Assert.True(PackManifest.FieldPackId("forest-01") == "field-forest-01", "the field pack id");
        return Task.CompletedTask;
    }

    private static async Task InstallAsync()
    {
        using var site = new FakeSite();
        var pack = site.Add("field-a", ("Field/a/stage.smd", "stage"), ("Field/a/grass.bmp", "grass"));
        var present = site.Add("field-b", ("Field/b/stage.smd", "b"));
        site.Add("model-m", ("char/monster/m/m.inx", "m"));
        site.Publish(fields: [new FieldPacks(7, "a", "A", ["field-a", "field-b"], ["model-m"], "", "")]);
        using var root = new Folder();
        File.WriteAllText(root.Combine("Field/b/stage.smd"), "b");
        using var store = new PackStore(root.Path, Address, site);
        await store.LoadManifestAsync(CancellationToken.None);
        var missing = store.MissingForField(7);
        Assert.True(missing.Count == 1 && missing[0].Id == "field-a", "only the stage pack whose files are absent holds the field back");
        Assert.True(store.MissingMonstersOf(7) is [{ Id: "model-m" }], "the field's monsters are listed apart");
        await store.InstallAsync(missing, null, CancellationToken.None);
        Assert.True(File.ReadAllText(root.Combine("Field/a/grass.bmp")) == "grass", "the pack's files are unpacked");
        Assert.True(store.MissingForField(7).Count == 0 && store.IsInstalled(pack) && store.IsInstalled(present), "both packs count as installed");
        Assert.True(!Directory.EnumerateFiles(root.Combine(".packs")).Any(), "the downloaded zip is removed");
    }

    private static async Task RebuiltAsync()
    {
        using var site = new FakeSite();
        site.Add("field-a", ("Field/a/stage.smd", "one"));
        site.Publish(fields: [new FieldPacks(7, "a", "A", ["field-a"], [], "", "")]);
        using var root = new Folder();
        using (var store = new PackStore(root.Path, Address, site))
        {
            await store.LoadManifestAsync(CancellationToken.None);
            await store.InstallAsync(store.MissingForField(7), null, CancellationToken.None);
        }

        site.Add("field-a", ("Field/a/stage.smd", "two"));
        site.Publish(fields: [new FieldPacks(7, "a", "A", ["field-a"], [], "", "")]);
        using var again = new PackStore(root.Path, Address, site);
        await again.LoadManifestAsync(CancellationToken.None);
        Assert.True(again.MissingForField(7).Count == 1, "the rebuilt pack is missing again");
        await again.InstallAsync(again.MissingForField(7), null, CancellationToken.None);
        Assert.True(File.ReadAllText(root.Combine("Field/a/stage.smd")) == "two", "the new files replace the old");
    }

    private static async Task ResumeAsync()
    {
        using var site = new FakeSite();
        site.Add("field-a", ("Field/a/stage.smd", new string('x', 200_000)), ("Field/a/b.bmp", Convert.ToBase64String(RandomNumberGenerator.GetBytes(90_000))));
        site.Publish(fields: [new FieldPacks(7, "a", "A", ["field-a"], [], "", "")]);
        site.CutAfter = 40_000;
        using var root = new Folder();
        using var store = new PackStore(root.Path, Address, site);
        await store.LoadManifestAsync(CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => store.InstallAsync(store.MissingForField(7), null, CancellationToken.None), "the cut connection");
        site.CutAfter = 0;
        await store.InstallAsync(store.MissingForField(7), null, CancellationToken.None);
        Assert.True(site.Ranges.Count == 1 && site.Ranges[0] == 40_000, $"the second request resumed at the cut: {string.Join(',', site.Ranges)}");
        Assert.True(File.ReadAllText(root.Combine("Field/a/stage.smd")).Length == 200_000, "the resumed pack is whole");
    }

    private static async Task RefusedAsync()
    {
        using var site = new FakeSite();
        site.Add("field-a", ("Field/a/stage.smd", "stage"));
        site.Add("field-evil", ("../outside.txt", "x"));
        site.Publish(fields: [new FieldPacks(7, "a", "A", ["field-a"], [], "", ""), new FieldPacks(8, "evil", "E", ["field-evil"], [], "", "")]);
        site.Corrupt = "field-a";
        using var root = new Folder();
        using var store = new PackStore(root.Path, Address, site);
        await store.LoadManifestAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(store.MissingForField(7), null, CancellationToken.None), "a damaged pack");
        Assert.True(!File.Exists(root.Combine("Field/a/stage.smd")) && !Directory.EnumerateFiles(root.Combine(".packs")).Any(),
            "nothing of the damaged pack is kept");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(store.MissingForField(8), null, CancellationToken.None), "an escaping entry");
        Assert.True(!File.Exists(Path.Combine(Path.GetDirectoryName(root.Path)!, "outside.txt")), "no file is written outside the root");
    }

    private sealed class Folder : IDisposable
    {
        public Folder() => Path = Directory.CreateTempSubdirectory("pack-store-").FullName;

        public string Path { get; }

        public string Combine(string relative)
        {
            var path = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    // Serves packs.json and pack zips from memory, with range requests; CutAfter ends a body early once, Corrupt flips a
    // byte of one pack after its SHA-256 was published.
    private sealed class FakeSite : HttpMessageHandler
    {
        private readonly Dictionary<string, (byte[] Zip, PackEntry Entry)> _packs = new(StringComparer.Ordinal);
        private byte[] _manifest = [];

        public int CutAfter { get; set; }

        public string? Corrupt { get; set; }

        public List<long> Ranges { get; } = [];

        public PackEntry Add(string id, params (string Path, string Text)[] files)
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, text) in files)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                    writer.Write(text);
                }
            }

            var bytes = memory.ToArray();
            var entry = new PackEntry(id, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)),
                files.Select(file => new PackFile(file.Path, System.Text.Encoding.UTF8.GetByteCount(file.Text))).ToArray());
            _packs[id] = (bytes, entry);
            return entry;
        }

        public void Publish(IReadOnlyList<FieldPacks> fields)
        {
            using var memory = new MemoryStream();
            new PackManifest(PackManifest.CurrentVersion, _packs.Values.Select(pack => pack.Entry).ToArray(), fields,
                new Dictionary<string, string>()).Write(memory);
            _manifest = memory.ToArray();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.Segments[^1];
            if (name == PackManifest.FileName)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_manifest) });
            }

            var id = name[..^".zip".Length];
            if (!_packs.TryGetValue(id, out var pack))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var bytes = pack.Zip.ToArray();
            if (id == Corrupt)
            {
                bytes[bytes.Length / 2] ^= 0xFF;
            }

            var from = request.Headers.Range?.Ranges.First().From ?? 0;
            if (from > 0)
            {
                Ranges.Add(from);
            }

            var body = bytes[(int)from..];
            Stream content = new MemoryStream(body);
            if (CutAfter > 0)
            {
                content = new CutStream(body, CutAfter);
            }

            return Task.FromResult(new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(content)
            });
        }
    }

    // A body that ends in an error after a number of bytes, as a dropped connection does.
    private sealed class CutStream(byte[] body, int cut) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => body.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= cut)
            {
                throw new IOException("connection reset");
            }

            var read = Math.Min(count, Math.Min(cut, body.Length) - _position);
            Array.Copy(body, _position, buffer, offset, read);
            _position += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
