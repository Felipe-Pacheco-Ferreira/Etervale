using PristonTale.Assets;

namespace PristonTale.Mobile.CharacterSelect;

// The character selection's baked stage with its textures (docs/android.md, "Baked files"), written by
// ptgltf --mobile-char-select. Positions are in the stage's own space; the renderer mirrors z.
internal sealed record CharSelectSceneData(byte[][] Textures, SceneBatch[] Stage)
{
    public static CharSelectSceneData Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        BakedReader.Magic(reader, MobileActorFormat.SelectSceneMagic);
        return new CharSelectSceneData(BakedReader.Textures(reader), BakedReader.Stage(reader));
    }
}
