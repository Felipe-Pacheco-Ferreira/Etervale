using System.Buffers.Binary;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Browser;

internal sealed class BrowserSceneTextures(Action<int, int, int, byte[]> publish, IClientLog log)
{
    private const int DecodeConcurrency = 2;
    private static readonly SemaphoreSlim DecodeSlots = new(DecodeConcurrency, DecodeConcurrency);
    private static int _nextTextureId;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Pixels(int Width, int Height, byte[] Rgba)
    {
        public static readonly Pixels Empty = new(0, 0, []);
    }

    private sealed class Entry(Func<Pixels> decode)
    {
        public Lazy<Task<Pixels>>? Preparation = new(() => DecodeAsync(decode));
        public bool Published;
        public int Id;
        public Pixels Decode() => decode();
    }

    public Task PrepareAsync(string directory, string name, string opacity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.CompletedTask;
        }
        var entry = Find(directory, name, opacity);
        return entry.Preparation is { } preparation ? preparation.Value.WaitAsync(cancellationToken) : Task.CompletedTask;
    }

    public int Get(string directory, string name, string opacity = "")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }
        var entry = Find(directory, name, opacity);
        if (entry.Published)
        {
            return entry.Id;
        }
        Pixels pixels;
        if (entry.Preparation is { IsValueCreated: true } preparation)
        {
            if (!preparation.Value.IsCompleted)
            {
                throw new InvalidOperationException("Scene texture preparation did not finish before publication.");
            }
            pixels = preparation.Value.GetAwaiter().GetResult();
        }
        else
        {
            // Synchronous bootstrap/tool loading; streaming awaits PrepareMaterialsAsync before CreateMesh.
            pixels = entry.Decode();
        }
        if (pixels.Rgba.Length > 0)
        {
            entry.Id = Interlocked.Decrement(ref _nextTextureId);
            publish(entry.Id, pixels.Width, pixels.Height, pixels.Rgba);
        }
        entry.Published = true;
        entry.Preparation = null;
        return entry.Id;
    }

    public void Clear() => _entries.Clear();

    private Entry Find(string directory, string name, string opacity)
    {
        var path = PristonTextureLoader.ResolvePath(directory, name);
        var mask = string.IsNullOrWhiteSpace(opacity) ? string.Empty : PristonTextureLoader.ResolvePath(directory, opacity);
        var key = path.Length > 0 ? path + "|" + mask : Path.GetFullPath(string.IsNullOrEmpty(directory) ? "." : directory) + "|" + name + "|" + opacity;
        return _entries.GetOrAdd(key, _ => new Entry(() => Decode(path, mask, name)));
    }

    private static async Task<Pixels> DecodeAsync(Func<Pixels> decode)
    {
        await DecodeSlots.WaitAsync();
        try
        {
            return await Task.Run(decode);
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    private Pixels Decode(string path, string maskPath, string requested)
    {
        try
        {
            if (path.Length == 0)
            {
                log.Warn($"Scene texture '{requested}' was not found; using white.");
                return Pixels.Empty;
            }
            var image = Read(path);
            if (!image.IsValid)
            {
                throw new InvalidDataException("The image decoder returned no pixels.");
            }
            if (!image.HasSourceAlpha && maskPath.Length > 0)
            {
                var mask = Read(maskPath);
                if (mask.IsValid && PristonOpacityMask.Applies(path, maskPath, image.Width, image.Height, mask.Width, mask.Height))
                {
                    for (var i = 0; i < image.Pixels.Length; i++)
                    {
                        var alpha = mask.Pixels[i];
                        image.Pixels[i].A = PristonOpacityMask.Alpha(alpha.R, alpha.G, alpha.B);
                    }
                }
            }
            var rgba = new byte[checked(image.Pixels.Length * 4)];
            for (var i = 0; i < image.Pixels.Length; i++)
            {
                var pixel = image.Pixels[i];
                rgba[i * 4] = pixel.R;
                rgba[i * 4 + 1] = pixel.G;
                rgba[i * 4 + 2] = pixel.B;
                rgba[i * 4 + 3] = pixel.A;
            }
            return new Pixels(image.Width, image.Height, rgba);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            log.Warn($"Scene texture '{requested}' could not be decoded: {exception.Message}");
            return Pixels.Empty;
        }
    }

    private static DecodedPristonImage Read(string path)
    {
        var bytes = AssetFiles.ReadAllBytes(path);
        return bytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0x20534444
            ? BrowserDdsImage.Decode(bytes) : PristonImageDecoder.Decode(bytes, Path.GetExtension(path));
    }
}

internal static class BrowserDdsImage
{
    private const int HeaderSize = 128;
    private const int MaximumPixels = 64_000_000;

    public static DecodedPristonImage Decode(byte[] bytes)
    {
        if (bytes.Length < HeaderSize || Read(bytes, 0) != 0x20534444 || Read(bytes, 4) != 124 || Read(bytes, 76) != 32)
        {
            throw new InvalidDataException("Invalid DDS header.");
        }
        if (Read(bytes, 24) > 1 || (Read(bytes, 112) & 0x20fe00) != 0)
        {
            throw new InvalidDataException("DDS scene textures must be two-dimensional images.");
        }
        var height = checked((int)Read(bytes, 12));
        var width = checked((int)Read(bytes, 16));
        if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels)
        {
            throw new InvalidDataException("Invalid DDS image dimensions.");
        }
        var format = Read(bytes, 84);
        var offset = HeaderSize;
        var compressed = 0;
        var rgba = false;
        if (format == 0x30315844)
        {
            if (bytes.Length < HeaderSize + 20)
            {
                throw new InvalidDataException("Missing DDS DX10 header.");
            }
            var dxgi = Read(bytes, HeaderSize);
            if (Read(bytes, HeaderSize + 4) != 3 || (Read(bytes, HeaderSize + 8) & 4) != 0 || Read(bytes, HeaderSize + 12) != 1)
            {
                throw new InvalidDataException("DDS scene textures must have one two-dimensional image.");
            }
            compressed = dxgi switch { 71 or 72 => 1, 74 or 75 => 3, 77 or 78 => 5, _ => 0 };
            rgba = dxgi is 28 or 29;
            if (compressed == 0 && !rgba && dxgi is not (87 or 91))
            {
                throw new InvalidDataException("Unsupported DDS DXGI format.");
            }
            offset += 20;
        }
        else
        {
            compressed = format switch { 0x31545844 => 1, 0x33545844 => 3, 0x35545844 => 5, _ => 0 };
            if (format != 0 && compressed == 0)
            {
                throw new InvalidDataException("Unsupported DDS compression.");
            }
        }
        byte[] pixels;
        if (compressed != 0)
        {
            var blockBytes = compressed == 1 ? 8 : 16;
            var length = checked(((width + 3) / 4) * ((height + 3) / 4) * blockBytes);
            if (length > bytes.Length - offset)
            {
                throw new InvalidDataException("Truncated DDS block data.");
            }
            var blocks = bytes.AsSpan(offset, length).ToArray();
            pixels = compressed switch
            {
                1 => DxtDecoder.Decode(blocks, width, height, SurfaceFormat.Dxt1),
                3 => DxtDecoder.Decode(blocks, width, height, SurfaceFormat.Dxt3),
                _ => DxtDecoder.Decode(blocks, width, height, SurfaceFormat.Dxt5)
            };
        }
        else
        {
            var bits = format == 0x30315844 ? 32 : checked((int)Read(bytes, 88));
            if (bits is not (24 or 32))
            {
                throw new InvalidDataException("Unsupported DDS RGB bit count.");
            }
            var pixelBytes = bits / 8;
            var rowBytes = checked(width * pixelBytes);
            var pitch = (Read(bytes, 8) & 8) != 0 ? Math.Max(rowBytes, checked((int)Read(bytes, 20))) : rowBytes;
            if ((long)pitch * height > bytes.Length - offset)
            {
                throw new InvalidDataException("Truncated DDS pixels.");
            }
            pixels = new byte[checked(width * height * 4)];
            var red = format == 0x30315844 ? rgba ? 0xffu : 0xff0000u : Read(bytes, 92);
            var green = format == 0x30315844 ? 0xff00u : Read(bytes, 96);
            var blue = format == 0x30315844 ? rgba ? 0xff0000u : 0xffu : Read(bytes, 100);
            var alpha = format == 0x30315844 ? 0xff000000u : Read(bytes, 104);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var source = offset + y * pitch + x * pixelBytes;
                    uint value = bytes[source] | (uint)bytes[source + 1] << 8 | (uint)bytes[source + 2] << 16;
                    if (pixelBytes == 4)
                    {
                        value |= (uint)bytes[source + 3] << 24;
                    }
                    var target = (y * width + x) * 4;
                    pixels[target] = Channel(value, red, 0);
                    pixels[target + 1] = Channel(value, green, 0);
                    pixels[target + 2] = Channel(value, blue, 0);
                    pixels[target + 3] = Channel(value, alpha, 255);
                }
            }
        }
        var colors = new Microsoft.Xna.Framework.Color[checked(width * height)];
        var sourceAlpha = compressed is 3 or 5 || (Read(bytes, 80) & 1) != 0 || format == 0x30315844;
        for (var i = 0; i < colors.Length; i++)
        {
            var at = i * 4;
            colors[i] = new Microsoft.Xna.Framework.Color(pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
            sourceAlpha |= pixels[at + 3] != 255;
        }
        return new DecodedPristonImage(width, height, colors, sourceAlpha);
    }

    private static uint Read(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static byte Channel(uint value, uint mask, byte missing)
    {
        if (mask == 0)
        {
            return missing;
        }
        var shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        var maximum = mask >> shift;
        return (byte)((((ulong)(value & mask) >> shift) * 255 + maximum / 2) / maximum);
    }
}
