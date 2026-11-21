using System.Text;
using PristonTale.Assets;
using PristonTale.Gameplay.Audio;
using PristonTale.Web.Services;

namespace PristonTale.Tests;

internal static class SoundFilesTests
{
    private static readonly string HashA = new('A', 64);
    private static readonly string HashB = new('B', 64);

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the browser's sound index lists a folder's waves in order and finds a file whatever its case", SoundIndexAsync);
        yield return ("the browser's sound index refuses paths outside assets and malformed lines", SoundIndexRefusalsAsync);
        yield return ("a sound whose file arrives on demand is null until its read finishes, then created once", OnDemandLoaderAsync);
        yield return ("a sound whose file cannot be read stays null and is reported once", FailedLoaderAsync);
        yield return ("the disk sound source lists a folder's waves as asset-relative paths in case-insensitive order", DiskWavesAsync);
        yield return ("the site serves the music's .bgm and the sounds' .wav files", SoundContentTypesAsync);
    }

    private static WebSoundIndex Index(params string[] lines) =>
        WebSoundIndex.Read(Encoding.UTF8.GetBytes(string.Join('\n', lines))) ?? throw new InvalidOperationException("the index was refused");

    private static Task SoundIndexAsync()
    {
        var index = Index(
            $"assets/wav/Effects/weapon/two hand swing axe.wav\t{HashA}",
            $"assets/wav/Effects/weapon/One hand miss.wav\t{HashB}",
            $"assets/wav/Effects/weapon/notes.txt\t{HashB}",
            $"assets/wav/BGM/Ice 1.bgm\t{HashA}",
            $"assets/wav/CharacterDelete.WAV\t{HashB}");
        Assert.True(index.Count == 5, "every listed file is kept");
        Assert.True(index.Waves("wav/Effects/weapon").SequenceEqual(["wav/Effects/weapon/One hand miss.wav", "wav/Effects/weapon/two hand swing axe.wav"]),
            "a folder lists its .wav files only, as asset-relative paths in case-insensitive order");
        Assert.True(index.Waves(@"WAV\EFFECTS\Weapon\").Count == 2, "a folder is found whatever its separators and case");
        Assert.True(index.Waves("wav").SequenceEqual(["wav/CharacterDelete.WAV"]), "an upper-case extension is a wave too");
        Assert.True(index.Waves("wav/Talk").Count == 0, "an absent folder lists nothing");
        Assert.True(index.TryFind(@"WAV\bgm\ice 1.BGM", out var path, out var hash) && path == "assets/wav/BGM/Ice 1.bgm" && hash == HashA,
            "a file is found with the index's spelling and hash");
        Assert.True(!index.TryFind("wav/rain01.wav", out _, out _), "an unlisted file is not found");
        Assert.True(WebSoundIndex.Empty.Count == 0 && WebSoundIndex.Empty.Waves("wav").Count == 0, "the empty index lists nothing");
        return Task.CompletedTask;
    }

    private static Task SoundIndexRefusalsAsync()
    {
        Assert.True(WebSoundIndex.Read(Encoding.UTF8.GetBytes($"Content/Fonts/UI.xnb\t{HashA}")) is null, "a file outside assets/ is refused");
        Assert.True(WebSoundIndex.Read(Encoding.UTF8.GetBytes($"assets/\t{HashA}")) is null, "the assets folder itself is refused");
        Assert.True(WebSoundIndex.Read(Encoding.UTF8.GetBytes("assets/wav/rain01.wav")) is null, "a line without a hash is refused");
        Assert.True(WebSoundIndex.Read(Encoding.UTF8.GetBytes($"assets/wav/../rain01.wav\t{HashA}")) is null, "a path leaving the folder is refused");
        Assert.True(WebSoundIndex.Read([]) is { Count: 0 }, "an empty list is an empty index");
        return Task.CompletedTask;
    }

    private static Task OnDemandLoaderAsync()
    {
        var reads = new Dictionary<string, TaskCompletionSource<byte[]>>();
        var created = 0;
        var loader = new SoundLoader<string>(
            path =>
            {
                var read = new TaskCompletionSource<byte[]>();
                reads.Add(path, read);
                return read.Task;
            },
            wave =>
            {
                created++;
                return Encoding.UTF8.GetString(wave);
            },
            (_, _) => throw new InvalidOperationException("no read failed"));

        Assert.True(loader.Get("wav/BGM/Ice 1.bgm") is null, "a sound is null while its file is read");
        Assert.True(loader.Get("WAV/bgm/ice 1.bgm") is null && reads.Count == 1, "asking again, in any case, starts no second read");
        loader.Request("wav/rain01.wav");
        Assert.True(reads.Count == 2 && created == 0, "a request starts the read without creating the sound");

        reads["wav/BGM/Ice 1.bgm"].SetResult("music"u8.ToArray());
        Assert.True(loader.Get("wav/BGM/Ice 1.bgm") == "music" && created == 1, "the next ask after the read creates the sound");
        Assert.True(loader.Get("wav/BGM/Ice 1.bgm") == "music" && created == 1, "the sound is created once");
        Assert.True(loader.Get(null) is null && loader.Get(string.Empty) is null && reads.Count == 2, "no path is no sound and no read");
        Assert.True(loader.Created.SequenceEqual(["music"]), "only created sounds are listed");

        var disk = new SoundLoader<string>(static path => Task.FromResult(Encoding.UTF8.GetBytes(path)), Encoding.UTF8.GetString,
            (_, _) => throw new InvalidOperationException("no read failed"));
        Assert.True(disk.Load("wav/thunder1.wav") == "wav/thunder1.wav" && disk.Get("wav/thunder1.wav") == "wav/thunder1.wav",
            "a read from disk creates the sound at once");
        return Task.CompletedTask;
    }

    private static Task FailedLoaderAsync()
    {
        var failures = new List<string>();
        var loader = new SoundLoader<string>(
            static path => Task.FromException<byte[]>(new FileNotFoundException(path)),
            static _ => throw new InvalidOperationException("nothing is created"),
            (path, error) => failures.Add($"{path}: {error.GetType().Name}"));

        Assert.True(loader.Get("wav/missing.wav") is null, "the first ask starts the read");
        Assert.True(loader.Get("wav/missing.wav") is null && loader.Get("wav/missing.wav") is null, "a failed read leaves the sound null");
        Assert.True(failures.SequenceEqual(["wav/missing.wav: FileNotFoundException"]), "the failure is reported once");
        Assert.Throws<FileNotFoundException>(() => loader.Load("wav/other.wav"), "a failed read from disk throws");
        return Task.CompletedTask;
    }

    private static Task DiskWavesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "claude", "etervale-sound-files-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(root, "wav", "Effects", "weapon");
            Directory.CreateDirectory(folder);
            foreach (var name in new[] { "two hand swing axe.wav", "One hand miss.wav", "notes.txt" })
            {
                File.WriteAllBytes(Path.Combine(folder, name), []);
            }

            var waves = DiskSoundFiles.Instance.Waves(root, "wav/Effects/weapon");
            Assert.True(waves.SequenceEqual(["wav/Effects/weapon/One hand miss.wav", "wav/Effects/weapon/two hand swing axe.wav"]),
                $"the disk lists {string.Join(", ", waves)}");
            Assert.True(DiskSoundFiles.Instance.Waves(root, "wav/Talk").Count == 0, "an absent folder lists nothing");
            Assert.True(!DiskSoundFiles.Instance.OnDemand && DiskSoundFiles.Instance.EnsureAsync(root, "wav/x.wav").IsCompletedSuccessfully,
                "files on disk are there at once");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task SoundContentTypesAsync()
    {
        var types = BrowserClient.ContentTypes();
        Assert.True(types.TryGetContentType("/play/assets/wav/BGM/Ice 1.bgm", out var bgm) && bgm == "application/octet-stream",
            "the music's .bgm is served");
        Assert.True(types.TryGetContentType("/play/assets/wav/rain01.wav", out var wav) && wav.StartsWith("audio/", StringComparison.Ordinal),
            "a .wav is served as audio");
        return Task.CompletedTask;
    }
}
