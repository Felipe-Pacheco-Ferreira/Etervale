using PristonTale.Assets;
using PristonTale.Gameplay.Audio;

namespace PristonTale.Browser;

// The game's sound files in the browser: listed by audio.txt (WebSoundIndex) and downloaded from the site one at a
// time, the first time the game reads each, into the runtime's file system. They are never part of a download wave or
// a world pack: the whole set is about 300 MB, of which a session hears a small part.
internal sealed class BrowserSoundFiles(WebSoundIndex index) : ISoundFileSource
{
    // Sound downloads in flight at once, beside the world's own.
    private const int ParallelDownloads = 4;

    // One thread: the downloads interleave at their awaits, so the table needs no locking.
    private readonly Dictionary<string, Task> _downloads = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _slots = new(ParallelDownloads);

    public bool OnDemand => true;

    public IReadOnlyList<string> Waves(string assetRoot, string relativeDirectory) => index.Waves(relativeDirectory);

    public Task EnsureAsync(string assetRoot, string relativePath)
    {
        if (!index.TryFind(relativePath, out var path, out var hash))
        {
            return Task.FromException(new FileNotFoundException($"{relativePath} is not listed in {WebSoundIndex.FileName}."));
        }

        if (!_downloads.TryGetValue(path, out var download))
        {
            download = DownloadAsync(path + "\t" + hash);
            _downloads[path] = download;
        }

        return download;
    }

    private async Task DownloadAsync(string entry)
    {
        await _slots.WaitAsync();
        try
        {
            await BootstrapFiles.FetchListedFileAsync(entry);
        }
        finally
        {
            _slots.Release();
        }
    }
}
