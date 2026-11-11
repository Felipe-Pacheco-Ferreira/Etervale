namespace PristonTale.Mobile;

internal sealed record MobileScene(byte[][] Textures, MobileBatch[] Stage, MobileBatch[][] Idle, MobileBatch[][] Walk, MobileBatch[][] Run, IReadOnlyDictionary<uint, MobileNpc> Npcs)
{
    public static MobileScene Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var format = reader.ReadInt32();
        if (format is not (0x31544352 or 0x32544352 or 0x33544352 or 0x34544352))
        {
            throw new InvalidDataException("Unsupported Ricarten scene format.");
        }

        var textures = new byte[Count(reader, 1024)][];
        for (var i = 0; i < textures.Length; i++)
        {
            var size = Count(reader, 8_000_000);
            textures[i] = reader.ReadBytes(size);
            if (textures[i].Length != size)
            {
                throw new EndOfStreamException();
            }
        }

        var stage = Frame();
        var idle = Clip();
        var walk = Clip();
        var run = format >= 0x32544352 ? Clip() : walk;
        var npcs = new Dictionary<uint, MobileNpc>();
        var count = stream.CanSeek && stream.Position == stream.Length ? 0 : Count(reader, 1024);
        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadUInt32();
            var idleNpc = format >= 0x33544352
                ? ReadNpcClip() ?? throw new InvalidDataException("Missing NPC idle clip.")
                : new MobileNpcClip(1f, [Frame()]);
            var walkNpc = format >= 0x34544352 ? ReadNpcClip() : null;
            var runNpc = format >= 0x34544352 ? ReadNpcClip() : null;
            npcs.Add(id, new MobileNpc(idleNpc, walkNpc, runNpc));
        }
        return new MobileScene(textures, stage, idle, walk, run, npcs);

        MobileNpcClip? ReadNpcClip()
        {
            var seconds = reader.ReadSingle();
            if (!float.IsFinite(seconds) || seconds < 0f)
            {
                throw new InvalidDataException("Invalid Ricarten NPC animation duration.");
            }
            return seconds > 0f ? new MobileNpcClip(seconds, Clip()) : null;
        }

        MobileBatch[] Frame()
        {
            var batches = new MobileBatch[Count(reader, 1024)];
            for (var i = 0; i < batches.Length; i++)
            {
                var texture = reader.ReadInt32();
                var size = Count(reader, 6_000_000);
                if (texture < -1 || texture >= textures.Length || size % 24 != 0)
                {
                    throw new InvalidDataException("Invalid Ricarten mesh batch.");
                }
                var vertices = new float[size];
                for (var j = 0; j < vertices.Length; j++)
                {
                    vertices[j] = reader.ReadSingle();
                }
                batches[i] = new MobileBatch(texture, vertices);
            }
            return batches;
        }

        MobileBatch[][] Clip()
        {
            var frames = new MobileBatch[Count(reader, 120)][];
            if (frames.Length == 0)
            {
                throw new InvalidDataException("An avatar animation has no frames.");
            }
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i] = Frame();
            }
            return frames;
        }
    }

    private static int Count(BinaryReader reader, int maximum)
    {
        var value = reader.ReadInt32();
        return value >= 0 && value <= maximum ? value : throw new InvalidDataException("Invalid Ricarten scene count.");
    }
}

internal sealed record MobileBatch(int Texture, float[] Vertices);

internal sealed record MobileNpc(MobileNpcClip Idle, MobileNpcClip? Walk, MobileNpcClip? Run);
internal sealed record MobileNpcClip(float Seconds, MobileBatch[][] Frames);
