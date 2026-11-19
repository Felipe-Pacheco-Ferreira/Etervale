using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;

namespace PristonTale.Browser;

internal sealed class BrowserGame : Game
{
    private readonly string _endpoint;
    private readonly PresentationSettings _presentation;
    private GameClient? _client;
    private FrameTiming? _frameTiming;
    private FrameTimingSample _frameSample;
    private long _profileUpdateCalls;
    private long _profileDrawCalls;
    private int _updatesSinceDraw;
    private int _lastUpdatesPerDraw;
    private int _maxUpdatesPerDraw;

    public BrowserGame(string endpoint, string language, string origin)
    {
        _endpoint = endpoint;
        var savedSettings = BrowserHost.StartSettings;
        var settings = savedSettings with
        {
            Language = ClientSettings.NormalizeLanguage(language),
            DiscordUrl = string.IsNullOrWhiteSpace(savedSettings.DiscordUrl) ? ClientSettings.CommunityDiscordUrl : savedSettings.DiscordUrl,
            InstagramUrl = string.IsNullOrWhiteSpace(savedSettings.InstagramUrl) ? ClientSettings.CommunityInstagramUrl : savedSettings.InstagramUrl,
            NewsEndpoint = new Uri(new Uri(origin, UriKind.Absolute), "/api/client/news").AbsoluteUri
        };
        _presentation = new PresentationSettings { Requested = settings };
        _presentation.ApplyRendering(settings);
        BrowserHost.Attach(_presentation);
        _ = new GraphicsDeviceManager(this) { GraphicsProfile = GraphicsProfile.HiDef };
        // The browser's animation callback owns pacing; one callback advances and draws the client once.
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void LoadContent()
    {
        var effectFiles = Directory.GetFiles(Path.Combine(Content.RootDirectory, "Effects"), "*.xnb");
        if (effectFiles.Length != 18)
        {
            throw new InvalidDataException($"Expected 18 browser effects, found {effectFiles.Length}.");
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var slowest = (Name: "", Ms: 0d);
        foreach (var file in effectFiles.Order(StringComparer.Ordinal))
        {
            var effectStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            Content.Load<Effect>("Effects/" + Path.GetFileNameWithoutExtension(file));
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(effectStarted).TotalMilliseconds;
            if (ms > slowest.Ms)
            {
                slowest = (Path.GetFileNameWithoutExtension(file), ms);
            }
        }

        var effectsMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        ConsoleClientLog.Instance.Info($"Loaded {effectFiles.Length} browser effects in {effectsMs:F0} ms (slowest {slowest.Name} {slowest.Ms:F0} ms); "
            + $"{started * 1000d / System.Diagnostics.Stopwatch.Frequency - BrowserHost.StartedMs:F0} ms after Start.");
        _client = new GameClient(GraphicsDevice, Content, BootstrapFiles.AssetRoot, _endpoint, _presentation)
        {
            PrepareWorldAssetsAsync = BootstrapFiles.FetchWorldAssetsAsync,
            PrepareCharacterAssetsAsync = BootstrapFiles.FetchCharacterAssetsAsync,
            CharacterAssetsReady = BootstrapFiles.AreCharacterAssetsReady
        };
        _client.LoadContent();
        ConsoleClientLog.Instance.Info($"[Startup] game content {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds - effectsMs:F0} ms.");
        Window.TextInput += (_, args) => _client?.TypeCharacter(args.Character);
    }

    protected override void Update(GameTime gameTime)
    {
        if (_frameTiming is null)
        {
            _client?.Update(gameTime, IsActive);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        _profileUpdateCalls++;
        _updatesSinceDraw++;
        _client?.Update(gameTime, IsActive);
        _frameTiming.RecordUpdate(Stopwatch.GetTimestamp() - started, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_frameTiming is null)
        {
            _client?.Draw();
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        _profileDrawCalls++;
        _lastUpdatesPerDraw = _updatesSinceDraw;
        _maxUpdatesPerDraw = Math.Max(_maxUpdatesPerDraw, _updatesSinceDraw);
        _updatesSinceDraw = 0;
        _client?.Draw();
        _frameTiming.RecordDraw(started, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    protected override void EndDraw()
    {
        if (_frameTiming is null)
        {
            base.EndDraw();
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        base.EndDraw();
        _frameTiming.RecordPresentation(Stopwatch.GetTimestamp() - started, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    internal void EnableFrameProfiling()
    {
        _frameTiming = new FrameTiming();
        BrowserExternalSceneRenderer.ProfileTiming = _frameTiming;
        PristonPoseMemoization.ProfileTiming = _frameTiming;
        _frameSample = default;
        _profileUpdateCalls = 0;
        _profileDrawCalls = 0;
        _updatesSinceDraw = 0;
        _lastUpdatesPerDraw = 0;
        _maxUpdatesPerDraw = 0;
    }

    internal void ResetFrameProfiling()
    {
        if (_frameTiming is not null)
        {
            EnableFrameProfiling();
        }
    }

    internal bool SetDiagnosticFullRain(bool enabled)
    {
        if (_client is null || (enabled && _frameTiming is null))
        {
            return false;
        }

        _client.WeatherDiagnostic.SetFullRain(enabled);
        return true;
    }

    internal BrowserFrameProfile FrameProfile()
    {
        if (_frameTiming?.TrySample(Stopwatch.GetTimestamp(), out var sample) == true)
        {
            _frameSample = sample;
        }

        var settings = _presentation.Requested;
        return new BrowserFrameProfile(_frameTiming is not null, _profileUpdateCalls, _profileDrawCalls,
            _lastUpdatesPerDraw, _maxUpdatesPerDraw, IsFixedTimeStep, InactiveSleepTime.TotalMilliseconds, IsActive,
            settings.Quality.ToString(), settings.RenderScale, settings.MaxFps, settings.BackgroundMaxFps,
            GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, _frameSample, _client?.WeatherDiagnostic.Sample ?? default, _frameTiming?.AnimationPoses ?? default);
    }

    protected override void UnloadContent()
    {
        BrowserExternalSceneRenderer.ProfileTiming = null;
        PristonPoseMemoization.ProfileTiming = null;
        _client?.Dispose();
        _client = null;
        base.UnloadContent();
    }
}
