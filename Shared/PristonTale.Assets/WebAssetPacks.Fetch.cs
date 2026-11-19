using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PristonTale.Assets;

public static partial class WebAssetPacks
{
    private const int BufferBytes = 128 * 1024;
    private const int ParallelChunks = 2;

    // The browser runs the game and this download on one thread: hand it back to the page this often, shared by both
    // chunk workers, so frames keep coming without a timer per file. Browsers delay a chained timer by at least 4 ms, so
    // a shorter slice would leave the download idle half the time.
    private static readonly TimeSpan YieldInterval = TimeSpan.FromMilliseconds(12);

    // Writes the files of `required` that are not under `root` yet, from the chunks that hold them. False when the
    // packs are absent, belong to another bootstrap or fail a check: every file written by then came whole from a checked chunk and
    // the caller's per-file download skips it. The caller's cancellation is thrown, never turned into a fallback.
    public static async Task<bool> TryFetchAsync(HttpClient http, string root, string bootstrapSha256, IReadOnlyDictionary<string, string> bootstrap,
        IEnumerable<string> required, Action<int, int>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wanted = new HashSet<string>(required, StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in wanted)
        {
            if (!bootstrap.ContainsKey(path))
            {
                return false;
            }

            if (!File.Exists(LocalPath(root, path)))
            {
                missing.Add(path);
            }
        }

        var total = wanted.Count;
        var ready = total - missing.Count;
        progress?.Invoke(ready, total);
        if (missing.Count == 0)
        {
            return true;
        }

        using var failed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var index = await ReadIndexAsync(http, failed.Token);
            var chunks = Check(index, bootstrapSha256, bootstrap);
            var listed = index.Chunks;
            var needed = new List<int>();
            for (var at = 0; at < chunks.Length; at++)
            {
                foreach (var path in chunks[at].Keys)
                {
                    if (missing.Contains(path))
                    {
                        needed.Add(at);
                        break;
                    }
                }
            }

            var pause = new PageYield();
            var next = -1;
            async Task WorkAsync()
            {
                var buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
                try
                {
                    int at;
                    while ((at = Interlocked.Increment(ref next)) < needed.Count)
                    {
                        var id = needed[at];
                        var chunk = listed[id];
                        using var body = await DownloadChunkAsync(http, chunk, buffer, pause, failed.Token);
                        using var archive = new ZipArchive(body, ZipArchiveMode.Read);
                        CheckArchive(archive, chunks[id]);
                        foreach (var entry in archive.Entries)
                        {
                            if (!missing.Contains(entry.FullName))
                            {
                                continue;
                            }

                            await ExtractAsync(entry, chunks[id][entry.FullName], LocalPath(root, entry.FullName), buffer, pause, failed.Token);
                            // Counted and reported together, so reports never arrive out of order.
                            lock (missing)
                            {
                                progress?.Invoke(++ready, total);
                            }
                        }
                    }
                }
                catch
                {
                    // Stop the other worker before the caller falls back to per-file downloads.
                    await failed.CancelAsync();
                    throw;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            var workers = new Task[Math.Min(ParallelChunks, needed.Count)];
            for (var at = 0; at < workers.Length; at++)
            {
                workers[at] = WorkAsync();
            }

            // WhenAll waits for both workers, so nothing still writes when this returns.
            await Task.WhenAll(workers);
            foreach (var path in missing)
            {
                if (!File.Exists(LocalPath(root, path)))
                {
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException
            or NotSupportedException or UnauthorizedAccessException or OperationCanceledException or AggregateException)
        {
            // A worker can fail as the caller cancels; the cancellation still wins.
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static async Task<PackIndex?> ReadIndexAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(IndexFile, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumIndexBytes)
        {
            throw new InvalidDataException("The pack index is too large.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
        try
        {
            await CopyAndHashAsync(stream, body, MaximumIndexBytes, buffer, null, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        body.Position = 0;
        return await JsonSerializer.DeserializeAsync<PackIndex>(body, JsonOptions, cancellationToken);
    }

    private static async Task<MemoryStream> DownloadChunkAsync(HttpClient http, PackChunk chunk, byte[] buffer, PageYield pause, CancellationToken cancellationToken)
    {
        // The hash names this version of the chunk, so the site lets the browser keep it for good.
        using var response = await http.GetAsync(chunk.Path + "?v=" + chunk.Sha256, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = new MemoryStream(checked((int)chunk.Bytes));
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var (bytes, hash) = await CopyAndHashAsync(stream, body, chunk.Bytes, buffer, pause, cancellationToken);
            if (bytes != chunk.Bytes || !HashEquals(hash, chunk.Sha256))
            {
                throw new InvalidDataException("A pack chunk's size or hash differs from its index.");
            }

            body.Position = 0;
            return body;
        }
        catch
        {
            await body.DisposeAsync();
            throw;
        }
    }

    // The chunk's hash already vouches for every entry (the generator inflated each one against its bootstrap hash), so
    // a file is not hashed again here: the browser's managed hashing is slow and would double the work. It
    // reaches its path only once its whole content arrived with the listed size.
    private static async Task ExtractAsync(ZipArchiveEntry entry, PackedFile file, string target, byte[] buffer, PageYield pause,
        CancellationToken cancellationToken)
    {
        if (File.Exists(target))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Its own name: an abandoned world entry can still be writing while the next one starts.
        var partial = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var source = entry.Open())
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1))
            {
                if (await CopyAsync(source, output, file.Bytes, buffer, pause, null, cancellationToken) != file.Bytes)
                {
                    throw new InvalidDataException($"A packed file's size differs from the bootstrap: {file.Path}");
                }
            }

            try
            {
                File.Move(partial, target, overwrite: false);
            }
            catch (IOException) when (File.Exists(target))
            {
                // A per-file download wrote the same checked file meanwhile.
            }
        }
        finally
        {
            File.Delete(partial);
        }
    }

    // Copies at most `limit` bytes (one more fails) and returns the count and the SHA-256 of what was copied.
    public static Task<(long Bytes, string Sha256)> CopyAndHashAsync(Stream source, Stream target, long limit, byte[] buffer,
        CancellationToken cancellationToken) =>
        CopyAndHashAsync(source, target, limit, buffer, null, cancellationToken);

    private static async Task<(long Bytes, string Sha256)> CopyAndHashAsync(Stream source, Stream target, long limit, byte[] buffer,
        PageYield? pause, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = await CopyAsync(source, target, limit, buffer, pause, hash, cancellationToken);
        return (bytes, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static async Task<long> CopyAsync(Stream source, Stream target, long limit, byte[] buffer, PageYield? pause, IncrementalHash? hash,
        CancellationToken cancellationToken)
    {
        long bytes = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            bytes += count;
            if (bytes > limit)
            {
                throw new InvalidDataException("A stream is longer than its declared size.");
            }

            hash?.AppendData(buffer, 0, count);
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            if (pause is not null)
            {
                await pause.WhenDueAsync(cancellationToken);
            }
        }

        return bytes;
    }

    private sealed class PageYield
    {
        private long _since = Stopwatch.GetTimestamp();

        public async ValueTask WhenDueAsync(CancellationToken cancellationToken)
        {
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _since)) < YieldInterval)
            {
                return;
            }

            Interlocked.Exchange(ref _since, Stopwatch.GetTimestamp());
            await Task.Delay(1, cancellationToken);
        }
    }
}
