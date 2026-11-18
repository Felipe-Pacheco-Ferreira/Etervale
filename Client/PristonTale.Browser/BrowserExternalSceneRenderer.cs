using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Browser;

internal sealed partial class BrowserExternalSceneRenderer : IExternalSceneRenderer
{
    internal static FrameTiming? ProfileTiming { private get; set; }

    public void Compose()
    {
        var timing = ProfileTiming;
        var started = timing is null ? 0 : Stopwatch.GetTimestamp();
        ComposeSceneCore(_scene);
        if (timing is not null)
        {
            timing.RecordSceneBridge(Stopwatch.GetTimestamp() - started);
        }
    }

    private const int VertexStride = 17;
    private const int ActorStride = 24;
    private const int FrameHeader = 68;
    private static int _nextScene;
    private readonly int _scene = Interlocked.Increment(ref _nextScene);
    private readonly BrowserSceneTextures _textures;
    private readonly Dictionary<ActorHandle, ActorEntry> _actors = [];
    private readonly Dictionary<int, MeshEntry> _meshes = [];
    private int _nextActor;
    private int[] _actorMeta = [];
    private float[] _actorStates = [];
    private float[] _actorBones = [];
    private int[] _meshMeta = [];
    private float[] _meshBones = [];
    private float[] _frame = new float[FrameHeader];
    private readonly float[] _portrait = new float[55];
    private readonly Dictionary<int, int[]> _portraitActorsByCount = [];
    private int _actorCount;
    private int _actorBoneCount;
    private int _meshCount;
    private int _meshBoneCount;
    private bool _disposed;
    private int[]? _skyCurrent;
    private int[]? _skyPrevious;
    private float _skyOpacity;
    private float _skyHeight;
    private float _skyBrightness;

    private sealed class ActorEntry(int id, int boneCount)
    {
        public readonly int Id = id;
        public readonly float[] Bones = new float[checked(boneCount * 12)];
        public ExternalActorState State;
        public int OverlayTexture;
        public bool Dirty;
        public bool PoseDirty;
        public bool HasPose;
    }

    private sealed class MeshEntry(int boneCount)
    {
        public readonly float[] Bones = new float[checked(boneCount * 12)];
        public bool Dirty;
    }

    public BrowserExternalSceneRenderer(IClientLog? log = null)
    {
        _textures = new BrowserSceneTextures(PublishTexture, log ?? ConsoleClientLog.Instance);
        CreateScene(_scene);
    }

    public void Configure(ClientSettings settings)
    {
        var document = JsonNode.Parse(ClientSettingsStore.Serialize(settings))!.AsObject();
        document["lightIntensityScale"] = PristonPhotometry.LampReferenceDistance * PristonPhotometry.LampReferenceDistance;
        ConfigureScene(_scene, document.ToJsonString());
    }

    public Task PrepareMaterialsAsync(string directory, IEnumerable<PristonMaterial> materials, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();
        foreach (var request in PristonTextureLoader.TextureRequests(materials))
        {
            tasks.Add(_textures.PrepareAsync(directory, request.Name, request.Opacity, cancellationToken));
        }
        return Task.WhenAll(tasks);
    }

    public Task PrepareTextureAsync(string path) => _textures.PrepareAsync(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileName(path), string.Empty, default);

    public void CreateMesh(int id, ExternalSceneMesh mesh)
    {
        var count = mesh.Positions.Length / 3;
        if (mesh.Positions.Length % 3 != 0 || mesh.Normals.Length != count * 3 || mesh.Colors.Length != count * 4
            || mesh.Uv.Length != count * 2 || mesh.Uv1.Length != count * 2 || mesh.Uv2.Length != count * 2
            || (mesh.BoneCount > 0 && mesh.BoneIndices.Length != count))
        {
            throw new ArgumentException("Inconsistent external mesh channels.", nameof(mesh));
        }
        var vertices = new float[checked(count * VertexStride)];
        for (var vertex = 0; vertex < count; vertex++)
        {
            var at = vertex * VertexStride;
            mesh.Positions.AsSpan(vertex * 3, 3).CopyTo(vertices.AsSpan(at, 3));
            mesh.Normals.AsSpan(vertex * 3, 3).CopyTo(vertices.AsSpan(at + 3, 3));
            mesh.Colors.AsSpan(vertex * 4, 4).CopyTo(vertices.AsSpan(at + 6, 4));
            mesh.Uv.AsSpan(vertex * 2, 2).CopyTo(vertices.AsSpan(at + 10, 2));
            mesh.Uv1.AsSpan(vertex * 2, 2).CopyTo(vertices.AsSpan(at + 12, 2));
            mesh.Uv2.AsSpan(vertex * 2, 2).CopyTo(vertices.AsSpan(at + 14, 2));
            vertices[at + 16] = mesh.BoneCount > 0 ? mesh.BoneIndices[vertex] : 0f;
        }
        var json = JsonSerializer.Serialize(new { vertexStride = VertexStride, vertexCount = count, boneCount = mesh.BoneCount,
            isSky = mesh.IsSky, material = Material(mesh.AssetDirectory, mesh.Material, mesh.IsSky) });
        CreateMeshPacked(_scene, id, json, MemoryMarshal.AsBytes(vertices.AsSpan()), MemoryMarshal.AsBytes(mesh.Indices.AsSpan()));
        _meshes.Add(id, new MeshEntry(mesh.BoneCount));
    }

    private object Material(string directory, PristonMaterial source, bool sky)
    {
        var frames = source.HasAnimatedTexture ? new int[source.AnimationTextureNames.Count] : new int[1];
        for (var index = 0; index < frames.Length; index++)
        {
            frames[index] = source.HasAnimatedTexture ? _textures.Get(directory, source.AnimationTextureNames[index])
                : _textures.Get(directory, source.TextureName, source.OpacityName);
        }
        var second = source.CombinedLayerCount > 0 ? _textures.Get(directory, source.StageTextureName(1)) : 0;
        var (alphaTest, transparent) = PristonMaterialRuntimeFactory.Classify(source, softAlpha: sky);
        var description = PristonMaterialAdapter.WithShape(PristonMaterialAdapter.Describe(source, null!), alphaTest, source.TwoSided);
        var (layer, strength) = second != 0
            ? PristonMaterialAdapter.DescribeLayer(PristonMaterialLayer.Decode(source.StageState(1)), source.StageFormState(1))
            : (PristonLayerMode.None, 1f);
        var (windX, windZ) = PristonMaterialRuntimeFactory.WindAmplitudes(source.SurfaceFlags);
        return new
        {
            textureId = frames.Length > 0 ? frames[0] : 0,
            textureIds = frames,
            texture2Id = second,
            blend = source.BlendType is PristonBlendType.Color or PristonBlendType.Lamp or PristonBlendType.AddColor ? "additive" : transparent ? "alpha" : "opaque",
            blendType = source.BlendType,
            color = new[] { description.BaseColorFactor.X, description.BaseColorFactor.Y, description.BaseColorFactor.Z, description.BaseColorFactor.W },
            alphaCutoff = alphaTest ? PristonMaterialRuntime.DefaultAlphaCutoff : 0f,
            doubleSided = source.TwoSided,
            unlit = sky || description.ShadingModel != PristonShadingModel.Lit,
            opacity = source.IsInvisible ? 0f : Math.Clamp(1f - source.Transparency, 0f, 1f),
            depthWrite = source.Transparency <= PristonMaterialRuntime.SolidTransparencyThreshold && !sky,
            windX,
            windZ,
            roughness = layer == PristonLayerMode.Reflection ? PristonMaterialAdapter.ShineRoughness : description.Roughness,
            metallic = description.Metallic,
            reflectance = description.Reflectance,
            emissive = description.EmissiveStrength,
            translucency = description.Translucency,
            blink = description.Blink,
            layerMode = layer.ToString().ToLowerInvariant(),
            layerStrength = strength,
            uvForm = source.RenderedStageCount > 1 ? source.StageFormState(0) : PristonMaterialRuntimeFactory.FirstTextureFormState(source),
            uvForm2 = source.StageFormState(1),
            frameMask = source.FrameMask,
            shiftFrameSpeed = source.ShiftFrameSpeed,
            animationFrame = source.AnimationFrame
        };
    }

    public void RemoveMesh(int id)
    {
        _meshes.Remove(id);
        RemoveMeshCore(_scene, id);
    }

    public void CreateStage(int id, int[] meshIds) => CreateStageCore(_scene, id, meshIds);
    public void RemoveStage(int id) => RemoveStageCore(_scene, id);

    public void UpdateMeshPose(int id, ReadOnlySpan<Vector4> boneRows)
    {
        if (_meshes.TryGetValue(id, out var entry))
        {
            CopyBones(boneRows, entry.Bones);
            entry.Dirty = true;
        }
    }

    public void SetSkyLayers(int[] currentMeshes, int[] previousMeshes, float previousOpacity, float height, float brightness)
    {
        // The engine's sky cache owns immutable profile arrays; moving the camera only changes scalar parameters.
        if (!ReferenceEquals(_skyCurrent, currentMeshes) || !ReferenceEquals(_skyPrevious, previousMeshes))
        {
            var timing = ProfileTiming;
            var started = timing is null ? 0 : Stopwatch.GetTimestamp();
            SetSkyLayersCore(_scene, currentMeshes, previousMeshes, previousOpacity, height, brightness);
            if (timing is not null)
            {
                timing.RecordSceneBridge(Stopwatch.GetTimestamp() - started);
            }
            _skyCurrent = currentMeshes;
            _skyPrevious = previousMeshes;
        }
        else if (_skyOpacity != previousOpacity || _skyHeight != height || _skyBrightness != brightness)
        {
            var timing = ProfileTiming;
            var started = timing is null ? 0 : Stopwatch.GetTimestamp();
            SetSkyParameters(_scene, previousOpacity, height, brightness);
            if (timing is not null)
            {
                timing.RecordSceneBridge(Stopwatch.GetTimestamp() - started);
            }
        }
        _skyOpacity = previousOpacity;
        _skyHeight = height;
        _skyBrightness = brightness;
    }

    public void CreateActor(ActorHandle handle, int[] meshIds, int boneCount)
    {
        var entry = new ActorEntry(++_nextActor, boneCount);
        _actors.Add(handle, entry);
        CreateActorCore(_scene, entry.Id, meshIds, boneCount);
    }

    public void RemoveActor(ActorHandle handle)
    {
        if (_actors.Remove(handle, out var entry))
        {
            RemoveActorCore(_scene, entry.Id);
        }
    }

    public void UpdateActor(ActorHandle handle, in ExternalActorState state, ReadOnlySpan<Vector4> boneRows)
    {
        if (!_actors.TryGetValue(handle, out var entry))
        {
            return;
        }
        if (entry.State != state || !entry.HasPose)
        {
            if (!string.Equals(entry.State.Overlay, state.Overlay, StringComparison.Ordinal))
            {
                entry.OverlayTexture = string.IsNullOrWhiteSpace(state.Overlay) ? 0
                    : _textures.Get(Path.GetDirectoryName(state.Overlay) ?? string.Empty, Path.GetFileName(state.Overlay));
            }
            entry.Dirty = true;
        }
        if (!boneRows.IsEmpty && (!entry.HasPose || entry.State.PoseRevision != state.PoseRevision))
        {
            CopyBones(boneRows, entry.Bones);
            entry.PoseDirty = true;
            entry.Dirty = true;
            entry.HasPose = true;
        }
        entry.State = state;
    }

    public void Draw(in ExternalSceneFrame frame)
    {
        PrepareUpdates();
        var count = FrameHeader + frame.PointLights.Count * 8;
        EnsureCapacity(ref _frame, count);
        PackFrame(frame, _frame);
        var timing = ProfileTiming;
        var started = timing is null ? 0 : Stopwatch.GetTimestamp();
        DrawFramePacked(_scene, MemoryMarshal.AsBytes(_frame.AsSpan(0, count)),
            MemoryMarshal.AsBytes(_actorMeta.AsSpan(0, _actorCount * 4)), MemoryMarshal.AsBytes(_actorStates.AsSpan(0, _actorCount * ActorStride)),
            MemoryMarshal.AsBytes(_actorBones.AsSpan(0, _actorBoneCount)), MemoryMarshal.AsBytes(_meshMeta.AsSpan(0, _meshCount * 3)),
            MemoryMarshal.AsBytes(_meshBones.AsSpan(0, _meshBoneCount)));
        if (timing is not null)
        {
            timing.RecordSceneBridge(Stopwatch.GetTimestamp() - started);
        }
        CompleteUpdates();
    }

    public void DrawPortrait(ReadOnlySpan<ActorHandle> handles, Matrix view, Matrix projection, Vector3 cameraPosition, in PristonPortraitLighting lighting)
    {
        PrepareUpdates();
        var timing = ProfileTiming;
        var started = timing is null ? 0 : Stopwatch.GetTimestamp();
        FlushScenePacked(_scene, MemoryMarshal.AsBytes(_actorMeta.AsSpan(0, _actorCount * 4)), MemoryMarshal.AsBytes(_actorStates.AsSpan(0, _actorCount * ActorStride)),
            MemoryMarshal.AsBytes(_actorBones.AsSpan(0, _actorBoneCount)), MemoryMarshal.AsBytes(_meshMeta.AsSpan(0, _meshCount * 3)),
            MemoryMarshal.AsBytes(_meshBones.AsSpan(0, _meshBoneCount)));
        if (timing is not null)
        {
            timing.RecordSceneBridge(Stopwatch.GetTimestamp() - started);
        }
        CompleteUpdates();
        var count = 0;
        foreach (var handle in handles)
        {
            if (_actors.ContainsKey(handle))
            {
                count++;
            }
        }
        if (!_portraitActorsByCount.TryGetValue(count, out var portraitActors))
        {
            portraitActors = new int[count];
            _portraitActorsByCount.Add(count, portraitActors);
        }
        count = 0;
        foreach (var handle in handles)
        {
            if (_actors.TryGetValue(handle, out var entry))
            {
                portraitActors[count++] = entry.Id;
            }
        }
        PackMatrix(_portrait, 0, view);
        PackMatrix(_portrait, 16, projection);
        PackVector(_portrait, 32, cameraPosition);
        PackVector(_portrait, 35, lighting.KeyDirection);
        PackVector(_portrait, 38, lighting.KeyColor);
        PackVector(_portrait, 41, lighting.SkyFill);
        PackVector(_portrait, 44, lighting.GroundFill);
        PackVector(_portrait, 47, lighting.RimPosition);
        PackVector(_portrait, 50, lighting.RimColor);
        _portrait[53] = lighting.RimRange;
        _portrait[54] = lighting.Exposure;
        started = timing is null ? 0 : Stopwatch.GetTimestamp();
        DrawPortraitCore(_scene, portraitActors, MemoryMarshal.AsBytes(_portrait.AsSpan()));
        if (timing is not null)
        {
            timing.RecordSceneBridge(Stopwatch.GetTimestamp() - started);
        }
    }

    private void PublishTexture(int id, int width, int height, byte[] rgba) => CreateSceneTexture(_scene, id, width, height, rgba.AsSpan());

    private static void CopyBones(ReadOnlySpan<Vector4> rows, float[] destination)
    {
        if (rows.Length * 4 != destination.Length)
        {
            throw new ArgumentException("The bone palette does not match its scene resource.", nameof(rows));
        }
        MemoryMarshal.Cast<Vector4, float>(rows).CopyTo(destination);
    }

    private static void EnsureCapacity<T>(ref T[] array, int count)
    {
        if (array.Length < count)
        {
            Array.Resize(ref array, Math.Max(count, Math.Max(16, array.Length * 2)));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        DisposeScene(_scene);
        _textures.Clear();
        _actors.Clear();
        _meshes.Clear();
        _portraitActorsByCount.Clear();
    }
}
