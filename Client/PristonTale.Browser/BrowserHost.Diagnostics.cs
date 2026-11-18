using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.Weather;

namespace PristonTale.Browser;

public static partial class BrowserHost
{
    [JSExport]
    public static void EnableFrameProfiling()
    {
        if (_game is null)
        {
            return;
        }
        _game.EnableFrameProfiling();
        WebCharacterSelectOverlay.ProfileStageInput = true;
    }

    [JSExport]
    public static string FrameProfile() => _game is null ? "{}" :
        JsonSerializer.Serialize(_game.FrameProfile(), BrowserDiagnosticsJsonContext.Default.BrowserFrameProfile);

    [JSExport]
    public static bool SetDiagnosticFullRain(bool enabled) => _game?.SetDiagnosticFullRain(enabled) ?? false;
}

internal readonly record struct BrowserFrameProfile(
    bool Enabled,
    long UpdateCalls,
    long DrawCalls,
    int LastUpdatesPerDraw,
    int MaxUpdatesPerDraw,
    bool FixedTimeStep,
    double InactiveSleepMs,
    bool Active,
    string Quality,
    float RenderScale,
    int MaxFps,
    int BackgroundMaxFps,
    int CanvasWidth,
    int CanvasHeight,
    FrameTimingSample Timing,
    ClientWeatherDiagnosticSample WeatherDiagnostic);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserFrameProfile))]
internal partial class BrowserDiagnosticsJsonContext : JsonSerializerContext;
