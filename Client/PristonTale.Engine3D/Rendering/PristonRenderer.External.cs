using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Engine3D.Rendering;

public sealed partial class PristonRenderer
{
    private int _nextExternalMesh = 1;
    private int _nextExternalStage = 1;
    private readonly Dictionary<RenderStageHandle, ExternalStageResource> _externalStages = new();
    private readonly Dictionary<PristonActor, ExternalActorResource> _externalActors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(PristonSkyLibrary Library, int Profile), int[]> _externalSky = new();

    private sealed record ExternalAnimatedResource(AnimatedModelRuntime Runtime, int[] Meshes);
    private sealed record ExternalStageResource(int Id, int[] Meshes, ExternalAnimatedResource[] Animated);
    private sealed class ExternalActorResource(PreparedActorGeometry geometry, int[] meshes)
    {
        public PreparedActorGeometry Geometry { get; } = geometry;
        public int[] Meshes { get; } = meshes;
        public int References { get; set; }
        public bool Pinned { get; set; }
    }

    private Task PrepareExternalStageAsync(PristonStage stage, IReadOnlyList<PristonModel> models, CancellationToken cancellationToken)
    {
        var tasks = new List<Task>
        {
            _externalRenderer.PrepareMaterialsAsync(Path.GetDirectoryName(stage.Path) ?? ".", PristonMaterialUsage.ForStage(stage), cancellationToken)
        };
        foreach (var model in models)
        {
            tasks.Add(_externalRenderer.PrepareMaterialsAsync(Path.GetDirectoryName(model.Path) ?? ".", PristonMaterialUsage.ForModel(model), cancellationToken));
        }
        return Task.WhenAll(tasks);
    }

    private RenderStageHandle AddExternalStage(PristonStage stage, IEnumerable<PristonSceneryInstance> models)
    {
        var (lights, glows) = _overrideLights is null ? BuildStageLights(stage, _log) : (_overrideLights, _overrideLights);
        var handle = new RenderStageHandle(stage, _lampIntensity, _lampRangeScale, lights,
            ApplyGlowTuning(glows, _glowIntensity, _glowScale), _glowIntensity, _glowScale)
        {
            Bounds = stage.Bounds,
            LightField = PristonStageLightField.Build(stage)
        };
        var meshIds = new List<int>();
        var animated = new List<ExternalAnimatedResource>();
        try
        {
            AddExternalStaticGroups(BuildStageGroups(stage), stage.Materials, Path.GetDirectoryName(stage.Path) ?? ".", meshIds);
            var baked = PristonStageBakedLight.For(stage);
            foreach (var instance in models)
            {
                var model = instance.Model;
                var directory = Path.GetDirectoryName(model.Path) ?? ".";
                if (!model.IsAnimated)
                {
                    AddExternalStaticGroups(WithBakedLight(PristonModelGeometry.BuildGroups(model, instance.World, Color.White), model.Materials, baked),
                        model.Materials, directory, meshIds);
                    continue;
                }
                var prepared = PrepareAnimatedModel(model, instance.World, baked);
                var palette = new PristonSkinPalette(prepared.BoneCount);
                var runtime = new AnimatedModelRuntime(model, palette, 0) { World = instance.World };
                var modelMeshes = new List<int>();
                foreach (var batch in prepared.Batches)
                {
                    var id = CreateExternalMesh(directory, batch.Material, batch.Mesh, prepared.BoneCount);
                    meshIds.Add(id);
                    modelMeshes.Add(id);
                    runtime.IncludeBindBounds(batch.Mesh.Bounds);
                }
                handle.AnimatedModels.Add(runtime);
                animated.Add(new ExternalAnimatedResource(runtime, modelMeshes.ToArray()));
            }
            var resource = new ExternalStageResource(_nextExternalStage++, meshIds.ToArray(), animated.ToArray());
            _externalRenderer.CreateStage(resource.Id, resource.Meshes);
            _externalStages.Add(handle, resource);
            _stages.Add(handle);
            UpdateExternalStage(resource);
            AssignEmitterGlowColors(handle, stage);
            StageBounds = UnionStageBounds();
            return handle;
        }
        catch
        {
            foreach (var id in meshIds)
            {
                _externalRenderer.RemoveMesh(id);
            }
            throw;
        }
    }

    private void AddExternalStaticGroups(Dictionary<int, List<PristonCornerVertex>> groups, IReadOnlyList<PristonMaterial> materials,
        string directory, List<int> meshIds)
    {
        foreach (var (materialIndex, corners) in groups)
        {
            var material = materialIndex >= 0 && materialIndex < materials.Count ? materials[materialIndex] : FallbackMaterial;
            if (corners.Count == 0 || material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }
            var prepared = _meshBuilder.PrepareStaticMesh(corners.ToArray());
            var id = _nextExternalMesh++;
            _externalRenderer.CreateMesh(id, ExternalMesh(directory, material, prepared.Vertices, prepared.Indices));
            meshIds.Add(id);
        }
    }

    private int CreateExternalMesh(string directory, PristonMaterial material, PreparedSkinnedMesh mesh, int boneCount)
    {
        var vertices = new PristonStaticVertex[mesh.Vertices.Length];
        var bones = new float[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            var vertex = mesh.Vertices[index];
            vertices[index] = new PristonStaticVertex(vertex.Position, vertex.Normal, vertex.Color, vertex.TexCoord, vertex.TexCoord1, vertex.TexCoord2);
            bones[index] = vertex.BoneIndex;
        }
        var id = _nextExternalMesh++;
        _externalRenderer.CreateMesh(id, ExternalMesh(directory, material, vertices, mesh.Indices, bones, boneCount));
        return id;
    }

    internal static ExternalSceneMesh ExternalMesh(string directory, PristonMaterial material, PristonStaticVertex[] vertices, int[] indices,
        float[] bones = null, int boneCount = 0)
    {
        var positions = new float[vertices.Length * 3];
        var normals = new float[vertices.Length * 3];
        var colors = new float[vertices.Length * 4];
        var uv = new float[vertices.Length * 2];
        var uv1 = new float[uv.Length];
        var uv2 = new float[uv.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            var vertex = vertices[index];
            var p = index * 3;
            positions[p] = vertex.Position.X;
            positions[p + 1] = vertex.Position.Y;
            positions[p + 2] = vertex.Position.Z;
            normals[p] = vertex.Normal.X;
            normals[p + 1] = vertex.Normal.Y;
            normals[p + 2] = vertex.Normal.Z;
            var c = index * 4;
            colors[c] = vertex.Color.R / 255f;
            colors[c + 1] = vertex.Color.G / 255f;
            colors[c + 2] = vertex.Color.B / 255f;
            colors[c + 3] = vertex.Color.A / 255f;
            var t = index * 2;
            uv[t] = vertex.TextureCoordinate.X;
            uv[t + 1] = vertex.TextureCoordinate.Y;
            uv1[t] = vertex.TextureCoordinate1.X;
            uv1[t + 1] = vertex.TextureCoordinate1.Y;
            uv2[t] = vertex.TextureCoordinate2.X;
            uv2[t + 1] = vertex.TextureCoordinate2.Y;
        }
        return new ExternalSceneMesh(directory, material, positions, normals, colors, uv, uv1, uv2, indices, bones ?? [], boneCount);
    }

    private ExternalActorResource ExternalActorResources(PristonActor actor, bool pin = false)
    {
        if (!_externalActors.TryGetValue(actor, out var resource))
        {
            if (actor.Gltf is not null)
            {
                throw new NotSupportedException("The external scene renderer requires prepared original actor geometry.");
            }
            var prepared = PreparedActorGeometries.GetValue(actor, PrepareActorGeometry);
            var meshes = new List<int>();
            try
            {
                var directory = Path.GetDirectoryName(actor.Mesh.Path) ?? ".";
                foreach (var batch in prepared.Batches)
                {
                    meshes.Add(CreateExternalMesh(directory, batch.Material, batch.Mesh, prepared.BoneMap.Length));
                }
                resource = new ExternalActorResource(prepared, meshes.ToArray());
                _externalActors.Add(actor, resource);
            }
            catch
            {
                foreach (var id in meshes)
                {
                    _externalRenderer.RemoveMesh(id);
                }
                throw;
            }
        }
        resource.Pinned |= pin;
        return resource;
    }

    private ActorRuntime BuildExternalActor(PristonActorInstance instance)
    {
        var resource = ExternalActorResources(instance.Actor);
        var runtime = new ActorRuntime(instance) { ExternalMeshes = resource.Meshes };
        runtime.SetBoneMap(resource.Geometry.BoneMap);
        foreach (var batch in resource.Geometry.Batches)
        {
            runtime.IncludeExternalBounds(batch.Mesh.Bounds);
        }
        UpdateActor(runtime);
        resource.References++;
        return runtime;
    }

    private void ReleaseExternalActor(ActorRuntime runtime)
    {
        if (_externalRenderer is null || !_externalActors.TryGetValue(runtime.Instance.Actor, out var resource))
        {
            return;
        }
        resource.References--;
        if (resource.References > 0 || resource.Pinned)
        {
            return;
        }
        foreach (var id in resource.Meshes)
        {
            _externalRenderer.RemoveMesh(id);
        }
        _externalActors.Remove(runtime.Instance.Actor);
    }

    private void UpdateExternalStage(ExternalStageResource stage)
    {
        foreach (var model in stage.Animated)
        {
            var runtime = model.Runtime;
            if (!runtime.PaletteIsCurrent())
            {
                var pose = runtime.Animator.Evaluate(runtime.Frame);
                for (var bone = 0; bone < runtime.PaletteCount; bone++)
                {
                    runtime.Palette.Matrices[bone] = pose[bone] * runtime.World;
                }
                runtime.Palette.Pack(_motionFrame);
                runtime.SetWorldBounds(TransformBoundsByPalette(runtime.BindBounds, runtime.Palette.Matrices));
                runtime.MarkPaletteCurrent();
                foreach (var id in model.Meshes)
                {
                    _externalRenderer.UpdateMeshPose(id, runtime.Palette.ShaderRows);
                }
            }
        }
    }

    private void DrawExternalScene(PristonCamera camera)
    {
        var lighting = BuildFrameLighting(camera.Target);
        var skyVisible = Sky is not null && !Underground;
        var currentSky = skyVisible ? ExternalSkyMeshes(SkyProfileNow) : [];
        var previousSky = skyVisible && _previousSkyProfile >= 0 && _skyBlend < 1f ? ExternalSkyMeshes(_previousSkyProfile) : [];
        _externalRenderer.SetSkyLayers(currentSky, previousSky, 1f - _skyBlend,
            MathF.Min(camera.Position.Y, SkyCameraCeiling) / SkyCameraHeightDivisor, SkyFlash);
        foreach (var stage in _externalStages.Values)
        {
            UpdateExternalStage(stage);
        }
        for (var slot = 0; slot < _actors.Count; slot++)
        {
            var actor = _actors[slot];
            if (!actor.Alive)
            {
                continue;
            }
            var shade = actor.Shade;
            var center = (actor.WorldBounds.Min + actor.WorldBounds.Max) * 0.5f;
            var state = new ExternalActorState(!actor.Hidden && shade?.Hidden != true && actor.Instance.VisibleInMainPass,
                actor.ExternalColorAdd, actor.ExternalOutline, shade?.ShownTint ?? Vector3.Zero, shade?.ShownOpacity ?? 1f,
                shade?.Shadowed ?? true, shade?.Wobble ?? false, shade?.Scale ?? 1f,
                FloorLightAt(new Vector3(center.X, actor.WorldBounds.Min.Y, center.Z)),
                actor.ExternalOverlay, shade?.OverlayForm ?? 0, actor.WorldBounds, actor.Palette.Revision);
            _externalRenderer.UpdateActor(new ActorHandle(slot, actor.Generation), state, actor.Palette.ShaderRows);
        }
        _externalRenderer.Draw(new ExternalSceneFrame(camera.View, camera.Projection, camera.Position, camera.Target,
            lighting.AmbientColor, lighting.GroundAmbientColor, lighting.Sun.Direction, lighting.Sun.DiffuseColor,
            lighting.PointLights, BackgroundColor ?? SkyColor, WeatherFog(DistanceFog), _renderTime, _lastDeltaSeconds, Underground,
            DirectionalShadowsEnabled(lighting)));
        _motionFrame++;
    }

    private int[] ExternalSkyMeshes(int profile)
    {
        var key = (Sky, profile);
        if (_externalSky.TryGetValue(key, out var ids))
        {
            return ids;
        }
        var meshes = Sky.GetExternalMeshes(profile);
        ids = new int[meshes.Length];
        for (var index = 0; index < meshes.Length; index++)
        {
            ids[index] = _nextExternalMesh++;
            _externalRenderer.CreateMesh(ids[index], meshes[index]);
        }
        _externalSky.Add(key, ids);
        return ids;
    }

    private void RemoveExternalStage(RenderStageHandle handle)
    {
        if (_externalRenderer is null || !_externalStages.Remove(handle, out var stage))
        {
            return;
        }
        _externalRenderer.RemoveStage(stage.Id);
        foreach (var id in stage.Meshes)
        {
            _externalRenderer.RemoveMesh(id);
        }
    }

    private void DisposeExternalScene()
    {
        if (_externalRenderer is null)
        {
            return;
        }
        foreach (var resource in _externalActors.Values)
        {
            foreach (var id in resource.Meshes)
            {
                _externalRenderer.RemoveMesh(id);
            }
        }
        _externalActors.Clear();
        foreach (var meshes in _externalSky.Values)
        {
            foreach (var id in meshes)
            {
                _externalRenderer.RemoveMesh(id);
            }
        }
        _externalSky.Clear();
    }
}
