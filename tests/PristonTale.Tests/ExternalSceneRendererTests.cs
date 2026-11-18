using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class ExternalSceneRendererTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("external scene facade exports geometry, authoritative poses and shared resource lifetimes without a GPU", LifecycleAsync);
        yield return ("external scene facade factory forwards quality and warm frame updates allocate nothing", ConfigurationAsync);
        yield return ("external scene facade exports authored sky layers and weather crossfades without a GPU", SkyAsync);
        yield return ("external scene facade preserves native day, moon and underground shadow eligibility", ShadowEligibilityAsync);
        yield return ("actor floor lighting reaches external states and native batches across movement and preference changes", FloorLightingAsync);
    }

    private static PristonMaterial Material() => new(true, 1, 0, 0, false, Vector3.One, 0, 0,
        PristonMaterialSurfaceFlags.None, "fixture.bmp", "", [], [], 0, 0, 0);

    private static PristonStage Stage() => new("fixture.smd", [Material()],
        [new(Vector3.Zero, Color.White), new(Vector3.UnitZ, Color.White), new(Vector3.UnitX, Color.White)],
        [new(0, 1, 2, 0, -1)], [], [], new BoundingBox(Vector3.Zero, Vector3.One));

    private static PristonActor Actor()
    {
        var material = Material();
        var meshObject = new PristonModelObject("root", "", false, Matrix.Identity, Vector3.Zero,
            [new(Vector3.Zero, Vector3.Forward), new(Vector3.UnitY, Vector3.Forward), new(Vector3.UnitX, Vector3.Forward)],
            [new(0, 1, 2, 0, -1)], [], [], [], [new(0, Vector3.Zero), new(1600, new Vector3(4, 0, 0))], [], [], [], [], []);
        var model = new PristonModel("actor.smb", [material], [meshObject], [], new BoundingBox(Vector3.Zero, Vector3.One));
        var stand = new PristonMotionInfo((uint)MotionCode.Stand, 0, 10, [], 0, 0, 0, 1, 0, 0);
        var motions = new PristonMotionInfo[PristonFormatConstants.CharacterMotionBase + 1];
        Array.Fill(motions, stand);
        var info = new PristonModelInfo("actor.inx", "actor.smb", "actor.smb", "", new(0, []), new(0, []), new(0, []),
            motions, (uint)motions.Length, "", "", "", [], 0);
        return new PristonActor(info, model, model, []);
    }

    private static async Task LifecycleAsync()
    {
        var sink = new RecordingRenderer();
        using var renderer = new PristonRenderer(null!, null!, externalSceneRenderer: sink) { ActorFloorLight = false };
        var actor = Actor();
        await renderer.PrepareActorResourcesAsync(actor);
        await renderer.PrepareStageTexturesAsync(Stage(), [actor.Mesh]);
        Assert.True(sink.Preparations >= 3, "external texture preparation was not delegated");
        var stage = renderer.AddStage(Stage(), [new PristonSceneryInstance(actor.Mesh, Matrix.CreateTranslation(0, 0, -10))]);
        Assert.True(sink.Meshes.Count == 2 && sink.Stages.Count == 1, "terrain and animated scenery were not registered");
        var stageMesh = sink.Meshes.Values.First(mesh => mesh.BoneCount == 0);
        Assert.True(stageMesh.Indices.Length == 3 && stageMesh.Positions.Length == 9 && stageMesh.Uv.Length == 6, "terrain channels changed");
        Assert.Near(1f, stageMesh.Normals[1], 0.001f);
        var first = renderer.AddActor(new(actor, Matrix.CreateTranslation(10, 20, 30), MotionCode.Stand));
        var second = renderer.AddActor(new(actor, Matrix.CreateTranslation(50, 20, 30), MotionCode.Stand));
        Assert.True(sink.Meshes.Count == 3 && sink.Actors.Count == 2, "identical actor geometry was uploaded more than once");
        var camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
        renderer.Draw(camera);
        renderer.ComposeScene();
        Assert.True(sink.Compositions == 1, "world composition did not preserve the explicit effects-to-HUD boundary");
        Assert.Near(10f, sink.Poses[first][0].W, 0.001f);
        Assert.Near(50f, sink.Poses[second][0].W, 0.001f);
        Assert.True(sink.MeshPoseUpdates > 0, "animated scenery did not publish its pose");
        renderer.Update(new GameTime(TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1)));
        renderer.Draw(camera);
        Assert.True(sink.Poses[first][0].W > 10f, "the authoritative C# motion did not advance");
        renderer.SetActorHidden(second, true);
        renderer.SetActorColorAdd(first, new Vector3(0.2f, 0.3f, 0.4f));
        renderer.Draw(camera);
        Assert.True(!sink.States[second].Visible && sink.States[first].ColorAdd.X == 0.2f, "actor visibility or highlight was lost");
        Assert.True(renderer.TryGetActorBounds([first], out var bounds) && bounds.Min.X >= 10f, "world-space selection bounds were lost");
        Assert.True(renderer.TryGetActorPortraitAnchor(first, "root", out var anchor) && anchor.HasBone, "portrait anchor was tied to a native buffer");
        Assert.True(renderer.TryGetActorReach(first, out var reach) && reach > 0, "item reach was tied to a native buffer");
        renderer.DrawActorPortrait([first], Matrix.Identity, Matrix.Identity, Vector3.Zero,
            new PristonPortraitLighting(Vector3.Down, Vector3.One, Vector3.One, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 1));
        Assert.True(sink.Portraits == 1, "live portraits did not reach the scene backend");
        renderer.RemoveActor(first);
        Assert.True(sink.Meshes.Count == 3, "one actor released a mesh still used by another");
        renderer.RemoveActor(second);
        Assert.True(sink.Meshes.Count == 2 && sink.Actors.Count == 0, "last actor did not release its mesh");
        renderer.RemoveStage(stage);
        Assert.True(sink.Meshes.Count == 0 && sink.Stages.Count == 0, "stage geometry survived stage removal");
        renderer.Dispose();
        Assert.True(sink.Disposed, "external backend was not disposed");
    }

    private static Task ConfigurationAsync()
    {
        var previous = PristonRenderer.ExternalSceneRendererFactory;
        var sink = new RecordingRenderer();
        try
        {
            PristonRenderer.ExternalSceneRendererFactory = () => sink;
            var presentation = new PresentationSettings();
            using var renderer = new PristonRenderer(null!, null!, presentation: presentation);
            presentation.ApplyRendering(new ClientSettings { RenderScale = 0.75f, ShadowsEnabled = false });
            Assert.True(sink.Settings?.RenderScale == 0.75f && !renderer.ShadowsEnabled, "quality settings did not reach the external renderer");
            renderer.AddActor(new(Actor(), Matrix.Identity, MotionCode.Stand));
            var camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
            for (var frame = 0; frame < 3; frame++)
            {
                renderer.Draw(camera);
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < 100; frame++)
            {
                renderer.Draw(camera);
            }
            Assert.True(GC.GetAllocatedBytesForCurrentThread() == before, "external scene updates allocated after warmup");
        }
        finally
        {
            PristonRenderer.ExternalSceneRendererFactory = previous;
        }
        Assert.True(sink.Disposed && sink.Meshes.Count == 0, "factory backend leaked resident resources");
        return Task.CompletedTask;
    }

    private static Task ShadowEligibilityAsync()
    {
        var sink = new RecordingRenderer();
        using var renderer = new PristonRenderer(null!, null!, externalSceneRenderer: sink) { DayLengthSeconds = 0 };
        var camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
        renderer.SetTimeOfDay(12f);
        renderer.Draw(camera);
        Assert.True(sink.LastFrame.ShadowsEnabled && sink.LastFrame.SunColor != Vector3.Zero,
            "daylight shadows were disabled despite an active directional light");
        renderer.SetTimeOfDay(23.5f);
        renderer.Draw(camera);
        Assert.True(sink.LastFrame.ShadowsEnabled && sink.LastFrame.SunColor != Vector3.Zero
            && Vector3.Distance(sink.LastFrame.SunDirection, -PristonDayNightCycle.TowardMoon) < 0.0001f,
            "native moon shadows were removed or used the below-horizon sun direction");
        renderer.Underground = true;
        renderer.Draw(camera);
        Assert.True(!sink.LastFrame.ShadowsEnabled && sink.LastFrame.SunColor == Vector3.Zero && renderer.ShadowsEnabled,
            "underground frames requested a directional shadow map or changed the user preference");
        renderer.Underground = false;
        renderer.ShadowsEnabled = false;
        renderer.Draw(camera);
        Assert.True(!sink.LastFrame.ShadowsEnabled, "the user shadow preference was ignored at night");
        renderer.SetTimeOfDay(12f);
        renderer.Draw(camera);
        Assert.True(!sink.LastFrame.ShadowsEnabled, "the user shadow preference was ignored during the day");
        return Task.CompletedTask;
    }

    private static Task FloorLightingAsync()
    {
        var sink = new RecordingRenderer();
        using var renderer = new PristonRenderer(null!, null!, externalSceneRenderer: sink) { ActorFloorLight = true };
        var colour = new Color(128, 128, 128);
        var floor = new PristonStage("floor.smd", [Material()],
            [new(new Vector3(-1000, 0, -1000), colour), new(new Vector3(1000, 0, -1000), colour),
                new(new Vector3(1000, 0, 1000), colour), new(new Vector3(-1000, 0, 1000), colour)],
            [new(0, 2, 1, 0, -1), new(0, 3, 2, 0, -1)], [], [],
            new BoundingBox(new Vector3(-1000, 0, -1000), new Vector3(1000, 0, 1000)));
        renderer.AddStage(floor, Array.Empty<PristonSceneryInstance>());
        var actor = renderer.AddActor(new(Actor(), Matrix.CreateTranslation(0, 20, 0), MotionCode.Stand));
        var camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
        renderer.Draw(camera);
        var expected = PristonColorSpace.SrgbToLinear(128f / 255f);
        Assert.True(expected < 0.3f && sink.States[actor].Bounds.Min.Y >= 20f,
            "fixture did not place a real external actor over a shaded floor");
        Assert.Near(expected, sink.States[actor].FloorLight, 0.00001f);

        // Exercise the native batch consumer of the same production palette update without allocating a GPU resource.
        var actors = (IList)typeof(PristonRenderer).GetField("_actors", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(renderer)!;
        var runtime = actors[actor.Slot]!;
        var batches = (List<RenderBatch>)runtime.GetType().GetProperty("Batches")!.GetValue(runtime)!;
        var batch = new RenderBatch([], [], null!, null!, IsSkinned: true, IsActor: true);
        batches.Add(batch);
        try
        {
            renderer.SetActorWorld(actor, Matrix.CreateTranslation(100, 20, 100));
            renderer.Draw(camera);
            Assert.Near(expected, batch.FloorLight, 0.00001f);
            Assert.Near(batch.FloorLight, sink.States[actor].FloorLight, 0.00001f);
            renderer.ActorFloorLight = false;
            renderer.SetActorWorld(actor, Matrix.CreateTranslation(200, 20, 100));
            renderer.Draw(camera);
            Assert.Near(1f, batch.FloorLight, 0.00001f);
            Assert.Near(1f, sink.States[actor].FloorLight, 0.00001f);
            renderer.ActorFloorLight = true;
            renderer.SetActorWorld(actor, Matrix.CreateTranslation(5000, 20, 100));
            renderer.Draw(camera);
            Assert.Near(1f, batch.FloorLight, 0.00001f);
            Assert.Near(1f, sink.States[actor].FloorLight, 0.00001f);
        }
        finally
        {
            batches.Remove(batch);
        }
        return Task.CompletedTask;
    }

    private static Task SkyAsync()
    {
        if (Assets.AssetFiles.FindAssetsRoot() is not { } root || !File.Exists(Path.Combine(root, PristonSkyManifest.AssetPath)))
        {
            return Task.CompletedTask;
        }
        var previous = PristonRenderer.ExternalSceneRendererFactory;
        var sink = new RecordingRenderer();
        try
        {
            PristonRenderer.ExternalSceneRendererFactory = () => sink;
            using var sky = PristonSkyLibrary.LoadBootstrap(null!, root, Engine3D.Diagnostics.NullClientLog.Instance, preloadAll: false);
            var meshes = sky.GetExternalMeshes(PristonSkyProfiles.Forest.Night);
            Assert.True(meshes.Length > 0 && meshes.All(mesh => mesh.IsSky && mesh.Indices.Length > 0 && mesh.Positions.Length > 0),
                "authored night sky was replaced by a flat background");
            using var renderer = new PristonRenderer(null!, null!, Engine3D.Diagnostics.NullClientLog.Instance) { Sky = sky, DayLengthSeconds = 0 };
            var camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
            renderer.SetTimeOfDay(23.5f);
            var step = new GameTime(TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1));
            renderer.Update(step);
            renderer.Draw(camera);
            Assert.True(sink.SkyCurrent > 0, "the night sky layers did not reach the scene backend");
            renderer.SetTimeOfDay(12f);
            renderer.Update(step);
            renderer.Draw(camera);
            Assert.True(sink.SkyCurrent > 0 && sink.SkyPrevious > 0 && sink.SkyPreviousOpacity > 0,
                "day/night sky transition lost its previous authored layers");
            renderer.Weather = new Engine3D.Rendering.Weather.PristonWeather(1f, 0f, 0f, Vector2.Zero);
            renderer.Update(step);
            renderer.Draw(camera);
            Assert.True(sink.SkyCurrent > 0, "overcast weather lost its authored dome");
        }
        finally
        {
            PristonRenderer.ExternalSceneRendererFactory = previous;
        }
        return Task.CompletedTask;
    }

    private sealed class RecordingRenderer : IExternalSceneRenderer
    {
        public Dictionary<int, ExternalSceneMesh> Meshes { get; } = new();
        public HashSet<int> Stages { get; } = new();
        public HashSet<ActorHandle> Actors { get; } = new();
        public Dictionary<ActorHandle, ExternalActorState> States { get; } = new();
        public Dictionary<ActorHandle, Vector4[]> Poses { get; } = new();
        public ClientSettings? Settings { get; private set; }
        public int Preparations { get; private set; }
        public int MeshPoseUpdates { get; private set; }
        public int Portraits { get; private set; }
        public int Compositions { get; private set; }
        public bool Disposed { get; private set; }
        public int SkyCurrent { get; private set; }
        public int SkyPrevious { get; private set; }
        public float SkyPreviousOpacity { get; private set; }
        public ExternalSceneFrame LastFrame { get; private set; }
        public void Configure(ClientSettings settings) => Settings = settings;
        public Task PrepareMaterialsAsync(string directory, IEnumerable<PristonMaterial> materials, CancellationToken cancellationToken = default)
        {
            Preparations++;
            return Task.CompletedTask;
        }
        public Task PrepareTextureAsync(string path) => Task.CompletedTask;
        public void CreateMesh(int id, ExternalSceneMesh mesh) => Meshes.Add(id, mesh);
        public void RemoveMesh(int id) => Meshes.Remove(id);
        public void CreateStage(int id, int[] meshIds) => Stages.Add(id);
        public void RemoveStage(int id) => Stages.Remove(id);
        public void UpdateMeshPose(int id, ReadOnlySpan<Vector4> boneRows) => MeshPoseUpdates++;
        public void SetSkyLayers(int[] currentMeshes, int[] previousMeshes, float previousOpacity, float height, float brightness)
        {
            SkyCurrent = currentMeshes.Length;
            SkyPrevious = previousMeshes.Length;
            SkyPreviousOpacity = previousOpacity;
        }
        public void CreateActor(ActorHandle handle, int[] meshIds, int boneCount) => Actors.Add(handle);
        public void RemoveActor(ActorHandle handle) => Actors.Remove(handle);
        public void UpdateActor(ActorHandle handle, in ExternalActorState state, ReadOnlySpan<Vector4> boneRows)
        {
            States[handle] = state;
            if (!Poses.TryGetValue(handle, out var pose) || pose.Length != boneRows.Length)
            {
                pose = new Vector4[boneRows.Length];
                Poses[handle] = pose;
            }
            boneRows.CopyTo(pose);
        }
        public void Draw(in ExternalSceneFrame frame) => LastFrame = frame;
        public void Compose() => Compositions++;
        public void DrawPortrait(ReadOnlySpan<ActorHandle> handles, Matrix view, Matrix projection, Vector3 cameraPosition, in PristonPortraitLighting lighting) => Portraits++;
        public void Dispose() => Disposed = true;
    }
}
