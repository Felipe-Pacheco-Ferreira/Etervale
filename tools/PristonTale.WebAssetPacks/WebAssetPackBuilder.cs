using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PristonTale.Assets;
using static PristonTale.Assets.WebAssetPacks;

namespace PristonTale.WebAssetPacks;

public sealed record PackBuildReport(string BootstrapSha256, int Files, int Chunks, long RawBytes, long ZipBytes, long LargestChunkBytes,
    int OversizedChunks, double Milliseconds);

// Reads a published client folder (Play:ClientPath) and writes its world packs into a new folder: the chunks first, then
// the report, then the index, so a copy that stops early has no index and the browser keeps its per-file downloads.
public static class WebAssetPackBuilder
{
    public const string ReportFile = "pack-build-report.json";
    private const string ListFile = BootstrapFile;
    private const int BufferBytes = 128 * 1024;

    public static async Task<PackBuildReport> BuildAsync(string root, string output, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        if (Directory.Exists(output) || File.Exists(output))
        {
            throw new IOException($"The output must be a new folder: {output}");
        }

        var listPath = Path.Combine(root, ListFile);
        var listBytes = await File.ReadAllBytesAsync(listPath, cancellationToken);
        var bootstrapSha256 = Convert.ToHexString(SHA256.HashData(listBytes));
        var bootstrap = ReadBootstrap(BootstrapLines(listBytes))
            ?? throw new InvalidDataException($"{ListFile} has a malformed line, an unsafe path or two paths that differ only by case.");
        if (bootstrap.Count == 0)
        {
            throw new InvalidDataException($"{ListFile} lists no files.");
        }

        var groups = Group(root, bootstrap.Keys.Order(StringComparer.Ordinal));
        Directory.CreateDirectory(Path.Combine(output, ChunkFolder));
        var buffer = new byte[BufferBytes];
        var chunks = new List<PackChunk>(groups.Count);
        var files = new List<PackedFile>(bootstrap.Count);
        foreach (var group in groups)
        {
            var chunk = await WriteChunkAsync(root, output, group, bootstrap, buffer, cancellationToken);
            foreach (var (path, bytes) in group)
            {
                files.Add(new PackedFile(path, bytes, bootstrap[path], chunks.Count));
            }

            chunks.Add(chunk);
        }

        var index = new PackIndex(FormatVersion, bootstrapSha256, ChunkBudgetBytes, Compression, chunks.ToArray(), files.ToArray());
        await VerifyAsync(output, index, bootstrapSha256, bootstrap, buffer, cancellationToken);
        if (!HashEquals(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(listPath, cancellationToken))), bootstrapSha256))
        {
            throw new IOException($"{ListFile} changed while it was packed.");
        }

        var report = new PackBuildReport(bootstrapSha256, files.Count, chunks.Count, chunks.Sum(chunk => chunk.RawBytes), chunks.Sum(chunk => chunk.Bytes),
            chunks.Max(chunk => chunk.Bytes), chunks.Count(chunk => chunk.RawBytes > ChunkBudgetBytes), Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        await WriteJsonAsync(Path.Combine(output, ReportFile), report, cancellationToken);
        await WriteJsonAsync(Path.Combine(output, IndexFile), index, cancellationToken);
        return report;
    }

    // Files in path order, so one folder's files share chunks; a chunk closes once it reaches the budget, and a file
    // larger than the budget gets a chunk of its own.
    private static List<List<(string Path, long Bytes)>> Group(string root, IEnumerable<string> paths)
    {
        var groups = new List<List<(string Path, long Bytes)>>();
        var group = new List<(string Path, long Bytes)>();
        long groupBytes = 0;
        foreach (var path in paths)
        {
            var bytes = new FileInfo(LocalPath(root, path)).Length;
            if (bytes > MaximumBytes)
            {
                throw new InvalidDataException($"A listed file exceeds {MaximumBytes} bytes: {path}");
            }

            if (group.Count > 0 && groupBytes + bytes > ChunkBudgetBytes)
            {
                groups.Add(group);
                group = [];
                groupBytes = 0;
            }

            group.Add((path, bytes));
            groupBytes += bytes;
        }

        if (group.Count > 0)
        {
            groups.Add(group);
        }

        return groups;
    }

    private static async Task<PackChunk> WriteChunkAsync(string root, string output, List<(string Path, long Bytes)> group,
        Dictionary<string, string> bootstrap, byte[] buffer, CancellationToken cancellationToken)
    {
        var pending = Path.Combine(output, ChunkFolder, "pending.zip");
        await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in group)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                entry.LastWriteTime = EntryTimestamp;
                entry.ExternalAttributes = 0;
                await using var source = new FileStream(LocalPath(root, path), FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, useAsync: true);
                await using var target = entry.Open();
                var (copied, hash) = await CopyAndHashAsync(source, target, MaximumBytes, buffer, cancellationToken);
                if (copied != bytes || !HashEquals(hash, bootstrap[path]))
                {
                    throw new InvalidDataException($"A file differs from bootstrap.txt: {path}");
                }
            }
        }

        var sha256 = await HashFileAsync(pending, cancellationToken);
        var name = ChunkPath(sha256);
        var final = LocalPath(output, name);
        File.Move(pending, final, overwrite: false);
        var zipBytes = new FileInfo(final).Length;
        if (zipBytes > MaximumBytes)
        {
            throw new InvalidDataException($"A chunk exceeds {MaximumBytes} bytes: {name}");
        }

        return new PackChunk(name, sha256, zipBytes, group.Sum(file => file.Bytes), group.Count);
    }

    // Reads the output back through the browser's own checks and inflates every entry against its bootstrap hash.
    private static async Task VerifyAsync(string output, PackIndex index, string bootstrapSha256, Dictionary<string, string> bootstrap, byte[] buffer,
        CancellationToken cancellationToken)
    {
        var chunks = Check(index, bootstrapSha256, bootstrap);
        for (var at = 0; at < chunks.Length; at++)
        {
            var path = LocalPath(output, index.Chunks[at].Path);
            if (!HashEquals(await HashFileAsync(path, cancellationToken), index.Chunks[at].Sha256))
            {
                throw new InvalidDataException($"A chunk changed after it was written: {index.Chunks[at].Path}");
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, useAsync: true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            CheckArchive(archive, chunks[at]);
            foreach (var entry in archive.Entries)
            {
                var file = chunks[at][entry.FullName];
                await using var source = entry.Open();
                var (bytes, hash) = await CopyAndHashAsync(source, Stream.Null, file.Bytes, buffer, cancellationToken);
                if (bytes != file.Bytes || !HashEquals(hash, file.Sha256))
                {
                    throw new InvalidDataException($"A packed file does not inflate to its bootstrap hash: {entry.FullName}");
                }
            }
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var pending = path + ".tmp";
        await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        }

        File.Move(pending, path, overwrite: false);
    }
}
