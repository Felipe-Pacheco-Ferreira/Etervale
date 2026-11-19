using Microsoft.Xna.Framework;
using PristonTale.Contracts.Motion;
using PristonTale.Engine3D;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class PoseMemoizationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("exact pose memoization preserves all matrices across shared samples, seeks and rotation limits", ExactSamplesAsync);
        yield return ("exact pose memoization owns raw buffers before seating and leaves held LOD poses independent", BufferOwnershipAsync);
        yield return ("exact pose memoization expires per update and releases all retained skeletons without steady allocation", LifetimeAsync);
        yield return ("exact pose memoization preserves renderer attachments, immediate queries, held poses and actor reuse", RendererAsync);
    }

    private static Task ExactSamplesAsync()
    {
        var model = AnimatedRig();
        var cache = new PristonPoseMemoization();
        var first = new PristonModelAnimator(model);
        var second = new PristonModelAnimator(model);
        var baseline = new PristonModelAnimator(model);
        first.SetMemoization(cache);
        second.SetMemoization(cache);
        var previousTiming = PristonPoseMemoization.ProfileTiming;
        var timing = new FrameTiming();
        try
        {
            PristonPoseMemoization.ProfileTiming = timing;
            var samples = new (float Frame, float End)[]
            {
                (0, 200), (25, 200), (99, 200), (100, 200), (175, 200), (200, 200),
                (75, 200), (150, 100), (10, 200), (MathF.BitIncrement(10f), 200),
                (75, 50), (75, 200), (75, float.PositiveInfinity), (75, 200)
            };
            foreach (var (frame, end) in samples)
            {
                var expected = baseline.Evaluate(frame, end);
                Equal(expected, first.Evaluate(frame, end));
                Equal(expected, second.Evaluate(frame, end));
            }
            Assert.True(timing.AnimationPoses.CacheMisses == samples.Length && timing.AnimationPoses.CacheHits == samples.Length,
                "sample frame or motion end was rounded, or exact shared samples were reevaluated");

            var distinctModel = model with { Path = model.Path };
            var distinct = new PristonModelAnimator(distinctModel);
            distinct.SetMemoization(cache);
            Equal(baseline.Evaluate(75, 200), distinct.Evaluate(75, 200));
            Assert.True(cache.Count == 2 && timing.AnimationPoses.CacheMisses == samples.Length + 1,
                "equal model records were treated as the same skeleton instance");
            var before = timing.AnimationPoses;
            first.Evaluate(float.PositiveInfinity);
            second.Evaluate(float.PositiveInfinity);
            Assert.True(timing.AnimationPoses.CacheHits == before.CacheHits && timing.AnimationPoses.CacheMisses == before.CacheMisses + 2,
                "a non-finite sample was reused");
            distinct.SetMemoization(null!);
        }
        finally
        {
            PristonPoseMemoization.ProfileTiming = previousTiming;
            first.SetMemoization(null!);
            second.SetMemoization(null!);
        }
        Assert.True(cache.Count == 0, "animators retained a skeleton after detaching");
        return Task.CompletedTask;
    }

    private static Task BufferOwnershipAsync()
    {
        var model = Biped();
        var seat = ActorSeat.TryCreate(model) ?? throw new InvalidOperationException("fixture is not a biped");
        var cache = new PristonPoseMemoization();
        var seated = new PristonModelAnimator(model);
        var standing = new PristonModelAnimator(model);
        seated.SetMemoization(cache);
        standing.SetMemoization(cache);
        var baseline = new PristonModelAnimator(model);
        var expectedRaw = (Matrix[])baseline.Evaluate(80, 200).Clone();
        var expectedSeat = (Matrix[])expectedRaw.Clone();
        seat.Apply(expectedSeat);
        var seatedBuffer = seated.Evaluate(80, 200);
        seat.Apply(seatedBuffer);
        Equal(expectedSeat, seatedBuffer);
        var standingBuffer = standing.Evaluate(80, 200);
        Equal(expectedRaw, standingBuffer);
        Assert.True(!ReferenceEquals(seatedBuffer, standingBuffer) && !expectedRaw.AsSpan().SequenceEqual(expectedSeat),
            "seated and standing actors share their output buffer, or the seat fixture did not change the pose");

        var motion = new PristonMotionInfo((uint)MotionCode.Stand, 0, 20, [], 0, 0, 0, 1, 0, 0);
        var held = new ActorPose();
        held.Take(seatedBuffer, 80, motion);
        standingBuffer[0] = Matrix.CreateTranslation(999, 999, 999);
        Equal(expectedRaw, standing.Evaluate(80, 200));
        for (var index = 0; index < 32; index++)
        {
            cache.Invalidate();
            var frame = index * 3f;
            Equal(baseline.Evaluate(frame, 200), standing.Evaluate(frame, 200));
            var raw = seated.Evaluate(frame, 200);
            seat.Apply(raw);
            Equal(expectedSeat, held.Transforms);
        }
        seated.SetMemoization(null!);
        standing.SetMemoization(null!);
        return Task.CompletedTask;
    }

    private static Task LifetimeAsync()
    {
        var model = AnimatedRig();
        var cache = new PristonPoseMemoization();
        var first = new PristonModelAnimator(model);
        var second = new PristonModelAnimator(model);
        first.SetMemoization(cache);
        second.SetMemoization(cache);
        first.SetMemoization(cache);
        var previousTiming = PristonPoseMemoization.ProfileTiming;
        var timing = new FrameTiming();
        try
        {
            PristonPoseMemoization.ProfileTiming = timing;
            for (var frame = 0; frame < 100; frame++)
            {
                cache.Invalidate();
                first.Evaluate(frame, 200);
                second.Evaluate(frame, 200);
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < 2000; frame++)
            {
                cache.Invalidate();
                first.Evaluate(frame % 200, 200);
                second.Evaluate(frame % 200, 200);
                timing.RecordActorPoses(2, 0);
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(bytes == 0, "pose memoization or its profiling counters allocate after warmup");
            Assert.True(timing.AnimationPoses.CacheMisses == 2100 && timing.AnimationPoses.CacheHits == 2100
                && timing.AnimationPoses.RootPoses == 4000 && timing.AnimationPoses.RootReuses == 0,
                "invalidation reused a previous update or counters lost real samples");
            first.SetMemoization(null!);
            Assert.True(cache.Count == 1, "one actor detached a skeleton still retained by another");
            second.SetMemoization(null!);
            Assert.True(cache.Count == 0, "the final actor retained its cached skeleton");
            cache.Clear();
            first.SetMemoization(cache);
            first.Evaluate(20, 200);
            Assert.True(cache.Count == 1 && timing.AnimationPoses.CacheMisses == 2101, "retaining a released skeleton reused stale data");
            Assert.True(new FrameTiming().AnimationPoses == default, "reset frame diagnostics retained pose counters");
        }
        finally
        {
            first.SetMemoization(null!);
            second.SetMemoization(null!);
            PristonPoseMemoization.ProfileTiming = previousTiming;
        }
        return Task.CompletedTask;
    }

    private static Task RendererAsync()
    {
        var actor = Actor();
        var baselineSink = new PoseSink();
        var cachedSink = new PoseSink();
        using var baseline = Renderer(baselineSink, memoized: false);
        using var cached = Renderer(cachedSink, memoized: true);
        Assert.True(!baseline.ExactActorPoseMemoization && cached.ExactActorPoseMemoization, "pose memoization changed the renderer default");
        var originals = AddActors(baseline, actor);
        var copies = AddActors(cached, actor);
        var camera = new PristonCamera(new Vector3(0, 80, 500), new Vector3(0, 40, 0));
        var previousTiming = PristonPoseMemoization.ProfileTiming;
        var timing = new FrameTiming();
        try
        {
            for (var frame = 0; frame < 40; frame++)
            {
                if (frame == 8 || frame == 13)
                {
                    baseline.SetActorSeated(originals[0], frame == 8);
                    cached.SetActorSeated(copies[0], frame == 8);
                }
                if (frame == 17)
                {
                    baseline.SeekActorMotion(originals[1], 2);
                    cached.SeekActorMotion(copies[1], 2);
                }
                if (frame == 21)
                {
                    baseline.SetActorMotion(originals[1], MotionCode.Walk);
                    cached.SetActorMotion(copies[1], MotionCode.Walk);
                }
                var time = new GameTime(TimeSpan.FromSeconds((frame + 1) / 60d), TimeSpan.FromSeconds(1 / 60d));
                PristonPoseMemoization.ProfileTiming = null!;
                baseline.Update(time);
                PristonPoseMemoization.ProfileTiming = timing;
                cached.Update(time);
                baseline.Draw(camera);
                cached.Draw(camera);
                for (var index = 0; index < originals.Length; index++)
                {
                    Assert.True(baselineSink.Poses[originals[index]].AsSpan().SequenceEqual(cachedSink.Poses[copies[index]]),
                        "cached renderer changed a held, seated or attached palette");
                    Assert.True(baseline.TryGetActorBoneWorld(originals[index], "Bip01 Pelvis", out var expected), "baseline bone was not queryable");
                    Assert.True(cached.TryGetActorBoneWorld(copies[index], "Bip01 Pelvis", out var actual), "cached bone was not queryable");
                    Equal([expected], [actual]);
                }
                Assert.True(baseline.LastAnimationCounts == cached.LastAnimationCounts, "memoization changed the animation LOD policy");
            }
            Assert.True(timing.AnimationPoses.CacheHits > 0 && timing.AnimationPoses.CacheMisses > 0
                && timing.AnimationPoses.RootReuses > 0, "renderer fixture did not exercise hits, misses and held LOD poses");
            cached.RemoveActor(copies[0]);
            var replacement = cached.AddActor(new(actor, Matrix.Identity, MotionCode.Stand));
            Assert.True(replacement.Slot == copies[0].Slot && replacement.Generation > copies[0].Generation, "actor slot was not reused");
            cached.ClearActors();
            Assert.True(cachedSink.Poses.Count == 0, "clearing actors retained external actor poses");
            var cache = (PristonPoseMemoization)typeof(PristonRenderer).GetField("_poseMemoization",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cached)!;
            Assert.True(cache.Count == 0, "clearing the renderer retained skeleton cache storage");
        }
        finally
        {
            PristonPoseMemoization.ProfileTiming = previousTiming;
        }
        return Task.CompletedTask;
    }

    private static PristonRenderer Renderer(PoseSink sink, bool memoized) =>
        new(null!, null!, externalSceneRenderer: sink) { ActorFloorLight = false, ExactActorPoseMemoization = memoized, DayLengthSeconds = 0 };

    private static ActorHandle[] AddActors(PristonRenderer renderer, PristonActor actor)
    {
        var first = renderer.AddActor(new(actor, Matrix.Identity, MotionCode.Stand));
        var second = renderer.AddActor(new(actor, Matrix.CreateTranslation(30, 0, 0), MotionCode.Stand));
        var distant = renderer.AddActor(new(actor, Matrix.CreateTranslation(0, 0, -1200), MotionCode.Stand));
        var attached = renderer.AddActor(new(actor, Matrix.Identity, MotionCode.Stand));
        renderer.AttachActor(attached, first, "Bip01 L Foot");
        renderer.SetActorAttachOffset(attached, Matrix.CreateRotationY(.2f), 3);
        return [first, second, distant, attached];
    }

    private static void Equal(ReadOnlySpan<Matrix> expected, ReadOnlySpan<Matrix> actual) =>
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.AsBytes(expected).SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(actual)),
            "pose matrix bits differ from uncached evaluation");

    internal static PristonModel AnimatedRig()
    {
        var rotations = new[] { Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.Up, .8f), Quaternion.CreateFromAxisAngle(Vector3.Up, 1.7f) };
        var animated = new PristonModelObject("animated", "", false, Matrix.Identity, Vector3.Zero, [], [], [],
            [new(0, rotations[0]), new(100, rotations[1]), new(200, rotations[2])], rotations.Select(Matrix.CreateFromQuaternion).ToArray(),
            [new(0, Vector3.Zero), new(100, new Vector3(10, 20, 30)), new(200, new Vector3(20, 40, 60))],
            [new(0, Vector3.One), new(100, new Vector3(2, 3, 4)), new(200, new Vector3(3, 5, 7))], [], [], [], []);
        var child = new PristonModelObject("child", "animated", false, Matrix.CreateTranslation(4, 5, 6), Vector3.Zero, [], [], [], [], [], [], [], [], [], [], []);
        return new("memoization.smb", [], [child, animated], [], default);
    }

    private static PristonModel Biped()
    {
        PristonModelObject Bone(string name, string parent, Vector3 at) =>
            new(name, parent, false, Matrix.CreateTranslation(at), Vector3.Zero, [], [], [], [], [], [], [], [], [], [], []);
        var root = new PristonModelObject("Bip01 Pelvis", "", false, Matrix.CreateTranslation(0, 40, 0), Vector3.Zero,
            [], [], [], [], [], [new(0, new Vector3(0, 40, 0)), new(3200, new Vector3(10, 45, 5))], [], [], [], [], []);
        var bones = new List<PristonModelObject> { root };
        foreach (var (side, x) in new[] { ("L", 5f), ("R", -5f) })
        {
            bones.Add(Bone($"Bip01 {side} Thigh", "Bip01 Pelvis", new Vector3(x, 38, 0)));
            bones.Add(Bone($"Bip01 {side} Calf", $"Bip01 {side} Thigh", new Vector3(x, 20, 0)));
            bones.Add(Bone($"Bip01 {side} Foot", $"Bip01 {side} Calf", new Vector3(x, 2, 0)));
            bones.Add(Bone($"Bip01 {side} Toe0", $"Bip01 {side} Foot", new Vector3(x, 0, 6)));
        }
        return new("biped.smb", [], bones, [], default);
    }

    private static PristonActor Actor()
    {
        var material = new PristonMaterial(true, 1, 0, 0, false, Vector3.One, 0, 0, PristonMaterialSurfaceFlags.None, "fixture.bmp", "", [], [], 0, 0, 0);
        var model = Biped();
        var meshObject = new PristonModelObject("Bip01 Pelvis", "", true, Matrix.Identity, Vector3.Zero,
            [new(Vector3.Zero, Vector3.Forward), new(Vector3.UnitY, Vector3.Forward), new(Vector3.UnitX, Vector3.Forward)],
            [new(0, 1, 2, 0, -1)], [], [], [], [], [], ["Bip01 Pelvis", "Bip01 L Foot", "Bip01 R Foot"], [], [], []);
        var mesh = new PristonModel("fixture.smd", [material], [meshObject], [], new(Vector3.Zero, Vector3.One));
        var stand = new PristonMotionInfo((uint)MotionCode.Stand, 0, 10, [], 0, 0, 0, 1, 0, 0);
        var walk = new PristonMotionInfo((uint)MotionCode.Walk, 1, 12, [], 0, 0, 0, 1, 0, 0);
        var motions = Enumerable.Repeat(stand, PristonFormatConstants.CharacterMotionBase).Append(stand).Append(walk).ToArray();
        var info = new PristonModelInfo("fixture.inx", mesh.Path, model.Path, "", new(0, []), new(0, []), new(0, []), motions, (uint)motions.Length, "", "", "", [], 0);
        return new(info, mesh, model, []);
    }

    private sealed class PoseSink : IExternalSceneRenderer
    {
        internal Dictionary<ActorHandle, Vector4[]> Poses { get; } = [];
        public void Configure(ClientSettings settings) { }
        public Task PrepareMaterialsAsync(string directory, IEnumerable<PristonMaterial> materials, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PrepareTextureAsync(string path) => Task.CompletedTask;
        public void CreateMesh(int id, ExternalSceneMesh mesh) { }
        public void RemoveMesh(int id) { }
        public void CreateStage(int id, int[] meshIds) { }
        public void RemoveStage(int id) { }
        public void UpdateMeshPose(int id, ReadOnlySpan<Vector4> boneRows) { }
        public void SetSkyLayers(int[] currentMeshes, int[] previousMeshes, float previousOpacity, float height, float brightness) { }
        public void CreateActor(ActorHandle handle, int[] meshIds, int boneCount) => Poses.Add(handle, new Vector4[boneCount * 3]);
        public void RemoveActor(ActorHandle handle) => Poses.Remove(handle);
        public void UpdateActor(ActorHandle handle, in ExternalActorState state, ReadOnlySpan<Vector4> boneRows) => boneRows.CopyTo(Poses[handle]);
        public void Draw(in ExternalSceneFrame frame) { }
        public void Compose() { }
        public void DrawPortrait(ReadOnlySpan<ActorHandle> handles, Matrix view, Matrix projection, Vector3 cameraPosition, in PristonPortraitLighting lighting) { }
        public void Dispose() { }
    }
}
