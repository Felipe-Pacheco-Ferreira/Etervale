using System.Text.Json.Serialization;

namespace PristonTale.Engine3D.Rendering;

// The scene textures' size: Light halves width and height, VeryLight quarters them. Auto lets the browser page pick by
// the device's memory. The interface and icons never shrink.
[JsonConverter(typeof(JsonStringEnumConverter<TextureQuality>))]
public enum TextureQuality
{
    Auto,
    Original,
    Light,
    VeryLight
}

public static class TextureDownscale
{
    // A texture never shrinks below this side, so small decals and fonts in the scene stay legible.
    public const int SmallestSide = 32;

    public static int Divisor(TextureQuality quality) => quality switch
    {
        TextureQuality.Light => 2,
        TextureQuality.VeryLight => 4,
        _ => 1
    };

    // Box-filters RGBA pixels by the divisor, halving while both sides stay at least SmallestSide; returns the input
    // when nothing shrinks. The filter averages colour weighted by alpha so cut-out edges do not darken.
    public static (int Width, int Height, byte[] Rgba) Shrink(int width, int height, byte[] rgba, int divisor)
    {
        var factor = 1;
        while (factor < divisor && width / (factor * 2) >= SmallestSide && height / (factor * 2) >= SmallestSide)
        {
            factor *= 2;
        }

        if (factor == 1)
        {
            return (width, height, rgba);
        }

        var targetWidth = width / factor;
        var targetHeight = height / factor;
        var result = new byte[checked(targetWidth * targetHeight * 4)];
        var samples = factor * factor;
        for (var y = 0; y < targetHeight; y++)
        {
            for (var x = 0; x < targetWidth; x++)
            {
                long red = 0, green = 0, blue = 0, alpha = 0;
                for (var dy = 0; dy < factor; dy++)
                {
                    var row = (y * factor + dy) * width;
                    for (var dx = 0; dx < factor; dx++)
                    {
                        var at = (row + x * factor + dx) * 4;
                        var weight = rgba[at + 3];
                        red += rgba[at] * weight;
                        green += rgba[at + 1] * weight;
                        blue += rgba[at + 2] * weight;
                        alpha += weight;
                    }
                }

                var target = (y * targetWidth + x) * 4;
                if (alpha > 0)
                {
                    result[target] = (byte)(red / alpha);
                    result[target + 1] = (byte)(green / alpha);
                    result[target + 2] = (byte)(blue / alpha);
                }

                result[target + 3] = (byte)(alpha / samples);
            }
        }

        return (targetWidth, targetHeight, result);
    }
}
