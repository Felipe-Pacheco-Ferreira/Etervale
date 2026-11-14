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
                chosen = saved;
            }
        }

        quality = (int)chosen;
    }

    public GraphicsQuality Quality => (GraphicsQuality)Volatile.Read(ref quality);

    // The renderer's options for the chosen quality. Low is cut to what the phone draws at 60 frames (docs/android.md,
    // "Graphics quality"): a 1024 shadow map in one cascade, and the painted sky in place of the realistic one.
    public ClientSettings Rendering
    {
        get
        {
            var settings = new ClientSettings().WithQuality(Quality);
            return Quality == GraphicsQuality.Low ? settings with { ShadowMapSize = 1024, ShadowCascades = 1, RealisticSky = false } : settings;
        }
    }

    // The width the world is drawn at, the height following the screen's shape; Low draws fewer pixels. Read once, when the
    // game starts: a quality chosen later takes its width at the next start.
    public int RenderWidth => Quality == GraphicsQuality.Low ? LowRenderWidth : RenderWidthAbove;

    private const int LowRenderWidth = 1088;
    private const int RenderWidthAbove = 1280;

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
