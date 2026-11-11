namespace PristonTale.GltfExport;

// An animated part of a baked Android file: its batches' textures and texture coordinates once, then each clip's
// vertex positions frame by frame. A null clip is one the model lacks.
internal sealed record MobilePartBatch(int Texture, float[] Uv);

// Frames[frame][batch] are the batch's vertex positions (3 floats each) at frame / Frames.Length of the clip.
internal sealed record MobileClip(float Seconds, float[][][] Frames);

internal sealed record MobilePart(MobilePartBatch[] Batches, MobileClip?[] Clips);

// The pieces the Android bakes share: sampling a model's clips into a part and writing parts, textures and stage batches
// (docs/android.md, "Baked files").
internal static class MobileActorWriter
{
    // A named clip sampled at framesPerSecond between the bounds, or at exactly frameCount frames when given (a head
    // follows its body); null when the model has no such clip.
    public static MobileClip? BakeClip(MobileSceneBake.Model model, string name, float framesPerSecond, int fewest, int most, MobileClip? follow = null)
    {
        if (model.Duration(name) is not { } seconds || seconds <= 0f)
        {
            return null;
        }

        var count = follow?.Frames.Length ?? Math.Clamp((int)MathF.Ceiling(seconds * framesPerSecond), fewest, most);
        var frames = new float[count][][];
        for (var frame = 0; frame < count; frame++)
        {
            frames[frame] = model.Bake(name, frame / (float)count).Select(batch => Channel(batch.Vertices, 0, 3)).ToArray();
        }

        return new MobileClip(follow?.Seconds ?? seconds, frames);
    }

    // The batches of a model in the pose it is first sampled in: clip, or its bind pose when it has none.
    public static MobilePartBatch[] Batches(MobileSceneBake.Model model, string? clip) =>
        model.Bake(clip, 0f).Select(batch => new MobilePartBatch(batch.Texture, Channel(batch.Vertices, 6, 2))).ToArray();

    public static float[] Channel(float[] vertices, int offset, int size)
    {
        var count = vertices.Length / MobileSceneBake.FloatsPerVertex;
        var result = new float[count * size];
        for (var vertex = 0; vertex < count; vertex++)
        {
            Array.Copy(vertices, vertex * MobileSceneBake.FloatsPerVertex + offset, result, vertex * size, size);
        }

        return result;
    }

    public static void WriteTextures(BinaryWriter writer, IReadOnlyList<byte[]> textures)
    {
        writer.Write(textures.Count);
        foreach (var texture in textures)
        {
            writer.Write(texture.Length);
            writer.Write(texture);
        }
    }

    public static void WriteStage(BinaryWriter writer, IReadOnlyList<MobileSceneBake.Batch> stage)
    {
        writer.Write(stage.Count);
        foreach (var batch in stage)
        {
            writer.Write(batch.Texture);
            writer.Write(batch.Blend);
            WriteFloats(writer, batch.Vertices);
        }
    }

    public static void WritePart(BinaryWriter writer, MobilePart part)
    {
        writer.Write(part.Batches.Length);
        foreach (var batch in part.Batches)
        {
            writer.Write(batch.Texture);
            WriteFloats(writer, batch.Uv);
        }

        writer.Write(part.Clips.Length);
        foreach (var clip in part.Clips)
        {
            writer.Write(clip?.Seconds ?? 0f);
            writer.Write(clip?.Frames.Length ?? 0);
            foreach (var frame in clip?.Frames ?? [])
            {
                foreach (var positions in frame)
                {
                    WriteFloats(writer, positions);
                }
            }
        }
    }

    public static void WriteFloats(BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        writer.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
    }
}
