using System.Diagnostics;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Motion;
using PristonTale.Assets;

namespace PristonTale.Gameplay.World.Remote;

public interface IModelActorComposer
{
    Task PrepareAsync(string assetRoot, string modelInxPath) => Task.CompletedTask;
    bool AdvanceTextureUpload(string assetRoot, string modelInxPath) => true;
    ComposedModelActor Compose(string assetRoot, string modelInxPath, Matrix world, MotionCode motion);
}

// Alternate: the model's sub model on its own skeleton (AlternateModel), hidden until a motion only it has plays.
public readonly record struct ComposedModelActor(ActorHandle Handle, PristonModelInfo Info, ActorHandle Alternate = default,
    PristonModelInfo AlternateInfo = null);

public sealed class ModelActorComposer(IActorScene scene, PristonActorLoader actorLoader) : IModelActorComposer
{
    private const int MaxTextureUploadsPerFrame = 2;
    private static readonly TimeSpan TextureUploadBudget = TimeSpan.FromMilliseconds(1);
    private readonly IActorScene _scene = scene;
    private readonly PristonActorLoader _actorLoader = actorLoader;
    private readonly Dictionary<string, PreparedModel> _prepared = new(StringComparer.OrdinalIgnoreCase);

    private sealed class PreparedModel(Task<(PristonActor Main, PristonActor Alternate)> actors)
    {
        public Task<(PristonActor Main, PristonActor Alternate)> Actors { get; } = actors;
        public IEnumerator<long> TextureUploads { get; set; }
        public bool TexturesReady { get; set; }
    }

    public Task PrepareAsync(string assetRoot, string modelInxPath)
    {
        var path = AssetFiles.Combine(assetRoot, modelInxPath);
        if (!_prepared.TryGetValue(path, out var model))
        {
            var preparation = Task.Run(async () =>
            {
                var actor = _actorLoader.Load(assetRoot, path);
                await _scene.PrepareActorResourcesAsync(actor);
                var alternate = LoadAlternate(assetRoot, actor);
                if (alternate is not null)
                {
                    await _scene.PrepareActorResourcesAsync(alternate);
                }

                return (actor, alternate);
            });
            model = new PreparedModel(preparation);
            _prepared.Add(path, model);
        }

        return model.Actors;
    }

    public bool AdvanceTextureUpload(string assetRoot, string modelInxPath)
    {
        if (!OperatingSystem.IsAndroid() || !_prepared.TryGetValue(AssetFiles.Combine(assetRoot, modelInxPath), out var model)
            || !model.Actors.IsCompletedSuccessfully || model.TexturesReady)
        {
            return true;
        }

        var (main, alternate) = model.Actors.Result;
        var uploads = model.TextureUploads ??= (_scene.UploadPreparedActorTextures(main)
            .Concat(alternate is null ? [] : _scene.UploadPreparedActorTextures(alternate))).GetEnumerator();
        var started = Stopwatch.GetTimestamp();
        try
        {
            for (var count = 0; count < MaxTextureUploadsPerFrame; count++)
            {
                if (!uploads.MoveNext())
                {
                    uploads.Dispose();
                    model.TextureUploads = null;
                    model.TexturesReady = true;
                    return true;
                }

                if (Stopwatch.GetElapsedTime(started) >= TextureUploadBudget)
                {
                    break;
                }
            }

            return false;
        }
        catch
        {
            uploads.Dispose();
            model.TextureUploads = null;
            throw;
        }
    }

    // An unreadable sub model leaves the model without its alternate rather than without the model.
    private PristonActor LoadAlternate(string assetRoot, PristonActor main)
    {
        if (AlternateModel.Path(assetRoot, main.Info) is not { } path)
        {
            return null;
        }

        try
        {
            var alternate = _actorLoader.Load(assetRoot, path);
            return AlternateModel.Alternates(main.Info, alternate.Info) ? alternate : null;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return null;
        }
    }

    public ComposedModelActor Compose(string assetRoot, string modelInxPath, Matrix world, MotionCode motion)
    {
        var path = AssetFiles.Combine(assetRoot, modelInxPath);
        if (!_prepared.TryGetValue(path, out var model) || !model.Actors.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("model actor must be prepared before composition");
        }

        var (actor, alternate) = model.Actors.Result;
        var handle = _scene.AddActor(new PristonActorInstance(actor, world, motion));
        if (alternate is null)
        {
            return new ComposedModelActor(handle, actor.Info);
        }

        var alternateHandle = _scene.AddActor(new PristonActorInstance(alternate, world, motion));
        _scene.SetActorHidden(alternateHandle, true);
        return new ComposedModelActor(handle, actor.Info, alternateHandle, alternate.Info);
    }
}
