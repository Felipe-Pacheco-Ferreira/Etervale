namespace PristonTale.Assets;

// The character selection's original sounds under the asset root. The desktop client plays these files; the Android
// build packages them under their own names.
public static class CharacterSelectSounds
{
    public const string Music = "StartImage/login/CharacterSelect.wav";

    public const string Button = "wav/Button.wav";
    public const string NewCharacter = "wav/New.wav";
    public const string Delete = "wav/CharacterDelete.WAV";
    public const string Back = "wav/ButtonBack.wav";
    public const string PickCharacter = "wav/CharacterSelect.wav";
    public const string GameStart = "wav/GameStart.wav";

    // The loops beside the music, at the original's volumes out of 400.
    public static IReadOnlyList<(string Path, float Volume)> Loops { get; } =
    [
        ("wav/NewChar.wav", 350f / 400f),
        ("wav/Fire.wav", 1f),
        ("wav/Thunder.wav", 1f)
    ];

    public static IReadOnlyList<string> Cues { get; } = [Button, NewCharacter, Delete, Back, PickCharacter, GameStart];

    // The name the Android build packages a cue or loop under: its file name with a lower-case extension, which the
    // package stores uncompressed so the sound can be played from it.
    public static string PackagedName(string path) => Path.ChangeExtension(Path.GetFileName(path), Path.GetExtension(path).ToLowerInvariant());
}
