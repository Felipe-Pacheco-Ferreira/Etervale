using Android.Content;
using Android.Graphics;
using Android.Views;

namespace PristonTale.Mobile;

internal sealed class MobileNameplates(Context context, RicartenSceneView scene) : View(context)
{
    private readonly Paint _paint = new() { AntiAlias = true };

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        scene.DrawNameplates(canvas, _paint, density);
        PostInvalidateOnAnimation();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _paint.Dispose();
        }
        base.Dispose(disposing);
    }
}
