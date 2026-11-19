using System;
using System.Runtime.InteropServices;
using PristonTale.Gameplay.Input;

// The fingers SDL reports, from the first touch device that has any.
internal sealed class SdlTouchFingers : ITouchFingers
{
    private long device;

    public int Count
    {
        get
        {
            for (var index = 0; index < SDL_GetNumTouchDevices(); index++)
            {
                var candidate = SDL_GetTouchDevice(index);
                var count = SDL_GetNumTouchFingers(candidate);
                if (count > 0)
                {
                    device = candidate;
                    return count;
                }
            }

            return 0;
        }
    }

    public bool TryRead(int index, out TouchFinger finger)
    {
        var pointer = SDL_GetTouchFinger(device, index);
        if (pointer == IntPtr.Zero)
        {
            finger = default;
            return false;
        }

        var native = Marshal.PtrToStructure<Finger>(pointer);
        finger = new TouchFinger(native.Id, native.X, native.Y);
        return true;
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
