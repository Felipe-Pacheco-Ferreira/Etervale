using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

internal sealed class WebClassPreviewWarmup(PristonRenderer renderer, ScreenContext context)
{
    private const double IntervalSeconds = 0.08;
    private readonly CharacterClassRegistry _classes = new();
    private readonly HashSet<PlayerClass> _started = [];
    private Task _pending;
    private CharacterVisualSet _pendingVisuals;
    private double _nextCheck;

    public PlayerActorComposer Composer { get; } =
        new(new RendererActorScene(renderer), new PristonActorLoader(new PristonAssetCache(), previewMotionsOnly: true));

    public void Update(double seconds)
    {
        var order = CharacterSelectArt.PreviewOrder;
        if (_pending is { IsCompleted: false } || seconds < _nextCheck)
        {
            return;
        }

        if (_pending is not null)
        {
            _pending = null;
            try
            {
                Composer.PreloadPreparedGraphics(context.AssetRoot, _pendingVisuals, faceIndex: 0);
            }
            catch (Exception exception)
            {
                context.Log.Error("[CharSelect] background graphics upload failed", exception);
            }
            return;
        }

        if (_started.Count == order.Count)
        {
            return;
        }

        // The browser runs managed work on the frame thread. Schedule one completed download at a time.
        for (var index = 0; index < order.Count; index++)
        {
            var playerClass = order[index];
            if (!_started.Contains(playerClass) && context.CharacterAssetsReady(playerClass))
            {
                _started.Add(playerClass);
                _pendingVisuals = CharacterSelectScene.JobVisuals(_classes, playerClass);
                _pending = PrepareAsync(playerClass, _pendingVisuals);
                return;
            }
        }

        _nextCheck = seconds + IntervalSeconds;
    }

    private async Task PrepareAsync(PlayerClass playerClass, CharacterVisualSet visuals)
    {
        try
        {
            await Composer.PrepareAsync(context.AssetRoot, visuals, faceIndex: 0);
        }
        catch (Exception exception)
        {
            context.Log.Error($"[CharSelect] background preparation failed for {playerClass}", exception);
        }
    }
}
