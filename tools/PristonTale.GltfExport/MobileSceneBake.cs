using System.Numerics;
using PristonTale.Assets;
using SharpGLTF.Runtime;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;

namespace PristonTale.GltfExport;

// Bakes a field's scene for the Android client (docs/android.md, "Baked files"): the stage and, for each NPC template
// given, its model as an animated part. The player's model is not in it: it comes from the class's character file.
internal static class MobileSceneBake
{
    internal const int FloatsPerVertex = 8;
    private const float NpcFramesPerSecond = 12f;
    private const int FewestNpcFrames = 2;
    private const int MostNpcFrames = 24;
    private const int PreviousMagic = 0x34544352;

    // npcDirectory holds one GLB per NPC template, named by the template's number.
    public static void Write(string stagePath, string outputPath, string? npcDirectory, string? sceneryDirectory)
    {
        var textures = new List<byte[]>();
        var stage = new Model(stagePath, textures).Bake(null, 0).ToList();
        var scenery = new List<MobilePart>();
        if (sceneryDirectory is not null)
        {
            const string animation = "Animation";
            foreach (var path in Directory.EnumerateFiles(sceneryDirectory, "*.glb").Order(StringComparer.Ordinal))
            {
                var model = new Model(path, textures);
                var clip = MobileActorWriter.BakeClip(model, animation, 12f, 2, 60);
                if (clip is null)
                {
                    stage.AddRange(model.Bake(null, 0f));
                }
                else
                {
                    scenery.Add(new MobilePart(MobileActorWriter.Batches(model, animation), [clip]));
                }
            }
        }
        var npcs = new SortedDictionary<uint, MobilePart>();
        if (npcDirectory is not null)
        {
            foreach (var path in Directory.EnumerateFiles(npcDirectory, "*.glb"))
            {
                var id = uint.Parse(Path.GetFileNameWithoutExtension(path), System.Globalization.CultureInfo.InvariantCulture);
                var model = new Model(path, textures);
                var stand = Npc(model, GltfSceneExport.StandClip);
                npcs.Add(id, new MobilePart(
                    MobileActorWriter.Batches(model, stand is null ? null : GltfSceneExport.StandClip),
                    [stand ?? Still(model), Npc(model, GltfSceneExport.WalkClip), Npc(model, GltfSceneExport.RunClip)]));
            }
        }

        Write(outputPath, textures, stage, npcs, scenery);
        Console.WriteLine($"Mobile scene: {stage.Count} stage batches, {textures.Count} textures, {npcs.Count} NPC models, {scenery.Count} scenery clips.");
    }

    // Rewrites a scene file of the previous format, which repeated every vertex's colour and texture coordinates in
    // every frame and carried one fixed player model, in the current one.
    public static void Upgrade(string previousPath, string outputPath)
    {
        using var reader = new BinaryReader(File.OpenRead(previousPath));
        if (reader.ReadInt32() != PreviousMagic)
        {
            throw new InvalidDataException("The file is not a scene of the previous format.");
        }

        var textures = new List<byte[]>();
        for (var count = reader.ReadInt32(); count > 0; count--)
        {
            textures.Add(reader.ReadBytes(reader.ReadInt32()));
        }

        var stage = ReadFrame(reader);
        for (var playerClip = 0; playerClip < 3; playerClip++)
        {
            ReadClip(reader);
        }

        var npcs = new SortedDictionary<uint, MobilePart>();
        for (var count = reader.ReadInt32(); count > 0; count--)
        {
            var id = reader.ReadUInt32();
            var clips = new MobileClip?[MobileActorFormat.NpcClips];
            MobilePartBatch[]? batches = null;
            for (var clip = 0; clip < clips.Length; clip++)
            {
                var seconds = reader.ReadSingle();
                if (seconds <= 0f)
                {
                    continue;
                }

                var frames = ReadClip(reader);
                batches ??= frames[0].Select(batch => new MobilePartBatch(batch.Texture, MobileActorWriter.Channel(batch.Vertices, 6, 2))).ToArray();
                clips[clip] = new MobileClip(seconds,
                    frames.Select(frame => frame.Select(batch => MobileActorWriter.Channel(batch.Vertices, 0, 3)).ToArray()).ToArray());
            }

            npcs.Add(id, new MobilePart(batches ?? [], clips));
        }

        Write(outputPath, textures, stage, npcs);
        Console.WriteLine($"Mobile scene upgraded: {stage.Length} stage batches, {textures.Count} textures, {npcs.Count} NPC models.");
    }

    private static void Write(string outputPath, List<byte[]> textures, IReadOnlyList<Batch> stage, SortedDictionary<uint, MobilePart> npcs,
        IReadOnlyList<MobilePart>? scenery = null)
    {
        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.Write(scenery is { Count: > 0 } ? MobileActorFormat.FieldSceneryMagic : MobileActorFormat.FieldSceneMagic);
        MobileActorWriter.WriteTextures(writer, textures);
        MobileActorWriter.WriteStage(writer, stage);
        writer.Write(npcs.Count);
        foreach (var (id, part) in npcs)
        {
            writer.Write(id);
            MobileActorWriter.WritePart(writer, part);
        }

        if (scenery is { Count: > 0 })
        {
            writer.Write(scenery.Count);
            foreach (var part in scenery)
            {
                MobileActorWriter.WritePart(writer, part);
            }
        }
    }

    private static MobileClip? Npc(Model model, string clip) =>
        MobileActorWriter.BakeClip(model, clip, NpcFramesPerSecond, FewestNpcFrames, MostNpcFrames);

    // A model without a stand keeps its bind pose.
    private static MobileClip Still(Model model) =>
        new(1f, [model.Bake(null, 0f).Select(batch => MobileActorWriter.Channel(batch.Vertices, 0, 3)).ToArray()]);

    private static Batch[][] ReadClip(BinaryReader reader)
    {
        var frames = new Batch[reader.ReadInt32()][];
        for (var frame = 0; frame < frames.Length; frame++)
        {
            frames[frame] = ReadFrame(reader);
        }

        return frames;
    }

    private static Batch[] ReadFrame(BinaryReader reader)
    {
        var batches = new Batch[reader.ReadInt32()];
        for (var index = 0; index < batches.Length; index++)
        {
            var texture = reader.ReadInt32();
            var vertices = new float[reader.ReadInt32()];
            reader.BaseStream.ReadExactly(System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan()));
            batches[index] = new Batch(texture, vertices);
        }

        return batches;
    }

    // Vertices hold position, colour and texture coordinates (FloatsPerVertex each); Blend is 0 opaque, 1 cut-out, 2 blended.
    internal sealed record Batch(int Texture, float[] Vertices, int Blend = 0);

    internal sealed class Model
    {
        private readonly ModelRoot _root;
        private readonly SceneInstance _scene;
        private readonly Dictionary<int, int> _textureIndices = new();

        public Model(string path, List<byte[]> textures)
        {
            _root = ModelRoot.Load(path);
            _scene = SceneTemplate.Create(_root.DefaultScene).CreateInstance();
            foreach (var image in _root.LogicalImages)
            {
                var bytes = image.Content.Content.ToArray();
                var index = textures.FindIndex(existing => existing.AsSpan().SequenceEqual(bytes));
                if (index < 0)
                {
                    index = textures.Count;
                    textures.Add(bytes);
                }
                _textureIndices.Add(image.LogicalIndex, index);
            }
        }

        public float? Duration(string clip) => _root.LogicalAnimations.FirstOrDefault(item => item.Name == clip)?.Duration;

        public IReadOnlyList<Batch> Bake(string? clip, float phase)
        {
            if (clip is not null)
            {
                var animation = _root.LogicalAnimations.Single(item => item.Name == clip);
                _scene.Armature.SetAnimationFrame(animation.LogicalIndex, animation.Duration * phase, false);
            }
            var batches = new List<Batch>();
            foreach (var drawable in _scene)
            {
                foreach (var primitive in _root.LogicalMeshes[drawable.Template.LogicalMeshIndex].Primitives)
                {
                    batches.Add(BakePrimitive(primitive, drawable.Transform));
                }
            }
            return batches;
        }

        private Batch BakePrimitive(MeshPrimitive primitive, IGeometryTransform transform)
        {
            var positions = primitive.GetVertexAccessor("POSITION").AsVector3Array();
            var uv = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
            var color = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
            var joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
            var weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
            var image = primitive.Material?.FindChannel("BaseColor")?.Texture?.PrimaryImage;
            var triangles = primitive.GetTriangleIndices().ToArray();
            var vertices = new float[triangles.Length * 3 * FloatsPerVertex];
            var offset = 0;
            foreach (var (a, b, c) in triangles)
            {
                Put(a);
                Put(b);
                Put(c);
            }
            var blend = primitive.Material?.Alpha switch
            {
                AlphaMode.BLEND => 2,
                AlphaMode.MASK => 1,
                _ => 0
            };
            return new Batch(image is null ? -1 : _textureIndices[image.LogicalIndex], vertices, blend);

            void Put(int index)
            {
                var skin = joints is not null && weights is not null ? SparseWeight8.Create(joints[index], weights[index]) : default;
                var p = transform.TransformPosition(positions[index], null, skin);
                var t = uv?[index] ?? Vector2.Zero;
                var tint = color?[index] ?? Vector4.One;
                vertices[offset++] = p.X;
                vertices[offset++] = p.Y;
                vertices[offset++] = -p.Z;
                vertices[offset++] = tint.X;
                vertices[offset++] = tint.Y;
                vertices[offset++] = tint.Z;
                vertices[offset++] = t.X;
                vertices[offset++] = t.Y;
            }
        }
    }
}
