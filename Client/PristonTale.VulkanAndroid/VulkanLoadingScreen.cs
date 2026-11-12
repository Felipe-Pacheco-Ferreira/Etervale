using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;

internal sealed class VulkanLoadingScreen : FrameLayout
{
    public TextView Status { get; }

    public VulkanLoadingScreen(Activity activity, string message) : base(activity)
    {
        SetBackgroundColor(Color.Rgb(12, 18, 24));
        var art = new ImageView(activity) { ContentDescription = "Priston Avalon" };
        using (var stream = activity.Assets!.Open("priston-avalon-loading.png"))
        {
            art.SetImageBitmap(BitmapFactory.DecodeStream(stream));
        }
        art.SetScaleType(ImageView.ScaleType.CenterCrop);
        AddView(art, new LayoutParams(-1, -1));

        var footer = new LinearLayout(activity)
        {
            Orientation = Orientation.Vertical
        };
        footer.SetGravity(GravityFlags.Center);
        var progress = new ProgressBar(activity)
        {
            Indeterminate = true,
            IndeterminateTintList = Android.Content.Res.ColorStateList.ValueOf(Color.Rgb(230, 185, 92))
        };
        footer.AddView(progress, new LinearLayout.LayoutParams(-2, -2));
        Status = new TextView(activity) { Text = message, TextSize = 16, Gravity = GravityFlags.Center };
        Status.SetTextColor(Color.Rgb(246, 219, 161));
        Status.SetShadowLayer(6f, 0f, 2f, Color.Black);
        footer.AddView(Status);
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        AddView(footer, new LayoutParams(-1, -2, GravityFlags.Bottom) { BottomMargin = (int)(24 * density) });
    }
}
