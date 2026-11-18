using System.Text.Json;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using PristonTale.Web.Services;

namespace PristonTale.Web.Hosting;

internal sealed class WebCompressedGameFiles
{
    internal const string IndexName = "browser-compressed-assets.json";
    private readonly string _root;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public WebCompressedGameFiles(BrowserClient client, ILogger<WebCompressedGameFiles> logger)
    {
        _root = client.Root;
        var index = Path.Combine(_root, IndexName);
        if (!File.Exists(index))
        {
            return;
        }

        try
        {
            var document = JsonSerializer.Deserialize<AssetIndex>(File.ReadAllText(index), Json);
            if (document is not { Version: 1, Files: not null })
            {
                logger.LogWarning("Unsupported compressed game asset index; using normal file responses.");
                return;
            }

            var types = BrowserClient.ContentTypes();
            foreach (var file in document.Files)
            {
                if (file is null || !SafeRelative(file.Path) || !IsEligible(new PathString(BrowserClient.RequestPath + "/" + file.Path))
                    || !ValidHash(file.RawSha256) || file.RawBytes < 0 || !types.TryGetContentType(file.Path, out var contentType))
                {
                    continue;
                }

                _entries.TryAdd(file.Path, new Entry(file.RawSha256, contentType, file.LastModifiedUtc,
                    VariantOf(file.Br, file.Path + ".br"), VariantOf(file.Gzip, file.Path + ".gz")));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("Compressed game asset index unavailable; using normal file responses.");
        }
    }

    internal static bool IsEligible(PathString path)
    {
        if (!path.StartsWithSegments(BrowserClient.RequestPath + "/assets", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments(BrowserClient.RequestPath + "/Content", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!SafeRelative(path.Value![(BrowserClient.RequestPath.Length + 1)..]))
        {
            return false;
        }

        var extension = Path.GetExtension(path.Value ?? string.Empty);
        return extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".smd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".smb", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xnb", StringComparison.OrdinalIgnoreCase);
    }

    internal async Task<bool> TryServeAsync(HttpContext context)
    {
        var request = context.Request;
        if ((!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) || !IsEligible(request.Path))
        {
            return false;
        }

        ApplyVary(context.Response.Headers);

        var relative = request.Path.Value![(BrowserClient.RequestPath.Length + 1)..];
        if (!SafeRelative(relative) || request.Headers.ContainsKey(HeaderNames.Range) || request.Headers.ContainsKey(HeaderNames.IfRange)
            || !request.Query.TryGetValue(BrowserClient.VersionQuery, out var version) || version.Count != 1
            || !_entries.TryGetValue(relative, out var entry) || !string.Equals(version[0], entry.RawSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encoding = Negotiate(request.Headers.AcceptEncoding, entry.Br is not null, entry.Gzip is not null,
            entry.Br?.Bytes ?? long.MaxValue, entry.Gzip?.Bytes ?? long.MaxValue);
        var variant = encoding == "br" ? entry.Br : encoding == "gzip" ? entry.Gzip : null;
        if (variant is null)
        {
            return false;
        }

        var info = new FileInfo(variant.Path);
        if (!info.Exists || info.Length != variant.Bytes)
        {
            return false;
        }

        context.Response.Headers.ContentEncoding = encoding;
        context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        await Results.File(variant.Path, entry.ContentType, lastModified: entry.LastModifiedUtc, entityTag: variant.Tag).ExecuteAsync(context);
        return true;
    }

    internal static string? Negotiate(StringValues header, bool brotli, bool gzip, long brotliBytes = long.MaxValue, long gzipBytes = long.MaxValue)
    {
        if (!StringWithQualityHeaderValue.TryParseList(header, out var values) || values.Count == 0)
        {
            return null;
        }

        double? br = null, gz = null, wildcard = null, identity = null;
        foreach (var value in values)
        {
            var quality = value.Quality ?? 1;
            if (value.Value.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                br = quality;
            }
            else if (value.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            {
                gz = quality;
            }
            else if (value.Value.Equals("identity", StringComparison.OrdinalIgnoreCase))
            {
                identity = quality;
            }
            else if (value.Value.Equals("*", StringComparison.Ordinal))
            {
                wildcard = quality;
            }
        }

        var brQuality = brotli ? br ?? wildcard ?? 0 : 0;
        var gzipQuality = gzip ? gz ?? wildcard ?? 0 : 0;
        var minimum = identity ?? 0;
        if (brQuality > 0 && brQuality >= minimum && (brQuality > gzipQuality || brQuality == gzipQuality && brotliBytes <= gzipBytes))
        {
            return "br";
        }
        if (gzipQuality > 0 && gzipQuality >= minimum)
        {
            return "gzip";
        }
        return null;
    }

    internal static void ApplyVary(IHeaderDictionary headers)
    {
        if (!headers.GetCommaSeparatedValues(HeaderNames.Vary).Contains(HeaderNames.AcceptEncoding, StringComparer.OrdinalIgnoreCase))
        {
            headers.AppendCommaSeparatedValues(HeaderNames.Vary, HeaderNames.AcceptEncoding);
        }
    }

    private Variant? VariantOf(CompressedVariant? value, string relative) => value is { Bytes: > 0 } && ValidHash(value.Sha256)
        ? new Variant(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)), value.Bytes, new EntityTagHeaderValue('"' + value.Sha256 + '"')) : null;

    private static bool SafeRelative(string? path) => !string.IsNullOrEmpty(path) && !Path.IsPathRooted(path) && !path.Contains('\\') && !path.Contains(':')
        && !path.Contains('%') && !path.Split('/').Any(static part => part is ".." or "." or "");

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(static character => char.IsAsciiHexDigit(character));
    private sealed record Variant(string Path, long Bytes, EntityTagHeaderValue Tag);
    private sealed record Entry(string RawSha256, string ContentType, DateTimeOffset LastModifiedUtc, Variant? Br, Variant? Gzip);
    internal sealed record CompressedVariant(string Sha256, long Bytes);
    internal sealed record CompressedAsset(string Path, string RawSha256, long RawBytes, DateTimeOffset LastModifiedUtc, CompressedVariant? Br, CompressedVariant? Gzip);
    internal sealed record AssetIndex(int Version, IReadOnlyList<CompressedAsset> Files);
}
