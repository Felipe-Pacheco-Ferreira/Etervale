using PristonTale.Contracts.World;
using PristonTale.Gameplay.Weather;
using PristonTale.Gameplay.World;
using PristonTale.Engine3D.Rendering;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.World.Fields;
using System.Reflection;

namespace PristonTale.Tests;

internal static class ClientWeatherDiagnosticTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("client weather diagnostic leaves normal server weather unchanged and isolates each client", NormalAsync);
        yield return ("client weather diagnostic reaches full authored rain without claiming a draw before it happens", FullRainAsync);
        yield return ("client weather diagnostic respects disabled fields and resets application on leave, reentry and normal mode", ResetAsync);
        yield return ("client weather diagnostic production scene respects settings, field eligibility and presence across world reentry", SceneAsync);
    }

    private static Task NormalAsync()
    {
        var first = new ClientWeatherDiagnostic();
        var second = new ClientWeatherDiagnostic();
        var server = new WeatherState(WeatherKind.Rain, 96, 64, 32);
        Assert.True(first.Resolve(server, true) == server && !first.Sample.Applied, "normal weather was overridden");
        first.SetFullRain(true);
        var presented = first.Resolve(server, true);
        Assert.True(presented.Kind == WeatherKind.Rain && presented.Intensity == byte.MaxValue && presented.Wind == 0, "full rain parameters changed");
        Assert.True(server.Intensity == 96 && first.Sample.ServerIntensity == 96, "the diagnostic changed authoritative weather");
        Assert.True(second.Resolve(server, true) == server && !second.Sample.FullRainRequested, "another client inherited the diagnostic");
        return Task.CompletedTask;
    }

    private static Task FullRainAsync()
    {
        var diagnostic = new ClientWeatherDiagnostic();
        var presentation = new WeatherPresentation(new Random(7));
        diagnostic.SetFullRain(true);
        var server = WeatherState.Clear;
        var presented = diagnostic.Resolve(server, true);
        presentation.Update(presented, true, WeatherPresentation.RainSeconds);
        diagnostic.ObservePresentation(presentation.Rain);
        Assert.True(diagnostic.Sample.Applied && diagnostic.Sample.Rain == 1f && diagnostic.Sample.DrawnRain == 0f,
            "an undrawn presentation was reported as rendered rain");
        diagnostic.ObserveDraw(presentation.Rain);
        Assert.True(diagnostic.Sample.DrawnRain == 1f && diagnostic.Sample.ServerKind == WeatherKind.Clear, "actual draw density or server weather was lost");
        Assert.True(presentation.View.Rain == 1f && presentation.View.Flash == 0f, "the authored full rain became a synthetic lightning storm");
        diagnostic.ObservePresentation(presentation.Rain);
        Assert.True(diagnostic.Sample.DrawnRain == 0f, "a later update without a draw retained stale proof");
        return Task.CompletedTask;
    }

    private static Task ResetAsync()
    {
        var diagnostic = new ClientWeatherDiagnostic();
        diagnostic.SetFullRain(true);
        Assert.True(diagnostic.Resolve(WeatherState.Clear, false) == WeatherState.Clear && !diagnostic.Sample.Applied,
            "disabled weather or a sheltered field forced rain");
        diagnostic.Resolve(WeatherState.Clear, true);
        diagnostic.ObservePresentation(1f);
        diagnostic.ObserveDraw(1f);
        diagnostic.LeaveWorld();
        Assert.True(diagnostic.Sample.FullRainRequested && !diagnostic.Sample.InWorld && !diagnostic.Sample.Applied
            && diagnostic.Sample.Rain == 0f && diagnostic.Sample.DrawnRain == 0f, "leaving kept a false wet-frame claim");
        Assert.True(diagnostic.Resolve(WeatherState.Clear, true).Intensity == byte.MaxValue && diagnostic.Sample.Applied, "diagnostic reentry lost its explicit request");
        diagnostic.SetFullRain(false);
        var server = new WeatherState(WeatherKind.Overcast, 0, 32, 16);
        Assert.True(diagnostic.Resolve(server, true) == server && !diagnostic.Sample.FullRainRequested && !diagnostic.Sample.Applied,
            "normal mode did not restore the current authoritative weather");
        return Task.CompletedTask;
    }

    private static Task SceneAsync()
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var update = typeof(GameScene).GetMethod("UpdateWeather", instance)!.CreateDelegate<Action<GameScene, float>>();
        var observe = typeof(GameScene).GetMethod("ObserveWeather", instance)!.CreateDelegate<Action<GameScene, PresenceUpdate>>();
        var field = typeof(GameScene).GetField("_currentField", instance)!;
        var diagnostic = new ClientWeatherDiagnostic();
        var settings = new PresentationSettings();
        diagnostic.SetFullRain(true);
        var scene = new GameScene(null!, null!, presentation: settings) { WeatherDiagnostic = diagnostic };
        var received = new PresenceUpdate(0, [], [], [], []) { Weather = WeatherState.Clear };
        observe(scene, received);
        update(scene, WeatherPresentation.CloudSeconds);
        Assert.True(scene.ServerWeather == WeatherState.Clear && diagnostic.Sample.Applied && diagnostic.Sample.Rain == 1f
            && diagnostic.Sample.DrawnRain == 0f, "the real scene changed server state or claimed a draw without graphics");
        settings.Requested = settings.Requested with { WeatherEnabled = false };
        update(scene, WeatherPresentation.CloudSeconds);
        Assert.True(!diagnostic.Sample.Applied && diagnostic.Sample.Rain == 0f, "the actual scene ignored disabled weather");
        settings.Requested = settings.Requested with { WeatherEnabled = true };
        field.SetValue(scene, Ice1Field.Definition);
        update(scene, WeatherPresentation.CloudSeconds);
        Assert.True(!diagnostic.Sample.Applied && diagnostic.Sample.Rain == 0f, "the actual scene forced rain into an ice field");
        field.SetValue(scene, null);
        update(scene, WeatherPresentation.CloudSeconds);
        Assert.True(diagnostic.Sample.Applied && diagnostic.Sample.Rain == 1f, "returning to an eligible field lost the diagnostic");
        scene.Dispose();
        Assert.True(!diagnostic.Sample.InWorld && !diagnostic.Sample.Applied && diagnostic.Sample.DrawnRain == 0f, "the disposed scene retained wet-frame proof");
        using var next = new GameScene(null!, null!, presentation: settings) { WeatherDiagnostic = diagnostic };
        var overcast = new WeatherState(WeatherKind.Overcast, 0, 32, 16);
        observe(next, received with { Weather = overcast });
        update(next, WeatherPresentation.CloudSeconds);
        Assert.True(next.ServerWeather == overcast && diagnostic.Sample.Applied && diagnostic.Sample.Rain == 1f, "reentry changed the new authoritative weather");
        diagnostic.SetFullRain(false);
        update(next, WeatherPresentation.CloudSeconds);
        Assert.True(!diagnostic.Sample.Applied && !diagnostic.Sample.FullRainRequested && diagnostic.Sample.Rain == 0f
            && next.ServerWeather == overcast, "normal mode failed to restore server weather in the real scene");
        return Task.CompletedTask;
    }
}
