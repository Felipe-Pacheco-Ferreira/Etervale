using PristonTale.Contracts.Characters;

namespace PristonTale.Assets;

// What the baked files of the Android client share (docs/android.md, "Baked files"): the first four bytes of each
// kind of file, the order of an animated part's clips and the name of a class's character file.
public static class MobileActorFormat
{
    public const int CharacterMagic = 0x31524843;
    public const int FieldSceneMagic = 0x35544352;
    public const int FieldSceneryMagic = 0x36544352;
    public const int SelectSceneMagic = 0x324C5343;
    public const int WorldArrivalMagic = 0x31524157;

    // A part's clips: every animated part has the first three (an NPC may lack walk or run); a character also has the
    // relaxed stand the character selection plays.
    public const int Stand = 0;
    public const int Walk = 1;
    public const int Run = 2;
    public const int SelectStand = 3;
    public const int NpcClips = 3;
    public const int CharacterClips = 4;

    public const string CharacterDirectory = "Characters";

    public static string CharacterFile(PlayerClass playerClass) => $"{(int)playerClass}.bin";
}
