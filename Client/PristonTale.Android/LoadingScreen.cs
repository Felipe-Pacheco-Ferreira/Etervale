using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace PristonTale.Mobile;

// The original loading screen, shown from the moment a character enters the world until the world's first frame is
// drawn: one of the original loading pictures (WorldLoadingArt, packaged as Loading/*.png), chosen at random, filling
// the screen. It takes every touch, so nothing under it answers while it shows.
internal sealed class LoadingScreen : IDisposable
{
    private const string Directory = "Loading";

    private readonly Bitmap? _picture;

    public LoadingScreen(Activity activity)
    {
        var root = new FrameLayout(activity) { Clickable = true };
        root.SetBackgroundColor(Color.Black);
        _picture = Decode(activity);
        if (_picture is not null)
        {
            var image = new ImageView(activity);
            image.SetImageBitmap(_picture);
            image.SetScaleType(ImageView.ScaleType.FitXy!);
            root.AddView(image, new FrameLayout.LayoutParams(-1, -1));
        }

        View = root;
    }

    public View View { get; }

    private static Bitmap? Decode(Activity activity)
    {
        try
        {
            var pictures = activity.Assets!.List(Directory);
            if (pictures is null || pictures.Length == 0)
            {
                return null;
            }

            using var stream = activity.Assets.Open($"{Directory}/{pictures[Random.Shared.Next(pictures.Length)]}");
            return BitmapFactory.DecodeStream(stream);
        }
        catch (Java.IO.IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _picture?.Recycle();
        _picture?.Dispose();
    }
}
