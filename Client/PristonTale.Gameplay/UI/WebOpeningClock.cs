namespace PristonTale.Gameplay.UI;

// Where the opening flight is, in seconds from its start, for the browser page: the page plays a recording of the
// flight while the game starts, then moves the live flight to the recording's moment before the recording fades out
// (Client/PristonTale.Browser/wwwroot/main.js). Only the looping opening (OpeningCinematic.Loop) reads and writes it.
internal static class WebOpeningClock
{
    private static double? _requested;

    // The live flight's moment, updated every frame it plays.
    internal static double Seconds { get; set; }

    internal static void Request(double seconds) => _requested = seconds;

    internal static bool TryTakeRequest(out double seconds)
    {
        seconds = _requested ?? 0d;
        var requested = _requested.HasValue;
        _requested = null;
        return requested;
    }
}
