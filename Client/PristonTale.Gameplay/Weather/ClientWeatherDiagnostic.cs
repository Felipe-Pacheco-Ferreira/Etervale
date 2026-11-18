using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.Weather;

public readonly record struct ClientWeatherDiagnosticSample(
    bool FullRainRequested,
    bool InWorld,
    bool Applied,
    float Rain,
    float DrawnRain,
    WeatherKind ServerKind,
    byte ServerIntensity);

public sealed class ClientWeatherDiagnostic
{
    private static readonly WeatherState FullRain = new(WeatherKind.Rain, byte.MaxValue, 0, 0);
    private ClientWeatherDiagnosticSample _sample;

    public ClientWeatherDiagnosticSample Sample => _sample;

    public void SetFullRain(bool enabled)
    {
        _sample = new ClientWeatherDiagnosticSample(enabled, false, false, 0f, 0f, default, 0);
    }

    public WeatherState Resolve(WeatherState server, bool weatherShown)
    {
        var applied = _sample.FullRainRequested && weatherShown;
        _sample = _sample with { InWorld = true, Applied = applied, ServerKind = server.Kind, ServerIntensity = server.Intensity };
        return applied ? FullRain : server;
    }

    public void ObservePresentation(float rain) => _sample = _sample with { Rain = rain, DrawnRain = 0f };

    public void ObserveDraw(float rain) => _sample = _sample with { DrawnRain = rain };

    public void LeaveWorld() => _sample = _sample with { InWorld = false, Applied = false, Rain = 0f, DrawnRain = 0f };
}
