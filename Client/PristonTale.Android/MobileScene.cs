using PristonTale.Assets;

namespace PristonTale.Mobile;

// A field's baked scene (docs/android.md, "Baked files"): its textures, its stage and the model of each NPC template
// baked for it. The player's model is not here; it is the class's character file.
internal sealed record MobileScene(byte[][] Textures, SceneBatch[] Stage, IReadOnlyDictionary<uint, ActorPart> Npcs, ActorPart[] Scenery)
{
    public StageCollisionReader.StageLamp[] Lamps { get; init; } = [];

    private const int MostNpcs = 1024;

    public static MobileScene Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var magic = reader.ReadInt32();
        if (!BitConverter.IsLittleEndian || magic is not (MobileActorFormat.FieldSceneMagic or MobileActorFormat.FieldSceneryMagic))
        {
            throw new InvalidDataException("Unsupported baked field format.");
        }
        var textures = BakedReader.Textures(reader);
        var stage = BakedReader.Stage(reader);
        var npcs = new Dictionary<uint, ActorPart>();
        for (var count = BakedReader.Count(reader, MostNpcs); count > 0; count--)
        {
            var id = reader.ReadUInt32();
            var part = BakedReader.Part(reader);
            if (part.Clips.Length < MobileActorFormat.NpcClips || part.Clips[MobileActorFormat.Stand] is null)
            {
                throw new InvalidDataException("An NPC model has no stand.");
            }

            npcs.Add(id, part);
        }

        var scenery = magic == MobileActorFormat.FieldSceneryMagic
            ? new ActorPart[BakedReader.Count(reader, BakedReader.MostBatches)]
            : [];
        for (var index = 0; index < scenery.Length; index++)
        {
            scenery[index] = BakedReader.Part(reader);
            if (scenery[index].Clips is not [{ }])
            {
                throw new InvalidDataException("A scenery model has no animation.");
            }
        }

        return new MobileScene(textures, stage, npcs, scenery);
    }
}
