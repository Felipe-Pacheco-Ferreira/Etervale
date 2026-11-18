using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Tests;

internal static class DxtDecoderTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("CPU DXT decoder preserves RGBA and clips partial blocks without a graphics device", DecodeAsync);
        yield return ("CPU DXT decoder refuses invalid dimensions, formats and truncated levels", RefusalsAsync);
    }

    private static Task DecodeAsync()
    {
        byte[] red = [0, 248, 0, 0, 0, 0, 0, 0];
        var pixels = DxtDecoder.Decode(red, 4, 4, SurfaceFormat.Dxt1);
        Assert.True(pixels.Length == 64 && pixels[0] == 255 && pixels[1] == 0 && pixels[2] == 0 && pixels[3] == 255, "BC1 pixel RGBA changed");
        pixels = DxtDecoder.Decode(red, 1, 1, SurfaceFormat.Dxt1);
        Assert.True(pixels.SequenceEqual(new byte[] { 255, 0, 0, 255 }), "partial block was not clipped");
        var explicitAlpha = new byte[16];
        Array.Fill(explicitAlpha, (byte)0x88, 0, 8);
        red.CopyTo(explicitAlpha, 8);
        pixels = DxtDecoder.Decode(explicitAlpha, 4, 4, SurfaceFormat.Dxt3);
        Assert.True(pixels[0] == 255 && pixels[3] == 136, "BC2 explicit alpha changed");
        explicitAlpha[0] = 200;
        explicitAlpha[1] = 100;
        Array.Clear(explicitAlpha, 2, 6);
        pixels = DxtDecoder.Decode(explicitAlpha, 4, 4, SurfaceFormat.Dxt5);
        Assert.True(pixels[0] == 255 && pixels[3] == 200, "BC3 interpolated alpha changed");
        return Task.CompletedTask;
    }

    private static Task RefusalsAsync()
    {
        Assert.Throws<ArgumentNullException>(() => DxtDecoder.Decode(null!, 4, 4, SurfaceFormat.Dxt1), "null block input accepted");
        Assert.Throws<ArgumentOutOfRangeException>(() => DxtDecoder.Decode([], 0, 4, SurfaceFormat.Dxt1), "zero width accepted");
        Assert.Throws<ArgumentOutOfRangeException>(() => DxtDecoder.Decode([], 4, -1, SurfaceFormat.Dxt1), "negative height accepted");
        Assert.Throws<ArgumentOutOfRangeException>(() => DxtDecoder.Decode([], 4, 4, SurfaceFormat.Color), "uncompressed format accepted");
        Assert.Throws<ArgumentException>(() => DxtDecoder.Decode([], int.MaxValue, 4, SurfaceFormat.Dxt1), "oversized decoded image accepted");
        Assert.Throws<ArgumentException>(() => DxtDecoder.Decode([], int.MaxValue, int.MaxValue, SurfaceFormat.Dxt1), "overflow-sized decoded image accepted");
        Assert.Throws<ArgumentException>(() => DxtDecoder.Decode(new byte[7], 4, 4, SurfaceFormat.Dxt1), "truncated BC1 accepted");
        Assert.Throws<ArgumentException>(() => DxtDecoder.Decode(new byte[15], 4, 4, SurfaceFormat.Dxt5), "truncated BC3 accepted");
        return Task.CompletedTask;
    }
}
