using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.UI.Clans;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

internal sealed class CharSelectAssets : IDisposable
{
    private const string UiDir = CharacterSelectArt.ScreenDirectory;
    private const string UiParentDir = CharacterSelectArt.LoginDirectory;

    private readonly PristonTextureLoader _loader;
    private readonly Func<string, string> _assetPath;
    private readonly Dictionary<PlayerClass, Texture2D> _normalClassLabels = new();
    private readonly Dictionary<PlayerClass, Texture2D> _selectedClassLabels = new();
    private readonly Dictionary<PlayerClass, Texture2D> _classDescriptions = new();
    private readonly Texture2D[,] _faceThumbs = new Texture2D[CharacterFace.FaceCount, 2];

    public CharSelectAssets(GraphicsDevice graphicsDevice, Func<string, string> assetPath)
    {
        _assetPath = assetPath;
        _loader = new PristonTextureLoader(graphicsDevice);
        _loader.Initialize();

        BtOk = Ui("Bt_ok.tga");
        BtSelect = Ui(CharacterSelectArt.SelectButton);
        BtBack = Ui(CharacterSelectArt.BackButton);
        BtExit = Ui(CharacterSelectArt.ExitButton);
        BtDelete = Ui(CharacterSelectArt.DeleteButton);
        BtNewChar = Ui(CharacterSelectArt.NewCharacterButton);
        NewCharBig = Ui(CharacterSelectArt.NewCharacterPrompt);

        FrameL = Ui(CharacterSelectArt.FrameLeft);
        FrameT = Ui(CharacterSelectArt.FrameTop);
        FrameR = Ui(CharacterSelectArt.FrameRight);
        FrameD = Ui(CharacterSelectArt.FrameBottom);
        FrameCn1 = Ui(CharacterSelectArt.FrameCorner1);
        FrameCn2 = Ui(CharacterSelectArt.FrameCorner2);
        FrameCn3 = Ui(CharacterSelectArt.FrameCorner3);
        FrameCn4 = Ui(CharacterSelectArt.FrameCorner4);

        TitleBox = Ui(CharacterSelectArt.TitleBox);
        TitleTxt = Ui(CharacterSelectArt.TitleText);

        MarkTempscron = Ui(CharacterSelectArt.TempskronMark);
        MarkMorion = Ui(CharacterSelectArt.MorionMark);
        LogoTempscron = Ui(CharacterSelectArt.TempskronTitle);
        LogoMorion = Ui(CharacterSelectArt.MorionTitle);
        RaceBoxMain = Ui(CharacterSelectArt.TribeBoxMain);
        RaceBoxEnd = Ui(CharacterSelectArt.TribeBoxEnd);
        TempscronDescription = Ui(CharacterSelectArt.TempskronDescription);
        MorionDescription = Ui(CharacterSelectArt.MorionDescription);

        JobBoxMain = Ui(CharacterSelectArt.ClassBoxMain);
        JobBoxEnd = Ui(CharacterSelectArt.ClassBoxEnd);

        InfoBackPanel = UiParent(CharacterSelectArt.InfoBackPanel);
        InfoOverlay = UiParent(CharacterSelectArt.InfoOverlay);

        FaceBigBoxTop = Ui(CharacterSelectArt.FaceBackdropTop);
        FaceBigBoxBottom = Ui(CharacterSelectArt.FaceBackdropBottom);
        FaceBoxNormal = Ui(CharacterSelectArt.FaceBoxNormal);
        FaceBoxSelected = Ui(CharacterSelectArt.FaceBoxSelected);
        FaceNameBox = Ui(CharacterSelectArt.FaceNameBox);

        foreach (var (playerClass, files) in CharacterSelectArt.Classes)
        {
            _normalClassLabels[playerClass] = Ui(files.NormalLabel);
            _selectedClassLabels[playerClass] = Ui(files.SelectedLabel);
            _classDescriptions[playerClass] = Ui(files.Description);
        }

        Pixel = new Texture2D(graphicsDevice, 1, 1);
        Pixel.SetData([Color.White]);
        ClanMark = ClanMarks.Default(graphicsDevice);
    }

    public Texture2D BtOk { get; }
    public Texture2D BtSelect { get; }
    public Texture2D BtBack { get; }
    public Texture2D BtExit { get; }
    public Texture2D BtDelete { get; }
    public Texture2D BtNewChar { get; }
    public Texture2D NewCharBig { get; }

    public Texture2D FrameL { get; }
    public Texture2D FrameT { get; }
    public Texture2D FrameR { get; }
    public Texture2D FrameD { get; }
    public Texture2D FrameCn1 { get; }
    public Texture2D FrameCn2 { get; }
    public Texture2D FrameCn3 { get; }
    public Texture2D FrameCn4 { get; }

    public Texture2D TitleBox { get; }
    public Texture2D TitleTxt { get; }

    public Texture2D MarkTempscron { get; }
    public Texture2D MarkMorion { get; }
    public Texture2D LogoTempscron { get; }
    public Texture2D LogoMorion { get; }
    public Texture2D RaceBoxMain { get; }
    public Texture2D RaceBoxEnd { get; }
    public Texture2D TempscronDescription { get; }
    public Texture2D MorionDescription { get; }
    public Texture2D JobBoxMain { get; }
    public Texture2D JobBoxEnd { get; }

    public Texture2D InfoBackPanel { get; }
    public Texture2D InfoOverlay { get; }
    public Texture2D FaceBigBoxTop { get; }
    public Texture2D FaceBigBoxBottom { get; }
    public Texture2D FaceBoxNormal { get; }
    public Texture2D FaceBoxSelected { get; }
    public Texture2D FaceNameBox { get; }

    public Texture2D Pixel { get; }

    public Texture2D ClanMark { get; }

    public bool TryClassLabel(PlayerClass playerClass, bool selected, out Texture2D label)
    {
        var labels = selected ? _selectedClassLabels : _normalClassLabels;
        return labels.TryGetValue(playerClass, out label);
    }

    public bool TryClassDescription(PlayerClass playerClass, out Texture2D description)
    {
        return _classDescriptions.TryGetValue(playerClass, out description);
    }

    public Texture2D FaceThumb(int index, bool selected) => _faceThumbs[index, selected ? 1 : 0];

    public void ClearFaceThumbs() => Array.Clear(_faceThumbs);

    public void LoadFaceThumbs(PlayerClass playerClass)
    {
        Array.Clear(_faceThumbs);
        var faces = CharacterSelectArt.Faces(playerClass);
        for (var index = 0; index < CharacterFace.FaceCount; index++)
        {
            _faceThumbs[index, 0] = _loader.LoadUiTexture(_assetPath(faces.Directory), faces.Normal[index]);
            _faceThumbs[index, 1] = _loader.LoadUiTexture(_assetPath(faces.Directory), faces.Selected[index]);
        }
    }

    private Texture2D Ui(string name) => _loader.LoadUiTexture(_assetPath(UiDir), name);
    private Texture2D UiParent(string name) => _loader.LoadUiTexture(_assetPath(UiParentDir), name);

    public void Dispose()
    {
        Pixel?.Dispose();
        ClanMark?.Dispose();
        _loader?.Dispose();
    }
}
