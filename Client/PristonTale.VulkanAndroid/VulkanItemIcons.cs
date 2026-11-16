using Android.Graphics;
using PristonTale.Assets;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Rendering;

// The items' inventory sprites as the desktop draws them (image/Sinimage/Items, ItemIconFiles), and other original UI
// bitmaps by their path (the skill buttons, HudArt.SkillButton), decoded off the UI thread into Android bitmaps and kept
// for the session. Loaded is raised on the UI thread when a requested icon arrives.
internal sealed class VulkanItemIcons(string assetRoot, Action<Action> post)
{
    private readonly Task<Dictionary<string, string>> _index = Task.Run(() => ItemIconFiles.Index(assetRoot));
    private readonly Dictionary<string, Bitmap> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _requested = new(StringComparer.OrdinalIgnoreCase);

    public event Action Loaded;

    // The icon of a catalog key, or null while it loads or when there is none; called on the UI thread.
    public Bitmap Get(string catalogKey) => Get(catalogKey, ItemFileAsync, upscale: true);

    // An original UI bitmap by its path under the asset root, or null while it loads or when there is none; UI thread.
    public Bitmap Picture(string assetPath) => Get(assetPath, path => Task.FromResult(AssetFiles.ResolveFile(assetRoot, path)));

    private Bitmap Get(string key, Func<string, Task<string>> locate, bool upscale = false)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (_icons.TryGetValue(key, out var icon))
        {
            return icon;
        }

        if (_requested.Add(key))
        {
            _ = LoadAsync(key, locate, upscale);
        }

        return null;
    }

    private async Task<string> ItemFileAsync(string catalogKey) => ItemIconFiles.TryFind(await _index, catalogKey, out var path) ? path : null;

    private async Task LoadAsync(string key, Func<string, Task<string>> locate, bool upscale)
    {
        try
        {
            var path = await locate(key);
            if (path is null)
            {
                return;
            }

            var colors = await Task.Run<(int Width, int Height, int[] Argb)?>(() =>
            {
                var extension = System.IO.Path.GetExtension(path);
                var image = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(path), extension, bmpCoverage: true);
                if (!image.IsValid)
                {
                    return null;
                }

                PristonUiPixels.Prepare(image, HudSprites.ColorKeyFor(extension));
                var argb = new int[image.Pixels.Length];
                for (var index = 0; index < argb.Length; index++)
                {
                    var pixel = image.Pixels[index];
                    if (pixel.A != 0)
                    {
                        argb[index] = pixel.A << 24 | Straight(pixel.R, pixel.A) << 16 | Straight(pixel.G, pixel.A) << 8 | Straight(pixel.B, pixel.A);
                    }
                }

                return upscale ? (image.Width * 2, image.Height * 2, DoubleSprite(argb, image.Width, image.Height))
                    : (image.Width, image.Height, argb);
            });
            if (colors is not { } decoded)
            {
                return;
            }

            post(() =>
            {
                _icons[key] = Bitmap.CreateBitmap(decoded.Argb, decoded.Width, decoded.Height, Bitmap.Config.Argb8888!)!;
                Loaded?.Invoke();
            });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Android.Util.Log.Warn("Etervale.Items", $"Icon {key}: {ex.Message}");
        }
    }

    // Bitmap.CreateBitmap takes straight ARGB. The desktop pixel preparer produces premultiplied color.
    private static int Straight(int channel, int alpha) => Math.Min(255, (channel * 255 + alpha / 2) / alpha);

    // Scale2x rounds the corners where neighbouring colours agree and keeps the original alpha silhouette.
    private static int[] DoubleSprite(int[] pixels, int width, int height)
    {
        var stride = width * 2;
        var result = new int[pixels.Length * 4];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var above = Math.Max(0, y - 1) * width;
            var below = Math.Min(height - 1, y + 1) * width;
            for (var x = 0; x < width; x++)
            {
                var center = pixels[row + x];
                var top = pixels[above + x];
                var bottom = pixels[below + x];
                var left = pixels[row + Math.Max(0, x - 1)];
                var right = pixels[row + Math.Min(width - 1, x + 1)];
                var dest = y * 2 * stride + x * 2;
                var corner = top != bottom && left != right;
                result[dest] = corner && left == top ? left : center;
                result[dest + 1] = corner && top == right ? right : center;
                result[dest + stride] = corner && left == bottom ? left : center;
                result[dest + stride + 1] = corner && bottom == right ? right : center;
            }
        }

        return result;
    }
}
