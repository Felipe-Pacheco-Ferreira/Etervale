using PristonTale.Contracts.Characters;

namespace PristonTale.Assets;

// The character selection's original art under the asset root, and the order the classes of each tribe line up in. The
// desktop screen loads these files as they are; the Android build packages them converted to PNG under the same names.
public static class CharacterSelectArt
{
    public const string LoginDirectory = "StartImage/login";
    public const string ScreenDirectory = LoginDirectory + "/CharSelect";
    public const string MorionFaceDirectory = LoginDirectory + "/Moryon";

    public const string TempskronMark = "mark_T.tga";
    public const string MorionMark = "mark_M.tga";
    public const string TempskronTitle = "T-tempscron.tga";
    public const string MorionTitle = "T-Morion.tga";
    public const string TempskronDescription = "T-ex_tscr.tga";
    public const string MorionDescription = "T-ex_mrin.tga";

    public const string SelectButton = "Bt_select.tga";
    public const string BackButton = "Bt_back.tga";
    public const string ExitButton = "Bt_exit.tga";
    public const string DeleteButton = "Bt_delet.tga";
    public const string NewCharacterButton = "bt-Newchar.tga";
    public const string NewCharacterPrompt = "New_char.tga";
    public const string FrameLeft = "mainbox_L.tga";
    public const string FrameTop = "mainbox_T.tga";
    public const string FrameRight = "mainbox_R.tga";
    public const string FrameBottom = "mainbox_D.tga";
    public const string FrameCorner1 = "mainbox_CN1.tga";
    public const string FrameCorner2 = "mainbox_CN2.tga";
    public const string FrameCorner3 = "mainbox_CN3.tga";
    public const string FrameCorner4 = "mainbox_CN4.tga";
    public const string TitleBox = "title-box.tga";
    // Four 128 x 30 rows: character select, tribe, class, face.
    public const string TitleText = "title-txt30.tga";
    public const string TribeBoxMain = "mark_box1.tga";
    public const string TribeBoxEnd = "mark_box2.tga";
    public const string ClassBoxMain = "T_cls-box1.tga";
    public const string ClassBoxEnd = "T_cls-box2.tga";
    public const string FaceBackdropTop = "face_bigbox1.tga";
    public const string FaceBackdropBottom = "face_bigbox2.tga";
    public const string FaceBoxNormal = "face_box_.tga";
    public const string FaceBoxSelected = "face_box.tga";
    public const string FaceNameBox = "face_box2.tga";
    // These two are in LoginDirectory.
    public const string InfoBackPanel = "UserCharacterInfo.tga";
    public const string InfoOverlay = "CharacterInfo.tga";

    // The client's UI font, which draws the screen's text.
    public const string Font = "Hud/Fonts/DejaVuSans.ttf";

    // The brazier flames' flipbook: FlameFrames files "<stem><1..n>.<ext>" beside FlameTexture.
    public const string FlameTexture = "image/flame.bmp";
    public const int FlameFrames = 8;

    // Every file of the screen's 2D art and flames, as (directory, name) under the asset root.
    public static IEnumerable<(string Directory, string Name)> Files()
    {
        string[] screen =
        [
            TempskronMark, MorionMark, TempskronTitle, MorionTitle, TempskronDescription, MorionDescription, SelectButton, BackButton, ExitButton,
            DeleteButton, NewCharacterButton, NewCharacterPrompt, FrameLeft, FrameTop, FrameRight, FrameBottom, FrameCorner1, FrameCorner2, FrameCorner3,
            FrameCorner4, TitleBox, TitleText, TribeBoxMain, TribeBoxEnd, ClassBoxMain, ClassBoxEnd, FaceBackdropTop, FaceBackdropBottom, FaceBoxNormal,
            FaceBoxSelected, FaceNameBox
        ];
        foreach (var name in screen)
        {
            yield return (ScreenDirectory, name);
        }

        yield return (LoginDirectory, InfoBackPanel);
        yield return (LoginDirectory, InfoOverlay);
        foreach (var (playerClass, art) in Classes)
        {
            yield return (ScreenDirectory, art.NormalLabel);
            yield return (ScreenDirectory, art.SelectedLabel);
            yield return (ScreenDirectory, art.Description);
            var faces = Faces(playerClass);
            foreach (var name in faces.Normal.Concat(faces.Selected))
            {
                yield return (faces.Directory, name);
            }
        }

        for (var frame = 1; frame <= FlameFrames; frame++)
        {
            yield return (Path.GetDirectoryName(FlameTexture)!, FlameFrame(frame));
        }
    }

    public static string FlameFrame(int number) =>
        $"{Path.GetFileNameWithoutExtension(FlameTexture)}{number}{Path.GetExtension(FlameTexture)}";

    public static IReadOnlyList<PlayerClass> TempskronLineup { get; } =
        [PlayerClass.Mechanician, PlayerClass.Fighter, PlayerClass.Pikeman, PlayerClass.Archer, PlayerClass.Assassin, PlayerClass.MartialArtist];

    public static IReadOnlyList<PlayerClass> MorionLineup { get; } =
        [PlayerClass.Knight, PlayerClass.Atalanta, PlayerClass.Priestess, PlayerClass.Magician, PlayerClass.Shaman];

    public static IReadOnlyDictionary<PlayerClass, ClassArt> Classes { get; } = new Dictionary<PlayerClass, ClassArt>
    {
        [PlayerClass.Mechanician] = new("T_cls-meca_.tga", "T_cls-meca.tga", "T_chr-meca.tga"),
        [PlayerClass.Fighter] = new("T_cls-ft_.tga", "T_cls-ft.tga", "T_chr-ft.tga"),
        [PlayerClass.Pikeman] = new("T_cls-pkm_.tga", "T_cls-pkm.tga", "T_chr-pkm.tga"),
        [PlayerClass.Archer] = new("T_cls-ac_.tga", "T_cls-ac.tga", "T_chr-ac.tga"),
        [PlayerClass.Knight] = new("T-cls-knt_.tga", "T-cls-knt.tga", "T_chr-knt.tga"),
        [PlayerClass.Atalanta] = new("T_cls-atlt_.tga", "T_cls-atlt.tga", "T_chr-atlt.tga"),
        [PlayerClass.Priestess] = new("T_cls-prts_.tga", "T_cls-prts.tga", "T_chr-prts.tga"),
        [PlayerClass.Magician] = new("T_cls-mgc_.tga", "T_cls-mgc.tga", "T_chr-mgc.tga"),
        [PlayerClass.Assassin] = new("T_cls-assa_.tga", "T_cls-assa.tga", "T_chr-assa.tga"),
        [PlayerClass.Shaman] = new("T_cls-sha_.tga", "T_cls-sha.tga", "T_chr-sha.tga"),
        [PlayerClass.MartialArtist] = new("T_cls-ma_.tga", "T_cls-ma.tga", "T_chr-ma.tga"),
    };

    // The thumbnails of a class's faces in face-index order, each unselected and selected.
    public static FaceThumbnails Faces(PlayerClass playerClass)
    {
        var stem = playerClass switch
        {
            PlayerClass.Mechanician => "TmA",
            PlayerClass.Fighter => "TmB",
            PlayerClass.Pikeman => "TmC",
            PlayerClass.Archer => "TfD",
            PlayerClass.Knight => "MmA",
            PlayerClass.Atalanta => "MfB",
            PlayerClass.Priestess => "MfC",
            PlayerClass.Magician => "MmD",
            PlayerClass.Assassin => "TfE",
            PlayerClass.Shaman => "MmE",
            PlayerClass.MartialArtist => "TfF",
            _ => throw new ArgumentOutOfRangeException(nameof(playerClass), playerClass, "Unsupported character-select class.")
        };
        var morion = MorionLineup.Contains(playerClass);
        var usesLetterSuffix = morion || playerClass is PlayerClass.Assassin or PlayerClass.MartialArtist;
        // The Atalanta's first two thumbnails are stored in the other order.
        int[] order = playerClass == PlayerClass.Atalanta ? [2, 1, 3] : [1, 2, 3];
        var normalSuffix = usesLetterSuffix ? "-a.tga" : "-01.tga";
        var selectedSuffix = usesLetterSuffix ? "-b.tga" : "-02.tga";
        var normal = new string[order.Length];
        var selected = new string[order.Length];
        for (var index = 0; index < order.Length; index++)
        {
            normal[index] = $"{stem}0{order[index]}{normalSuffix}";
            selected[index] = $"{stem}0{order[index]}{selectedSuffix}";
        }

        return new FaceThumbnails(morion ? MorionFaceDirectory : LoginDirectory, normal, selected);
    }
}

public sealed record ClassArt(string NormalLabel, string SelectedLabel, string Description);

public sealed record FaceThumbnails(string Directory, IReadOnlyList<string> Normal, IReadOnlyList<string> Selected);
