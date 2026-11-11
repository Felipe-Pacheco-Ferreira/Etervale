using System.Runtime.InteropServices;
using Android.Content.Res;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Mobile;

// A stage batch: position, colour and texture coordinates (8 floats a vertex); Blend is 0 opaque, 1 cut-out, 2 blended.
internal sealed record SceneBatch(int Texture, int Blend, float[] Vertices);

internal sealed record PartBatch(int Texture, float[] Uv);

// Frames[frame][batch] are the batch's vertex positions (3 floats each) at frame / Frames.Length of the clip.
internal sealed record PartClip(float Seconds, float[][][] Frames);

// An animated model: its batches' textures and texture coordinates, and its clips in the order of MobileActorFormat; a
// null clip is one the model lacks.
internal sealed record ActorPart(PartBatch[] Batches, PartClip?[] Clips);

// A class's character file: its default body and each of its faces, with the textures they index.
internal sealed record CharacterModel(byte[][] Textures, ActorPart Body, ActorPart[] Heads)
{
    public static CharacterModel Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        BakedReader.Magic(reader, MobileActorFormat.CharacterMagic);
        var textures = BakedReader.Textures(reader);
        var body = BakedReader.Part(reader);
        var heads = new ActorPart[BakedReader.Count(reader, BakedReader.MostBatches)];
        for (var face = 0; face < heads.Length; face++)
        {
            heads[face] = BakedReader.Part(reader);
        }

        if (heads.Length == 0 || body.Clips.Length < MobileActorFormat.CharacterClips)
        {
            throw new InvalidDataException("The character file is incomplete.");
        }

        return new CharacterModel(textures, body, heads);
    }

    // Reads a class's packaged file off the calling thread.
    public static Task<CharacterModel> LoadAsync(AssetManager assets, PlayerClass playerClass) => Task.Run(() =>
    {
        using var asset = assets.Open($"{MobileActorFormat.CharacterDirectory}/{MobileActorFormat.CharacterFile(playerClass)}");
        using var buffered = new BufferedStream(asset, BakedReader.BufferSize);
        return Read(buffered);
    });
}

// The pieces the baked files share (docs/android.md, "Baked files"). Files are little-endian; float arrays are read in
// one piece.
internal static class BakedReader
{
    public const int BufferSize = 1 << 16;
    public const int MostBatches = 1024;
    private const int MostTextures = 1024;
    private const int MostTextureBytes = 8 * 1024 * 1024;
    private const int MostFloats = 6_000_000;
    private const int MostFrames = 120;
    private const int MostClips = 16;

    public static void Magic(BinaryReader reader, int expected)
    {
        if (!BitConverter.IsLittleEndian || reader.ReadInt32() != expected)
        {
            throw new InvalidDataException("Unsupported baked file format.");
        }
    }

    public static byte[][] Textures(BinaryReader reader)
    {
        var textures = new byte[Count(reader, MostTextures)][];
        for (var index = 0; index < textures.Length; index++)
        {
            textures[index] = reader.ReadBytes(Count(reader, MostTextureBytes));
        }

        return textures;
    }

    public static SceneBatch[] Stage(BinaryReader reader)
    {
        var stage = new SceneBatch[Count(reader, MostBatches)];
        for (var index = 0; index < stage.Length; index++)
        {
            stage[index] = new SceneBatch(reader.ReadInt32(), reader.ReadInt32(), Floats(reader));
        }

        return stage;
    }

    public static ActorPart Part(BinaryReader reader)
    {
        var batches = new PartBatch[Count(reader, MostBatches)];
        for (var index = 0; index < batches.Length; index++)
        {
            batches[index] = new PartBatch(reader.ReadInt32(), Floats(reader));
        }

        var clips = new PartClip?[Count(reader, MostClips)];
        for (var clip = 0; clip < clips.Length; clip++)
        {
            var seconds = reader.ReadSingle();
            var frames = new float[Count(reader, MostFrames)][][];
            for (var frame = 0; frame < frames.Length; frame++)
            {
                frames[frame] = new float[batches.Length][];
                for (var batch = 0; batch < batches.Length; batch++)
                {
                    frames[frame][batch] = Floats(reader);
                }
            }

            clips[clip] = frames.Length > 0 && seconds > 0f ? new PartClip(seconds, frames) : null;
        }

        return new ActorPart(batches, clips);
    }

    public static int Count(BinaryReader reader, int most)
    {
        var count = reader.ReadInt32();
        return count >= 0 && count <= most ? count : throw new InvalidDataException("The baked file is damaged.");
    }

    private static float[] Floats(BinaryReader reader)
    {
        var values = new float[Count(reader, MostFloats)];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }
}
