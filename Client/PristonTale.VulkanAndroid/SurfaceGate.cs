using System.Diagnostics;

// Keeps the game from drawing while Android has taken its surface away (the app in the background): drawing then makes
// the Vulkan runtime record into a swap chain that is gone, and the process dies. The game's activity closes the gate
// in onPause, waiting for a frame being drawn to end, and opens it again in onResume (VulkanHudLifecycle); the game
// draws a frame only through the open gate (RicartenProbe.BeginDraw and EndDraw).
internal static class SurfaceGate
{
    private const int LongestFrameWaitMs = 250;
    private static volatile bool closed;
    private static int drawing;

    // Called on the game thread before a frame; false while the gate is closed.
    public static bool TryEnterDraw()
    {
        if (closed)
        {
            return false;
        }

        Volatile.Write(ref drawing, 1);
        if (closed)
        {
            Volatile.Write(ref drawing, 0);
            return false;
        }

        return true;
    }

    // Called on the game thread once the frame is presented.
    public static void LeaveDraw() => Volatile.Write(ref drawing, 0);

    // Called on the UI thread when the activity pauses.
    public static void Close()
    {
        closed = true;
        var waited = Stopwatch.StartNew();
        while (Volatile.Read(ref drawing) != 0 && waited.ElapsedMilliseconds < LongestFrameWaitMs)
        {
            Thread.Sleep(1);
        }
    }

    // Called on the UI thread when the activity resumes.
    public static void Open() => closed = false;
}
