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
    private IReadOnlyList<Vector2> _route = [];
    // Dots along the route are this many field units apart.
    private const float RouteDotSpacing = 72f;
    // The hunting area in field coordinates; a radius of 0 hides it.
    private Vector2 _huntCentre;
    private float _huntRadius;
    private readonly Paint _huntFill = new() { AntiAlias = true, Color = Color.Argb(80, 255, 50, 36) };
    private readonly Paint _huntEdge = new() { AntiAlias = true, Color = Color.Argb(230, 255, 96, 72) };
    public event Action<Vector2>? DestinationSelected;

    // The field's map image and the field rectangle it covers, in field coordinates (for a full-screen map).
    public Bitmap Image => _image;
    public RectF World => _world;
    public IReadOnlyList<Vector2> Route => _route;

    // The route to show as yellow dots, in field coordinates, as the position is; empty hides it.
    public void SetRoute(IReadOnlyList<Vector2> route)
    {
        if (!ReferenceEquals(route, _route))
        {
            _route = route;
            Invalidate();
        }
    }

    // The round hunting area to show in red under the route and the arrow, in field coordinates; radius 0 hides it.
    public void SetHuntArea(Vector2 centre, float radius)
    {
        if (_huntCentre != centre || _huntRadius != radius)
        {
            _huntCentre = centre;
            _huntRadius = radius;
            Invalidate();
        }
    }

    private MobileMinimap(Context context, Bitmap image, RectF world) : base(context)
    {
        _image = image;
        _world = world;
        _huntEdge.SetStyle(Paint.Style.Stroke);
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
        if (_huntRadius > 0f)
        {
            var huntX = cx + (_huntCentre.X - _position.X) * scale;
            var huntY = cy - (_huntCentre.Y - _position.Z) * scale;
            canvas.DrawCircle(huntX, huntY, _huntRadius * scale, _huntFill);
            _huntEdge.StrokeWidth = Math.Max(1.5f, Width * .014f);
            canvas.DrawCircle(huntX, huntY, _huntRadius * scale, _huntEdge);
        }

        _paint.Color = Color.Rgb(233, 196, 117);
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = 2;
        canvas.DrawRect(1, 1, Width - 1, Height - 1, _paint);
        _paint.SetStyle(Paint.Style.Fill);
        DrawRoute(canvas, cx, cy, scale);
        _paint.Color = Color.Rgb(233, 196, 117);
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

    private void DrawRoute(Canvas canvas, float cx, float cy, float scale)
    {
        if (_route.Count == 0)
        {
            return;
        }

        _paint.Color = Color.Rgb(255, 214, 64);
        var dot = Math.Max(2f, Width * .018f);
        var from = new Vector2(_position.X, _position.Z);
        foreach (var to in _route)
        {
            var length = Vector2.Distance(from, to);
            var dots = Math.Max(1, (int)(length / RouteDotSpacing));
            for (var index = 1; index <= dots; index++)
            {
                var point = Vector2.Lerp(from, to, index / (float)dots);
                canvas.DrawCircle(cx + (point.X - _position.X) * scale, cy - (point.Y - _position.Z) * scale, dot, _paint);
            }

            from = to;
        }

        var goal = _route[^1];
        canvas.DrawCircle(cx + (goal.X - _position.X) * scale, cy - (goal.Y - _position.Z) * scale, dot * 2.2f, _paint);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image.Dispose();
            _world.Dispose();
            _destination.Dispose();
            _paint.Dispose();
            _huntFill.Dispose();
            _huntEdge.Dispose();
            _arrow.Dispose();
        }
        base.Dispose(disposing);
    }
}
