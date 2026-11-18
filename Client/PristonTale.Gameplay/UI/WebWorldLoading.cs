using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.UI;

// The browser keeps drawing the character screen while preparing a world entry. These milestones let the page
// show progress over that live scene without claiming a map before the server confirms its field code.
internal static class WebWorldLoading
{
    private static readonly Lazy<WorldCatalog> Catalog = new();
    private static bool _active;
    private static int _percent;
    private static string _mapName = string.Empty;

    internal static Action<string, int, string> Report { get; set; }
    internal static bool Active => _active;

    internal static void Begin()
    {
        if (Report is null)
        {
            return;
        }

        _active = true;
        _percent = 0;
        _mapName = string.Empty;
        Report("loading", _percent, _mapName);
    }

    internal static void DownloadProgress(int done, int total) =>
        Advance(total <= 0 ? 80 : (int)(80L * Math.Clamp(done, 0, total) / total));

    internal static void Connecting() => Advance(85);

    internal static void ConfirmField(int fieldCode)
    {
        if (!_active)
        {
            return;
        }

        var fields = Catalog.Value;
        _mapName = (fields.TryGetFieldByCode(fieldCode, out var field) ? field : fields.Ricarten).Name;
        Advance(90);
    }

    internal static void InitializingScene() => Advance(95);

    internal static void ScenePrepared() => Advance(99);

    private static void Advance(int percent)
    {
        if (!_active || percent <= _percent)
        {
            return;
        }

        _percent = percent;
        Report?.Invoke("loading", percent, _mapName);
    }

    internal static void Complete()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        Report?.Invoke("ready", 100, _mapName);
        _mapName = string.Empty;
    }

    internal static void Cancel()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _mapName = string.Empty;
        Report?.Invoke("cancelled", 0, _mapName);
    }
}
