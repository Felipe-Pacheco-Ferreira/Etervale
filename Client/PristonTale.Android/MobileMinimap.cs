using System.Numerics;
using Android.Content;
using Android.Graphics;
using Android.Views;

namespace PristonTale.Mobile;

internal sealed class MobileMinimap : View
{
    private const float Radius = 24 * 64f;
    private readonly Bitmap _image;
    private readonly RectF _world;
    private readonly RectF _destination = new();
    private readonly Paint _paint = new() { AntiAlias = true, FilterBitmap = true };
    private readonly global::Android.Graphics.Path _arrow = new();
    private Vector3 _position;
    private float _facing;
    public event Action<Vector2>? DestinationSelected;

    private MobileMinimap(Context context, Bitmap image, RectF world) : base(context)
    {
        _image = image;
        _world = world;
        ContentDescription = "Minimapa. Toque para marcar um destino.";
        Clickable = true;
    }

    public static MobileMinimap? Open(Context context, string field)
    {
        if (field == "quest-arena")
        {
            return null;
        }
        using var bounds = new BinaryReader(context.Assets!.Open($"{field}-minimap.bin"));
        var rectangle = new RectF(bounds.ReadSingle(), bounds.ReadSingle(), bounds.ReadSingle(), bounds.ReadSingle());
        using var stream = context.Assets.Open($"{field}-minimap.png");
        var bitmap = BitmapFactory.DecodeStream(stream) ?? throw new InvalidDataException("Invalid minimap image.");
        return new MobileMinimap(context, bitmap, rectangle);
    }

    public void UpdatePosition(Vector3 position, float facing)
    {
        if (_position == position && _facing == facing)
        {
            return;
        }
        _position = position;
        _facing = facing;
        Invalidate();
    }

    public override bool OnTouchEvent(MotionEvent? motion)
    {
        if (motion?.ActionMasked == MotionEventActions.Up)
        {
            var scale = Math.Min(Width, Height) / (Radius * 2f);
            if (scale > 0f)
            {
                var destination = new Vector2(_position.X + (motion.GetX() - Width * .5f) / scale,
                    _position.Z - (motion.GetY() - Height * .5f) / scale);
                PerformClick();
                DestinationSelected?.Invoke(destination);
            }
        }
        return true;
    }

    public override bool PerformClick() => base.PerformClick();

    protected override void OnDraw(Canvas canvas)
    {
        var cx = Width * .5f;
        var cy = Height * .5f;
        var scale = Math.Min(Width, Height) / (Radius * 2f);
        canvas.DrawColor(Color.Rgb(27, 22, 17));
        _destination.Set(cx + (_world.Left - _position.X) * scale, cy + (_position.Z - _world.Bottom) * scale,
            cx + (_world.Right - _position.X) * scale, cy + (_position.Z - _world.Top) * scale);
        canvas.DrawBitmap(_image, null, _destination, _paint);
        _paint.Color = Color.Rgb(233, 196, 117);
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = 2;
        canvas.DrawRect(1, 1, Width - 1, Height - 1, _paint);
        _paint.SetStyle(Paint.Style.Fill);
        canvas.Save();
        canvas.Rotate(_facing * 180f / MathF.PI, cx, cy);
        var size = Width * .055f;
        _arrow.Reset();
        _arrow.MoveTo(cx, cy - size);
        _arrow.LineTo(cx + size * .7f, cy + size);
        _arrow.LineTo(cx - size * .7f, cy + size);
        _arrow.Close();
        canvas.DrawPath(_arrow, _paint);
        canvas.Restore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image.Dispose();
            _world.Dispose();
            _destination.Dispose();
            _paint.Dispose();
            _arrow.Dispose();
        }
        base.Dispose(disposing);
    }
}
