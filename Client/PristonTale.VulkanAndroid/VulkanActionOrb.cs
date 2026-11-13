using Android.Content;
using Android.Graphics;
using Android.Views;
using PristonTale.Mobile;

// A round HUD button: a sphere in a gold ring with a glyph or an item's sprite, a caption under the glyph and a count at
// its lower right; the prominent one is the attack button, crossed swords on steel (VulkanHudArt.AttackFace). Taps and
// long presses arrive as the view's Click and LongClick.
internal sealed class VulkanActionOrb : View
{
    private readonly bool _prominent;
    private HudIcon _glyph;
    private Bitmap _picture;
    private string _caption = "";
    private string _count = "";
    private readonly RectF _box = new();

    public VulkanActionOrb(Context context, HudGlyph glyph, bool prominent = false) : base(context)
    {
        _prominent = prominent;
        Clickable = true;
        LongClickable = true;
        SetGlyph(glyph);
    }

    private void SetGlyph(HudGlyph glyph)
    {
        _glyph = new HudIcon(glyph, 24);
        Invalidate();
    }

    // An item's sprite drawn instead of the glyph; null goes back to the glyph.
    public void SetPicture(Bitmap picture)
    {
        if (!ReferenceEquals(picture, _picture))
        {
            _picture = picture;
            Invalidate();
        }
    }

    public void SetCaption(string caption, string count = "")
    {
        if (caption != _caption || count != _count)
        {
            _caption = caption ?? "";
            _count = count ?? "";
            Invalidate();
        }
    }

    protected override void DrawableStateChanged()
    {
        base.DrawableStateChanged();
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        var radius = Math.Min(Width, Height) / 2f;
        var x = Width / 2f;
        var y = Height / 2f;
        if (_prominent)
        {
            VulkanHudArt.AttackFace(canvas, x, y, radius, Enabled, Pressed);
            return;
        }

        VulkanHudArt.Orb(canvas, x, y, radius, Enabled, Pressed);
        var icon = radius * (_caption.Length > 0 ? .9f : 1.1f);
        var top = y - icon / 2f - (_caption.Length > 0 ? radius * .14f : 0f);
        if (_picture is not null)
        {
            var scale = Math.Min(icon * 1.2f / _picture.Width, icon * 1.2f / _picture.Height);
            var width = _picture.Width * scale;
            var height = _picture.Height * scale;
            _box.Set(x - width / 2f, y - height / 2f - radius * .05f, x + width / 2f, y + height / 2f - radius * .05f);
            canvas.DrawBitmap(_picture, null, _box, null);
        }
        else
        {
            _glyph.SetBounds((int)(x - icon / 2f), (int)top, (int)(x + icon / 2f), (int)(top + icon));
            _glyph.SetAlpha(Enabled ? 255 : 120);
            _glyph.Draw(canvas);
        }

        if (_caption.Length > 0)
        {
            VulkanHudArt.Label(canvas, _caption, x, y + radius * .5f, radius * .3f, Color.White, Paint.Align.Center!);
        }

        if (_count.Length > 0)
        {
            VulkanHudArt.Label(canvas, _count, x + radius * .55f, y + radius * .62f, radius * .36f, Color.White, Paint.Align.Center!);
        }
    }
}
