using System.Buffers.Binary;
using PristonTale.Assets;

namespace PristonTale.Engine3D.IO;

public static class PristonWaveDecoder
{
    private const int WaveHeaderSize = 12;

    public static Stream OpenRead(string path) => new MemoryStream(ReadAllBytes(path), writable: false);

    // The file with its header normalized to RIFF, as SoundEffect.FromStream reads it.
    public static byte[] ReadAllBytes(string path)
    {
        var bytes = AssetFiles.ReadAllBytes(path);
        NormalizeHeader(bytes);
        return bytes;
    }

    internal static void NormalizeHeader(Span<byte> bytes)
    {
        if (bytes.Length < WaveHeaderSize || !bytes[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Audio asset is not a WAVE container.");
        }

        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]);
        if (declaredSize > bytes.Length - 8)
        {
            throw new InvalidDataException("WAVE container declares data beyond the end of the asset.");
        }

        if (bytes[..4].SequenceEqual("RIFF"u8))
        {
            return;
        }

        if (!bytes[..4].SequenceEqual("JODO"u8))
        {
            throw new InvalidDataException("Unsupported original WAVE signature.");
        }

        "RIFF"u8.CopyTo(bytes);
    }
}
