using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

internal sealed class TouchOrbit
{
    private long firstId;
    private long moveId;
    private bool moving;
    private bool joystickMoved;
    private bool dragged;
    private Vector2 touchStart;
    public Vector2? Tap { get; private set; }
    private Vector2 origin;
    public Vector2 Movement { get; private set; }
    public bool JoystickActive => moving;
    public bool CameraActive => previousCount > 0;
    public Vector2 JoystickOrigin => origin;
    private long secondId;
    private int previousCount;
    private Vector2 previousPosition;
    private float previousSpan;
    private float yaw;
    private float pitch = 0.59f;
    private float distance = 270f;

    public Vector3 Offset => new(
        MathF.Sin(yaw) * MathF.Cos(pitch) * distance,
        MathF.Sin(pitch) * distance,
        MathF.Cos(yaw) * MathF.Cos(pitch) * distance);

    public void Update(bool allowJoystick = true)
    {
        Tap = null;
        for (var index = 0; index < SDL_GetNumTouchDevices(); index++)
        {
            var device = SDL_GetTouchDevice(index);
            var count = SDL_GetNumTouchFingers(device);
            if (count == 0 || !TryRead(device, 0, out var first))
            {
                continue;
            }

            if (allowJoystick && !moving && previousCount == 0 && first.X < .45f && first.Y > .48f)
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
                    if (count == 0 || !TryRead(device, 1, out first))
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
            if (!moving && count >= 2 && TryRead(device, 1, out var second))
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

    private static bool TryRead(long device, int index, out Finger finger)
    {
        var pointer = SDL_GetTouchFinger(device, index);
        finger = pointer == IntPtr.Zero ? default : Marshal.PtrToStructure<Finger>(pointer);
        return pointer != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Finger
    {
        public long Id;
        public float X;
        public float Y;
        public float Pressure;
    }

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetNumTouchDevices();

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern long SDL_GetTouchDevice(int index);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetNumTouchFingers(long device);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetTouchFinger(long device, int index);
}
