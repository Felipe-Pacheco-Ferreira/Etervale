using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.Input;

// The touch gestures of the world, shared by the Android and browser clients: a finger that lands in the lower left
// region is the joystick (Movement); one finger dragged elsewhere orbits the camera and two pinch its distance (Offset);
// a finger lifted without dragging is a tap. Positions are fractions of the screen.
public sealed class TouchOrbit
{
    private readonly ITouchFingers fingers;
    private long firstId;
    private long moveId;
    private bool moving;
    private bool joystickMoved;
    private bool dragged;
    private Vector2 touchStart;
    private Vector2 origin;
    private long secondId;
    private int previousCount;
    private Vector2 previousPosition;
    private float previousSpan;
    private float yaw;
    private float pitch = 0.59f;
    private float distance = 270f;

    public TouchOrbit(ITouchFingers fingers)
    {
        this.fingers = fingers;
    }

    public Vector2? Tap { get; private set; }
    public Vector2 Movement { get; private set; }
    public bool JoystickActive => moving;
    public bool CameraActive => previousCount > 0;
    public Vector2 JoystickOrigin => origin;

    public Vector3 Offset => new(
        MathF.Sin(yaw) * MathF.Cos(pitch) * distance,
        MathF.Sin(pitch) * distance,
        MathF.Cos(yaw) * MathF.Cos(pitch) * distance);

    // Whether a finger landing at this point takes the joystick.
    public static bool InJoystickRegion(Vector2 point) => point.X < .45f && point.Y > .48f;

    public void Update(bool allowJoystick = true)
    {
        Tap = null;
        var count = fingers.Count;
        if (count > 0 && fingers.TryRead(0, out var first))
        {
            Track(first, count, allowJoystick);
            return;
        }

        if (moving && !joystickMoved)
        {
            Tap = origin;
        }
        else if (previousCount == 1 && !moving && !dragged)
        {
            Tap = previousPosition;
        }
        previousCount = 0;
        moving = false;
        Movement = Vector2.Zero;
    }

    private void Track(TouchFinger first, int count, bool allowJoystick)
    {
        if (allowJoystick && !moving && previousCount == 0 && InJoystickRegion(new Vector2(first.X, first.Y)))
        {
            moving = true;
            joystickMoved = false;
            moveId = first.Id;
            origin = new Vector2(first.X, first.Y);
        }
        if (moving)
        {
            if (first.Id == moveId)
            {
                var offset = (new Vector2(first.X, first.Y) - origin) * new Vector2(12f, 6f);
                Movement = offset.LengthSquared() < .16f * .16f ? Vector2.Zero : offset / Math.Max(1f, offset.Length());
                joystickMoved |= Movement != Vector2.Zero;
                count--;
                if (count == 0 || !fingers.TryRead(1, out first))
                {
                    previousCount = 0;
                    return;
                }
            }
            else
            {
                moving = false;
                Movement = Vector2.Zero;
            }
        }
        var position = new Vector2(first.X, first.Y);
        if (previousCount == 0)
        {
            touchStart = position;
            dragged = moving;
        }
        if (!moving && count >= 2 && fingers.TryRead(1, out var second))
        {
            var span = Vector2.Distance(position, new Vector2(second.X, second.Y));
            if (previousCount == 2 && first.Id == firstId && second.Id == secondId && span > 0.025f)
            {
                distance = Math.Clamp(distance * previousSpan / span, 125f, 850f);
            }
            dragged = true;
            previousSpan = Math.Max(0.025f, span);
            secondId = second.Id;
            previousCount = 2;
        }
        else
        {
            dragged |= Vector2.DistanceSquared(touchStart, position) > .012f * .012f;
            if (dragged && previousCount == 1 && first.Id == firstId)
            {
                var delta = position - previousPosition;
                yaw = MathHelper.WrapAngle(yaw - delta.X * MathHelper.TwoPi);
                pitch = Math.Clamp(pitch + delta.Y * 2.4f, 0.12f, 1.24f);
            }
            previousCount = 1;
        }

        firstId = first.Id;
        previousPosition = position;
    }
}
