using System.Numerics;
using SharpGLTF.Runtime;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;

namespace PristonTale.GltfExport;

internal static class MobileSceneBake
{
    private const int IdleFrames = 16;
    private const int WalkFrames = 56;

    public static void Write(string stagePath, string bodyPath, string headPath, string outputPath, string? npcDirectory)
    {
        var textures = new List<byte[]>();
        var stage = new Model(stagePath, textures);
        var body = new Model(bodyPath, textures);
        var head = new Model(headPath, textures);
        var terrain = stage.Bake(null, 0);
        var idle = BakeClip(body, head, "Stand", IdleFrames);
        var walk = BakeClip(body, head, "Walk", WalkFrames);
        var run = BakeClip(body, head, "Run", WalkFrames);
        var npcs = new SortedDictionary<uint, (NpcClip Idle, NpcClip? Walk, NpcClip? Run)>();
        if (npcDirectory is not null)
        {
            foreach (var path in Directory.EnumerateFiles(npcDirectory, "*.glb"))
            {
                var id = uint.Parse(Path.GetFileNameWithoutExtension(path), System.Globalization.CultureInfo.InvariantCulture);
                var model = new Model(path, textures);
                npcs.Add(id, (model.BakeNpcClip("Stand") ?? new NpcClip(1f, [model.Bake(null, 0f)]), model.BakeNpcClip("Walk"), model.BakeNpcClip("Run")));
            }
        }

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.Write(0x34544352);
        writer.Write(textures.Count);
        foreach (var texture in textures)
        {
            writer.Write(texture.Length);
            writer.Write(texture);
        }
        WriteFrame(writer, terrain);
        WriteClip(writer, idle);
        WriteClip(writer, walk);
        WriteClip(writer, run);
        writer.Write(npcs.Count);
        foreach (var (id, model) in npcs)
        {
            writer.Write(id);
            WriteNpcClip(writer, model.Idle);
            WriteNpcClip(writer, model.Walk);
            WriteNpcClip(writer, model.Run);
        }
        Console.WriteLine($"Mobile scene: {terrain.Count} stage batches, {textures.Count} textures, {idle.Length} idle and {walk.Length} walk and {run.Length} run frames.");
    }

    private static IReadOnlyList<Batch>[] BakeClip(Model body, Model head, string clip, int frames)
    {
        var result = new IReadOnlyList<Batch>[frames];
        for (var i = 0; i < frames; i++)
        {
            var phase = i / (float)frames;
            result[i] = body.Bake(clip, phase).Concat(head.Bake(clip, phase)).ToArray();
        }
        return result;
    }

    private sealed record NpcClip(float Seconds, IReadOnlyList<Batch>[] Frames);

    private static void WriteNpcClip(BinaryWriter writer, NpcClip? clip)
    {
        writer.Write(clip?.Seconds ?? 0f);
        if (clip is not null)
        {
            WriteClip(writer, clip.Frames);
        }
    }

    private static void WriteClip(BinaryWriter writer, IReadOnlyList<Batch>[] frames)
    {
        writer.Write(frames.Length);
        foreach (var frame in frames)
        {
            WriteFrame(writer, frame);
        }
    }

    private static void WriteFrame(BinaryWriter writer, IReadOnlyList<Batch> batches)
    {
        writer.Write(batches.Count);
        foreach (var batch in batches)
        {
            writer.Write(batch.Texture);
            writer.Write(batch.Vertices.Length);
            foreach (var value in batch.Vertices)
            {
                writer.Write(value);
            }
        }
    }

    private sealed record Batch(int Texture, float[] Vertices);

    private sealed class Model
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

        public NpcClip? BakeNpcClip(string name)
        {
            var animation = _root.LogicalAnimations.FirstOrDefault(clip => clip.Name == name);
            if (animation is null || animation.Duration <= 0f)
            {
                return null;
            }

            var count = Math.Clamp((int)MathF.Ceiling(animation.Duration * 12f), 2, 24);
            var frames = new IReadOnlyList<Batch>[count];
            for (var i = 0; i < count; i++)
            {
                frames[i] = Bake(animation.Name, i / (float)count);
            }
            return new NpcClip(animation.Duration, frames);
        }

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
            var vertices = new float[triangles.Length * 3 * 8];
            var offset = 0;
            foreach (var (a, b, c) in triangles)
            {
                Put(a);
                Put(b);
                Put(c);
            }
            return new Batch(image is null ? -1 : _textureIndices[image.LogicalIndex], vertices);

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
