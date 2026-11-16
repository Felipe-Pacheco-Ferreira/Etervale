using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;

// The graphics quality picked in the in-game menu: the desktop presets (ClientSettings.WithQuality), kept in the app's
// client-settings.json. The menu asks on the UI thread; the game thread applies it at the next frame.
internal sealed class MobileGraphics
{
    // A quality is saved only after it has drawn this long, so one the phone cannot hold (the system ends the app for
    // memory) is not chosen again at the next start.
    private const double SettleSeconds = 30;
    private const int NoRequest = -1;

    private readonly ClientSettingsStore store;
    private int quality;
    private int requested = NoRequest;
    private double settling;
    private int sortByMaterial;
    private int diagnostic;

    public LightingDiagnostic Diagnostic
    {
        get => (LightingDiagnostic)Volatile.Read(ref diagnostic);
        set => Volatile.Write(ref diagnostic, (int)value);
    }

    public bool SortByMaterial
    {
        get => Volatile.Read(ref sortByMaterial) != 0;
        set => Volatile.Write(ref sortByMaterial, value ? 1 : 0);
    }

    // folder: the app's own files folder, where client-settings.json lives.
    public MobileGraphics(string folder)
    {
        var path = Path.Combine(folder, "client-settings.json");
        store = new ClientSettingsStore(path, ConsoleClientLog.Instance);
        var chosen = GraphicsQuality.Low;
        if (File.Exists(path))
        {
            var saved = store.Load().Quality;
            if (store.LastError.Length == 0 && saved != GraphicsQuality.Custom)
            {
                chosen = saved == GraphicsQuality.Ultra ? GraphicsQuality.High : saved;
            }
        }

        quality = (int)chosen;
    }

    public GraphicsQuality Quality => (GraphicsQuality)Volatile.Read(ref quality);

    // Mobile budgets for the shared renderer. Higher presets retain distinct lighting and post-processing features.
    public ClientSettings Rendering
    {
        get
        {
            var preset = Quality;
            var settings = new ClientSettings().WithQuality(preset);
            return preset switch
            {
                GraphicsQuality.Low => settings with
                {
                    ShadowMapSize = 1024, ShadowCascades = 1, ShadowUpdateRate = 30, RealisticSky = false
                },
                GraphicsQuality.Medium => settings with
                {
                    ShadowMapSize = 1024, ShadowCascades = 2, ShadowUpdateRate = 30,
                    RealisticSky = false, AmbientOcclusion = false, Bloom = BloomQuality.Off, AntiAliasing = AntiAliasingMode.Fxaa
                },
                GraphicsQuality.High => settings with
                {
                    ShadowMapSize = 1024, ShadowCascades = 3, ShadowUpdateRate = 30, LampShadowTileSize = 512,
                    ShadowFilter = ShadowFilterMode.Pcf, LampShadowCount = 2,
                    CloudQuality = CloudQuality.Low, ContactShadows = false, AntiAliasing = AntiAliasingMode.Off,
                    ScreenSpaceReflections = false, AmbientOcclusion = false, VolumetricLight = false, LocalLightLimit = 8,
                    RealisticSky = false
                },
                _ => settings
            };
        }
    }

    public int SceneWidthLimit => Quality switch
    {
        GraphicsQuality.Low => 1280,
        GraphicsQuality.Medium => 1440,
        _ => 1600
    };

    // Called on the UI thread.
    public void Request(GraphicsQuality next) => Volatile.Write(ref requested, (int)next);

    // Called on the game thread every frame: applies a requested quality, keeping the exposure in use, and saves it once
    // it has drawn SettleSeconds.
    public void Update(PresentationSettings presentation, double elapsedSeconds)
    {
        var next = Interlocked.Exchange(ref requested, NoRequest);
        if (next != NoRequest && next != Volatile.Read(ref quality))
        {
            Volatile.Write(ref quality, next);
            presentation.ApplyRendering(Rendering with { ExposureCompensation = presentation.Rendering.ExposureCompensation });
            settling = SettleSeconds;
        }

        if (settling > 0 && (settling -= elapsedSeconds) <= 0)
        {
            store.QueueSave(Rendering);
        }
    }
}

internal enum LightingDiagnostic
{
    Normal,
    NoSolarShadows,
    NoLocalLights,
    UnlitMaterial
}
