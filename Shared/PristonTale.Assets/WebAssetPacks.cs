using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PristonTale.Assets;

// World asset packs: the files bootstrap.txt lists, grouped into content-addressed ZIP chunks so the browser client
// fetches a field's thousands of files in a few dozen requests. tools/PristonTale.WebAssetPacks writes them beside the
// published client; the browser reads them before it falls back to one request per file. The index names the exact
// bootstrap.txt bytes it was built from, so packs left from another build are ignored.
public static partial class WebAssetPacks
{
    private const char ByteOrderMark = (char)0xFEFF;
    public const string BootstrapFile = "bootstrap.txt";
    public const string IndexFile = "world-packs.json";
    public const string ChunkFolder = "world-packs";
    public const int FormatVersion = 1;
    public const string Compression = "ZIP Deflate Optimal";
    public const long ChunkBudgetBytes = 8L * 1024 * 1024;

    // Bounds a raw file, a chunk's raw total and a ZIP download; a file larger than the chunk budget gets its own chunk.
    public const long MaximumBytes = 64L * 1024 * 1024;
    public const int MaximumIndexBytes = 8 * 1024 * 1024;

    // ZIP stores DOS times, which start in 1980: a fixed stamp keeps the chunks' bytes, and so their names, reproducible.
    public static readonly DateTimeOffset EntryTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
    };

    public sealed record PackIndex(int Version, string BootstrapSha256, long ChunkBudgetBytes, string Compression, PackChunk[] Chunks, PackedFile[] Files);

    // Bytes: the ZIP's size; RawBytes: its files' total.
    public sealed record PackChunk(string Path, string Sha256, long Bytes, long RawBytes, int Files);

    public sealed record PackedFile(string Path, long Bytes, string Sha256, int Chunk);

    public static string ChunkPath(string sha256) => ChunkFolder + "/" + sha256 + ".zip";

    // The lines of bootstrap.txt as the publish writes it (UTF-8, CRLF or LF); the index names the hash of these bytes.
    public static string[] BootstrapLines(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes).TrimStart(ByteOrderMark).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // bootstrap.txt: one "path<TAB>SHA-256" line per file; a line repeated exactly counts once. Null when a line is
    // malformed, a path is unsafe, one path has two hashes, or two paths differ only by case (one file system keeps both,
    // another merges them).
    public static Dictionary<string, string>? ReadBootstrap(IEnumerable<string> lines)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var separator = line.IndexOf('\t');
            if (separator <= 0)
            {
                return null;
            }

            var path = line[..separator];
            var hash = line[(separator + 1)..].ToUpperInvariant();
            if (hashes.TryGetValue(path, out var listed))
            {
                if (listed != hash)
                {
                    return null;
                }

                continue;
            }

            if (!IsSafePath(path) || !IsHash(hash) || !folded.Add(path))
            {
                return null;
            }

            hashes.Add(path, hash);
        }

        return hashes;
    }

    // A client file's path stays inside the client folder: relative, '/'-separated, without empty, "." or ".." segments,
    // backslashes, drive or stream separators, control characters, or '%' (served paths are already unescaped).
    public static bool IsSafePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains(':') || path.Contains('%') || path.Any(char.IsControl))
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsHash(string? value)
    {
        if (value is not { Length: 64 })
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    public static bool HashEquals(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    public static string LocalPath(string root, string path)
    {
        if (!IsSafePath(path))
        {
            throw new InvalidDataException($"A path leaves the client folder: {path}");
        }

        return Path.Combine([root, .. path.Split('/')]);
    }

    // Checks the index against the bootstrap it claims and returns each chunk's files by exact path. Every listed file
    // is packed once, with the bootstrap's spelling and hash, and the counts and sizes add up.
    public static Dictionary<string, PackedFile>[] Check([NotNull] PackIndex? index, string bootstrapSha256, IReadOnlyDictionary<string, string> bootstrap)
    {
        if (index is null || index.Version != FormatVersion || !HashEquals(index.BootstrapSha256, bootstrapSha256)
            || index.ChunkBudgetBytes is <= 0 or > MaximumBytes || index.Compression != Compression
            || index.Chunks.Length == 0 || index.Files.Length != bootstrap.Count || index.Chunks.Length > index.Files.Length)
        {
            throw new InvalidDataException("The pack index does not match this bootstrap.");
        }

        var chunks = new Dictionary<string, PackedFile>[index.Chunks.Length];
        var rawBytes = new long[chunks.Length];
        var chunkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var at = 0; at < chunks.Length; at++)
        {
            var chunk = index.Chunks[at];
            if (chunk is null || !IsHash(chunk.Sha256) || chunk.Path != ChunkPath(chunk.Sha256) || !chunkNames.Add(chunk.Path)
                || chunk.Bytes is <= 0 or > MaximumBytes || chunk.RawBytes is < 0 or > MaximumBytes || chunk.Files <= 0
                || chunk.RawBytes > index.ChunkBudgetBytes && chunk.Files != 1)
            {
                throw new InvalidDataException("A pack chunk is out of bounds.");
            }

            chunks[at] = new Dictionary<string, PackedFile>(chunk.Files, StringComparer.Ordinal);
        }

        var fileNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in index.Files)
        {
            if (file is null || !bootstrap.TryGetValue(file.Path, out var expected) || !HashEquals(file.Sha256, expected)
                || file.Bytes is < 0 or > MaximumBytes || file.Chunk < 0 || file.Chunk >= chunks.Length
                || !chunks[file.Chunk].TryAdd(file.Path, file) || !fileNames.Add(file.Path))
            {
                throw new InvalidDataException("A packed file is unknown, repeated or out of bounds.");
            }

            rawBytes[file.Chunk] = checked(rawBytes[file.Chunk] + file.Bytes);
        }

        for (var at = 0; at < chunks.Length; at++)
        {
            if (rawBytes[at] != index.Chunks[at].RawBytes || chunks[at].Count != index.Chunks[at].Files)
            {
                throw new InvalidDataException("A pack chunk's sizes or counts differ from its files.");
            }
        }

        return chunks;
    }

    // A chunk holds exactly its listed files, each once, with the listed size.
    public static void CheckArchive(ZipArchive archive, IReadOnlyDictionary<string, PackedFile> files)
    {
        if (archive.Entries.Count != files.Count)
        {
            throw new InvalidDataException("A pack chunk's entries differ from its index.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!files.TryGetValue(entry.FullName, out var file) || !seen.Add(entry.FullName) || entry.Length != file.Bytes)
            {
                throw new InvalidDataException("A pack chunk holds an unlisted, repeated or resized entry.");
            }
        }
    }
}
