using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using PristonTale.Web.Hosting;
using PristonTale.Web.Services;

namespace PristonTale.Tests;

internal static class WebCompressedGameFilesTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("website compressed assets: negotiated sidecars preserve every decoded byte and format", FormatsAsync);
        yield return ("website compressed assets: HEAD, validators and raw range retain HTTP semantics", ValidatorsAsync);
        yield return ("website compressed assets: missing or stale sidecars fall back without changing raw assets", FallbackAsync);
        yield return ("website compressed assets: missing sidecars retain raw validators without dynamic representation collisions", RawFallbackValidatorsAsync);
        yield return ("website compressed assets: malformed indexes preserve raw file and private endpoint handling", InvalidIndexAndPrivatePathAsync);
        yield return ("website compressed assets: explicit encoding exclusions override wildcard and unsafe paths", NegotiationAsync);
    }

    private static async Task FormatsAsync()
    {
        using var fixture = new Fixture();
        foreach (var extension in new[] { ".bmp", ".tga", ".smd", ".smb", ".xnb" })
        {
            foreach (var encoding in new[] { "br", "gzip", "identity" })
            {
                var context = await fixture.RequestAsync(extension, encoding);
                Assert.True(context.Response.StatusCode == 200, "asset request failed");
                var data = Decode(((MemoryStream)context.Response.Body).ToArray(), context.Response.Headers.ContentEncoding.ToString());
                Assert.True(data.SequenceEqual(fixture.Bytes), $"{extension}/{encoding} changed decoded bytes or its signature");
                Assert.True(context.Response.ContentType == (extension == ".bmp" ? "image/bmp" : "application/octet-stream"), "the original MIME type was lost");
                Assert.True(context.Response.Headers.Vary.ToString().Contains("Accept-Encoding", StringComparison.OrdinalIgnoreCase), "representation cache variants were merged");
                Assert.True(context.Response.Headers.CacheControl.ToString().Contains("immutable", StringComparison.Ordinal), "versioned asset caching was lost");
                Assert.True(context.Response.Headers.ContentEncoding.ToString() == (encoding == "identity" ? "" : encoding), "wrong content encoding");
            }
        }
    }

    private static async Task ValidatorsAsync()
    {
        using var fixture = new Fixture();
        var br = await fixture.RequestAsync(".bmp", "br");
        var head = await fixture.RequestAsync(".bmp", "br", "HEAD");
        Assert.True(head.Response.Body.Length == 0 && head.Response.ContentLength == br.Response.Body.Length, "HEAD did not describe the encoded representation");
        var notModified = await fixture.RequestAsync(".bmp", "br", headers: new Dictionary<string, string> { ["If-None-Match"] = br.Response.Headers.ETag.ToString() });
        Assert.True(notModified.Response.StatusCode == 304 && notModified.Response.Body.Length == 0, "matching sidecar ETag did not return an empty 304");
        var gzip = await fixture.RequestAsync(".bmp", "gzip", headers: new Dictionary<string, string> { ["If-None-Match"] = br.Response.Headers.ETag.ToString() });
        Assert.True(gzip.Response.StatusCode == 200 && gzip.Response.Headers.ETag != br.Response.Headers.ETag, "different encodings reused a strong ETag");
        var range = await fixture.RequestAsync(".bmp", "br,gzip", headers: new Dictionary<string, string> { ["Range"] = "bytes=0-15" });
        Assert.True(range.Response.StatusCode == 206 && range.Response.Headers.ContentEncoding.Count == 0
            && ((MemoryStream)range.Response.Body).ToArray().SequenceEqual(fixture.Bytes[..16]), "range processing did not use the original bytes");
        var raw = await fixture.RequestAsync(".bmp", "identity");
        Assert.True(raw.Response.Headers.ETag != br.Response.Headers.ETag && raw.Response.Headers.ETag != gzip.Response.Headers.ETag,
            "raw and encoded bytes reused a strong validator");
        var ifRange = await fixture.RequestAsync(".bmp", "br", headers: new Dictionary<string, string>
        {
            ["Range"] = "bytes=0-15", ["If-Range"] = raw.Response.Headers.ETag.ToString()
        });
        Assert.True(ifRange.Response.StatusCode == 206, "raw If-Range validator was not preserved");
        var encodedIfRange = await fixture.RequestAsync(".bmp", "br", headers: new Dictionary<string, string>
        {
            ["Range"] = "bytes=0-15", ["If-Range"] = br.Response.Headers.ETag.ToString()
        });
        Assert.True(encodedIfRange.Response.StatusCode == 200 && encodedIfRange.Response.Headers.ContentEncoding.Count == 0
            && ((MemoryStream)encodedIfRange.Response.Body).ToArray().SequenceEqual(fixture.Bytes), "an encoded If-Range validator selected raw partial bytes");
    }

    private static async Task FallbackAsync()
    {
        using var fixture = new Fixture();
        File.Delete(Path.Combine(fixture.Root, "assets", "sample.bmp.br"));
        var missing = await fixture.RequestAsync(".bmp", "br");
        Assert.True(Decode(((MemoryStream)missing.Response.Body).ToArray(), missing.Response.Headers.ContentEncoding.ToString()).SequenceEqual(fixture.Bytes), "missing sidecar blocked the original asset");
        var stale = await fixture.RequestAsync(".tga", "gzip", version: new string('0', 64));
        Assert.True(Decode(((MemoryStream)stale.Response.Body).ToArray(), stale.Response.Headers.ContentEncoding.ToString()).SequenceEqual(fixture.Bytes), "a stale index changed the requested raw bytes");
        Assert.True(stale.Response.Headers.ETag != fixture.GzipTag, "stale version served the indexed sidecar");
        Assert.True(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Root, "assets", "sample.bmp"))).SequenceEqual(SHA256.HashData(fixture.Bytes)), "compression altered a source file");
    }

    private static async Task RawFallbackValidatorsAsync()
    {
        using var fixture = new Fixture();
        var encoded = await fixture.RequestAsync(".smb", "br");
        File.Delete(Path.Combine(fixture.Root, "assets", "sample.smb.br"));
        File.Delete(Path.Combine(fixture.Root, "assets", "sample.smb.gz"));
        var fallback = await fixture.RequestAsync(".smb", "br,gzip");
        var identity = await fixture.RequestAsync(".smb", "identity");
        Assert.True(fallback.Response.StatusCode == 200 && fallback.Response.Headers.ContentEncoding.Count == 0
            && ((MemoryStream)fallback.Response.Body).ToArray().SequenceEqual(fixture.Bytes), "missing sidecars silently switched to dynamic encoded bytes");
        Assert.True(fallback.Response.Headers.ETag == identity.Response.Headers.ETag && fallback.Response.Headers.ETag != encoded.Response.Headers.ETag,
            "the raw fallback validator does not identify its actual bytes");
        var oldEncodedTag = await fixture.RequestAsync(".smb", "br,gzip", headers: new Dictionary<string, string>
        {
            ["If-None-Match"] = encoded.Response.Headers.ETag.ToString()
        });
        Assert.True(oldEncodedTag.Response.StatusCode == 200 && oldEncodedTag.Response.Body.Length == fixture.Bytes.Length,
            "an obsolete encoded validator incorrectly suppressed the raw fallback");
        var rawTag = await fixture.RequestAsync(".smb", "br,gzip", headers: new Dictionary<string, string>
        {
            ["If-None-Match"] = identity.Response.Headers.ETag.ToString()
        });
        Assert.True(rawTag.Response.StatusCode == 304 && rawTag.Response.Body.Length == 0 && rawTag.Response.Headers.ContentEncoding.Count == 0,
            "the normal raw conditional response was not preserved");
    }

    private static async Task InvalidIndexAndPrivatePathAsync()
    {
        using var fixture = new Fixture(static path => File.WriteAllText(path, "{"));
        var raw = await fixture.RequestAsync(".xnb", "br,gzip");
        Assert.True(raw.Response.StatusCode == 200 && raw.Response.Headers.ContentEncoding.Count == 0
            && ((MemoryStream)raw.Response.Body).ToArray().SequenceEqual(fixture.Bytes), "a malformed index blocked or recoded raw content");
        var privatePath = await fixture.RequestAsync(".bmp", "br,gzip", path: "/api/support/sample.bmp");
        Assert.True(privatePath.Response.StatusCode == 200 && privatePath.Response.Headers.ContentEncoding.Count == 0
            && privatePath.Response.Headers.CacheControl == "private, no-store" && privatePath.Response.Headers.ETag == "\"private-test\""
            && ((MemoryStream)privatePath.Response.Body).ToArray().SequenceEqual(fixture.Bytes), "game compression intercepted a private endpoint");
    }

    private static Task NegotiationAsync()
    {
        Assert.True(WebCompressedGameFiles.Negotiate("br;q=0, gzip;q=1", true, true) == "gzip", "explicit Brotli exclusion ignored");
        Assert.True(WebCompressedGameFiles.Negotiate("br;q=0, *;q=1", true, true) == "gzip", "wildcard overrode an explicit exclusion");
        Assert.True(WebCompressedGameFiles.Negotiate("br;q=.5, identity;q=1", true, true) is null, "identity preference ignored");
        Assert.True(WebCompressedGameFiles.Negotiate("gzip;q=.5, br;q=1", true, true) == "br", "quality ordering ignored");
        Assert.True(WebCompressedGameFiles.Negotiate("br,gzip", false, true) == "gzip", "missing Brotli variant blocked gzip");
        Assert.True(WebCompressedGameFiles.Negotiate("br,gzip", true, true, 200, 100) == "gzip", "an equal-quality request did not select the smaller representation");
        Assert.True(!WebCompressedGameFiles.IsEligible("/api/sample.bmp") && !WebCompressedGameFiles.IsEligible("/play/assets/sample.png"), "unrelated resources were opted into compression");
        Assert.True(!WebCompressedGameFiles.IsEligible("/play/assets/../sample.bmp") && !WebCompressedGameFiles.IsEligible("/play/assets/%2e%2e/sample.bmp"), "unsafe relative paths were accepted");
        return Task.CompletedTask;
    }

    private static byte[] Decode(byte[] data, string encoding)
    {
        if (encoding.Length == 0)
        {
            return data;
        }
        using var input = new MemoryStream(data);
        using Stream decompressor = encoding == "br" ? new BrotliStream(input, CompressionMode.Decompress) : new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "etervale-web-compression-" + Guid.NewGuid().ToString("N"));
        internal byte[] Bytes { get; } = Enumerable.Range(0, 8192).Select(static index => (byte)(index % 17)).ToArray();
        internal string GzipTag { get; }
        private readonly ServiceProvider _services;
        private readonly RequestDelegate _pipeline;

        internal Fixture(Action<string>? prepareIndex = null)
        {
            Bytes[0] = (byte)'B';
            Bytes[1] = (byte)'M';
            Directory.CreateDirectory(Path.Combine(Root, "assets"));
            var hash = Convert.ToHexString(SHA256.HashData(Bytes));
            var records = new List<WebCompressedGameFiles.CompressedAsset>();
            foreach (var extension in new[] { ".bmp", ".tga", ".smd", ".smb", ".xnb" })
            {
                var path = Path.Combine(Root, "assets", "sample" + extension);
                File.WriteAllBytes(path, Bytes);
                using (var output = File.Create(path + ".br"))
                using (var compression = new BrotliStream(output, CompressionLevel.Fastest))
                {
                    compression.Write(Bytes);
                }
                using (var output = File.Create(path + ".gz"))
                using (var compression = new GZipStream(output, CompressionLevel.Fastest))
                {
                    compression.Write(Bytes);
                }
                WebCompressedGameFiles.CompressedVariant Variant(string name) => new(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(name))), new FileInfo(name).Length);
                records.Add(new("assets/sample" + extension, hash, Bytes.Length, File.GetLastWriteTimeUtc(path), Variant(path + ".br"), Variant(path + ".gz")));
            }
            GzipTag = '"' + records[0].Gzip!.Sha256 + '"';
            File.WriteAllText(Path.Combine(Root, WebCompressedGameFiles.IndexName), JsonSerializer.Serialize(new WebCompressedGameFiles.AssetIndex(1, records)));
            prepareIndex?.Invoke(Path.Combine(Root, WebCompressedGameFiles.IndexName));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Play:ClientPath"] = Root }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(WebSettings.Read(configuration));
            services.AddSingleton<BrowserClient>();
            services.AddSingleton<WebCompressedGameFiles>();
            services.AddSingleton<IWebHostEnvironment>(new Environment(Root));
            _services = services.BuildServiceProvider();
            var builder = new ApplicationBuilder(_services);
            WebHosting.UseBrowserClient(builder);
            builder.Run(async context =>
            {
                if (context.Request.Path == "/api/support/sample.bmp")
                {
                    context.Response.ContentType = "image/bmp";
                    context.Response.Headers.CacheControl = "private, no-store";
                    context.Response.Headers.ETag = "\"private-test\"";
                    await context.Response.Body.WriteAsync(Bytes);
                    return;
                }
                context.Response.StatusCode = 404;
            });
            _pipeline = builder.Build();
        }

        internal async Task<DefaultHttpContext> RequestAsync(string extension, string encoding, string method = "GET", Dictionary<string, string>? headers = null,
            string? version = null, string? path = null)
        {
            var context = new DefaultHttpContext { RequestServices = _services };
            context.Request.Method = method;
            context.Request.Path = path ?? "/play/assets/sample" + extension;
            context.Request.QueryString = new QueryString("?v=" + (version ?? Convert.ToHexString(SHA256.HashData(Bytes))));
            context.Request.Headers.AcceptEncoding = encoding;
            if (headers is not null)
            {
                foreach (var header in headers)
                {
                    context.Request.Headers[header.Key] = header.Value;
                }
            }
            context.Response.Body = new MemoryStream();
            await _pipeline(context);
            return context;
        }

        public void Dispose()
        {
            _services.Dispose();
            var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.True(Path.GetFullPath(Root).StartsWith(temporary, StringComparison.Ordinal), "test cleanup escaped its temporary directory");
            Directory.Delete(Root, true);
        }
    }

    private sealed class Environment(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "PristonTale.Web";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
