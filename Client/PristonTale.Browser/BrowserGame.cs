using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;

namespace PristonTale.Browser;

internal sealed class BrowserGame : Game
{
    private readonly string _endpoint;
    private readonly PresentationSettings _presentation;
    private GameClient? _client;

    public BrowserGame(string endpoint, string language, string origin)
    {
        _endpoint = endpoint;
        var settings = new ClientSettings
        {
            Language = ClientSettings.NormalizeLanguage(language),
            NewsEndpoint = new Uri(new Uri(origin, UriKind.Absolute), "/api/client/news").AbsoluteUri
        };
        _presentation = new PresentationSettings { Requested = settings };
        _presentation.ApplyRendering(settings.WithQuality(GraphicsQuality.Low));
        _ = new GraphicsDeviceManager(this) { GraphicsProfile = GraphicsProfile.HiDef };
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
            PrepareCharacterAssetsAsync = BootstrapFiles.FetchCharacterAssetsAsync
        };
        _client.LoadContent();
        ConsoleClientLog.Instance.Info($"[Startup] game content {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds - effectsMs:F0} ms.");
        Window.TextInput += (_, args) => _client?.TypeCharacter(args.Character);
    }

    protected override void Update(GameTime gameTime) => _client?.Update(gameTime);

    protected override void Draw(GameTime gameTime) => _client?.Draw();

    protected override void UnloadContent()
    {
        _client?.Dispose();
        _client = null;
        base.UnloadContent();
    }
}
