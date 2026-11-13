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
    public Bitmap Get(string catalogKey) => Get(catalogKey, ItemFileAsync);

    // An original UI bitmap by its path under the asset root, or null while it loads or when there is none; UI thread.
    public Bitmap Picture(string assetPath) => Get(assetPath, path => Task.FromResult(AssetFiles.ResolveFile(assetRoot, path)));

    private Bitmap Get(string key, Func<string, Task<string>> locate)
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
            _ = LoadAsync(key, locate);
        }

        return null;
    }

    private async Task<string> ItemFileAsync(string catalogKey) => (await _index).TryGetValue(catalogKey, out var path) ? path : null;

    private async Task LoadAsync(string key, Func<string, Task<string>> locate)
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
                    argb[index] = pixel.A << 24 | pixel.R << 16 | pixel.G << 8 | pixel.B;
                }

                return (image.Width, image.Height, argb);
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
}
