using System.Numerics;
using Android.Content;
using Android.Graphics;
using Android.Views;

namespace PristonTale.Mobile;

internal sealed class MovementJoystick : View
{
    private const float DeadZone = .16f;
    private readonly Paint _paint = new() { AntiAlias = true };
    private int _pointer = -1;
    private Vector2 _knob;

    public MovementJoystick(Context context) : base(context)
    {
        ContentDescription = "Controle de movimento";
    }

    public event Action<Vector2>? DirectionChanged;

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_pointer < 0)
        {
            return;
        }
        var radius = Math.Min(Width, Height) * .47f;
        var x = Width / 2f;
        var y = Height / 2f;
        _paint.Color = Color.Argb(124, 19, 25, 33);
        canvas.DrawCircle(x, y, radius, _paint);
        _paint.Color = Color.Argb(158, 193, 204, 212);
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = radius * .04f;
        canvas.DrawCircle(x, y, radius, _paint);
        _paint.SetStyle(Paint.Style.Fill);
        _paint.Color = Color.Argb(220, 222, 206, 162);
        canvas.DrawCircle(x + _knob.X * radius * .65f, y + _knob.Y * radius * .65f, radius * .31f, _paint);
    }

    public override bool OnTouchEvent(MotionEvent? motion)
    {
        if (motion is null)
        {
            return false;
        }

        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
                _pointer = motion.GetPointerId(0);
                UpdateDirection(motion.GetX(), motion.GetY());
                Invalidate();
                return true;
            case MotionEventActions.Move:
                var index = motion.FindPointerIndex(_pointer);
                if (index >= 0)
                {
                    UpdateDirection(motion.GetX(index), motion.GetY(index));
                }
                break;
            case MotionEventActions.PointerUp when motion.GetPointerId(motion.ActionIndex) == _pointer:
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                Reset();
                break;
        }
        return true;
    }

    public void Reset()
    {
        _pointer = -1;
        _knob = Vector2.Zero;
        DirectionChanged?.Invoke(Vector2.Zero);
        Invalidate();
    }

    private void UpdateDirection(float x, float y)
    {
        var radius = Math.Min(Width, Height) * .31f;
        if (radius <= 0f)
        {
            return;
        }
        var offset = new Vector2(x - Width / 2f, y - Height / 2f) / radius;
        var length = offset.Length();
        _knob = length > 1f ? offset / length : offset;
        var direction = length > DeadZone ? new Vector2(_knob.X, -_knob.Y) : Vector2.Zero;
        DirectionChanged?.Invoke(direction);
        Invalidate();
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
