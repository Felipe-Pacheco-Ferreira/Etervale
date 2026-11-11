using System.Numerics;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;

namespace PristonTale.Mobile.CharacterSelect;

internal readonly record struct CharSelectJobView(PlayerClass Class, Vector3 HeadWorld, bool Hovered, bool Selected);

internal sealed class CharSelectViewModel
{
    public int StateIndex;
    public string ErrorMessage = string.Empty;
    public double ElapsedSeconds;
    public bool RosterHasChars;
    public bool HasSelectedRoster;
    public bool ShowNewCharacterPrompt;
    public CharSelectInfo? Info;
    public int HoveredRace = -1;
    public int JobCount;
    public CharSelectJobView[] Jobs = new CharSelectJobView[Math.Max(CharacterSelectArt.TempskronLineup.Count, CharacterSelectArt.MorionLineup.Count)];
    public int FaceIndex;
    public int HoveredFace = -1;
    public PlayerClass FaceClass;
    public CharacterBrood FaceBrood;
    public string NameDisplay = string.Empty;
    public bool CaretVisible;
    public bool ShowFacePortrait;
}

// The desktop CharacterSelectView: the same art at the same places of the viewport, drawn in the same order. The
// viewport is the virtual one of CharSelectScreen, so every number here is the desktop's.
internal sealed class CharSelectView(CharSelectGraphics graphics, CharSelectCamera camera, CharSelectScene scene)
{
    // The desktop UI font's em in pixels: 21 points at 96 dots an inch.
    private const float FontEm = 21f * 96f / 72f;
    private const int InfoPanelValueColumnX = 102;
    private const float InfoPanelValueScale = 0.45f;
    private const float InfoPanelValueBaselineAdjustment = -2f;

    private static readonly Vector4 White = Vector4.One;
    private static readonly Vector4 Gold = new(1f, 215f / 255f, 0f, 1f);
    private static readonly Vector4 Bar = new(0f, 0f, 0f, 150f / 255f);
    private static readonly Vector4 NameColor = new(1f, 250f / 255f, 235f / 255f, 1f);
    private static readonly Vector4 LevelColor = new(1f, 220f / 255f, 100f / 255f, 1f);
    private static readonly Vector4 ClanColor = new(230f / 255f, 240f / 255f, 1f, 1f);

    private int _levelShown = -1;
    private string _levelText = "";
    private string _nameShown = "";
    private string _nameWithCaret = "_";
    private PlayerClass _facesClass;
    private FaceThumbnails? _faces;

    public void Draw(int width, int height, CharSelectViewModel m)
    {
        graphics.Begin2D();
        DrawFrame(width, height);
        DrawTitle(width, m.StateIndex);
        switch (m.StateIndex)
        {
            case CharacterSelectStage.RosterStep:
                DrawCharacterSelect(width, height, m);
                break;
            case CharacterSelectStage.TribeStep:
                DrawRaceSelect(width, height, m);
                break;
            case CharacterSelectStage.ClassStep:
                DrawJobSelect(width, height, m);
                break;
            default:
                DrawFaceSelect(width, height, m);
                break;
        }

        if (m.ErrorMessage.Length > 0)
        {
            graphics.Text(m.ErrorMessage, width / 2f - 128f, height / 2f + height / 6f, FontEm, Gold);
        }
    }

    private void DrawFrame(int w, int h)
    {
        graphics.Fill(0, 0, w, 70, Bar);
        graphics.Fill(0, h - 50, w, 70, Bar);
        var top = Art(CharacterSelectArt.FrameTop);
        var bottom = Art(CharacterSelectArt.FrameBottom);
        for (var x = 4; x < w; x += 64)
        {
            graphics.Sprite(top, x, 68, 64, 8);
            graphics.Sprite(bottom, x, h - 54, 64, 8);
        }

        var left = Art(CharacterSelectArt.FrameLeft);
        var right = Art(CharacterSelectArt.FrameRight);
        for (var y = 70; y < h; y += 64)
        {
            graphics.Sprite(left, 0, y, 8, 64);
            graphics.Sprite(right, w - 8, y, 8, 64);
        }

        graphics.Sprite(Art(CharacterSelectArt.FrameCorner1), 0, 68, 130, 64);
        graphics.Sprite(Art(CharacterSelectArt.FrameCorner2), w - 130, 68, 130, 64);
        graphics.Sprite(Art(CharacterSelectArt.FrameCorner3), 0, h - (64 + 48), 130, 64);
        graphics.Sprite(Art(CharacterSelectArt.FrameCorner4), w - 130, h - (64 + 48), 130, 64);
    }

    private void DrawTitle(int width, int stateIndex)
    {
        graphics.Sprite(Art(CharacterSelectArt.TitleBox), width / 2 - 128, 0, 256, 128);
        var row = Math.Min(stateIndex, CharacterSelectStage.FaceStep);
        graphics.Sprite(Art(CharacterSelectArt.TitleText), width / 2 - 64, 53, 128, 30, 0, row * 30, 128, 30, White);
    }

    private void DrawCharacterSelect(int width, int height, CharSelectViewModel m)
    {
        var y = CharacterSelectLayout.ButtonRowY(height);
        graphics.Sprite(Art(m.RosterHasChars ? CharacterSelectArt.SelectButton : CharacterSelectArt.NewCharacterButton),
            CharacterSelectLayout.TwoButtonPrimaryX(width), y);
        graphics.Sprite(Art(CharacterSelectArt.ExitButton), CharacterSelectLayout.TwoButtonQuitX(width), y);
        if (m.HasSelectedRoster)
        {
            graphics.Sprite(Art(CharacterSelectArt.DeleteButton), CharacterSelectLayout.TwoButtonDeleteX, y);
        }

        if (m.ShowNewCharacterPrompt)
        {
            var opacity = m.RosterHasChars ? 1f : 0.5f + 0.5f * (0.5f + 0.5f * MathF.Sin((float)m.ElapsedSeconds * 3f));
            graphics.Sprite(Art(CharacterSelectArt.NewCharacterPrompt), CharacterSelectLayout.NewCharPrompt(width), opacity);
        }

        if (m.Info is { } info)
        {
            DrawInfoPanel(width, height, info);
        }
    }

    private void DrawRaceSelect(int width, int height, CharSelectViewModel model)
    {
        var temp = CharacterSelectLayout.TempskronMark(width, height);
        var mor = CharacterSelectLayout.MorionMark(width, height);
        DrawRaceMark(temp, Art(CharacterSelectArt.TempskronMark), Art(CharacterSelectArt.TempskronTitle));
        DrawRaceMark(mor, Art(CharacterSelectArt.MorionMark), Art(CharacterSelectArt.MorionTitle));
        if (model.HoveredRace == 0)
        {
            DrawRaceDescription(temp.X - 175, temp.Y + 150, Art(CharacterSelectArt.TempskronDescription));
        }
        else if (model.HoveredRace == 1)
        {
            DrawRaceDescription(mor.X, mor.Y + 150, Art(CharacterSelectArt.MorionDescription));
        }

        DrawThreeButtons(width, height);
    }

    private void DrawJobSelect(int width, int height, CharSelectViewModel m)
    {
        for (var index = 0; index < m.JobCount; index++)
        {
            var job = m.Jobs[index];
            var art = CharacterSelectArt.Classes[job.Class];
            var head = camera.Project(job.HeadWorld, width, height);
            graphics.Sprite(Art(job.Hovered || job.Selected ? art.SelectedLabel : art.NormalLabel), head.X - 64f, head.Y - 30f);
            if (job.Hovered && !job.Selected)
            {
                var x = width / 2 - 140;
                var y = height / 5;
                graphics.Sprite(Art(CharacterSelectArt.ClassBoxMain), x, y, 256, 256);
                graphics.Sprite(Art(CharacterSelectArt.ClassBoxEnd), x + 256, y, 32, 256);
                graphics.Sprite(Art(art.Description), x + 10, y + 4);
            }
        }

        DrawThreeButtons(width, height);
    }

    private void DrawFaceSelect(int width, int height, CharSelectViewModel m)
    {
        graphics.Sprite(Art(CharacterSelectArt.FaceBackdropTop), CharacterSelectLayout.FaceBackdropTop(width, height));
        graphics.Sprite(Art(CharacterSelectArt.FaceBackdropBottom), CharacterSelectLayout.FaceBackdropBottom(width, height));
        if (m.ShowFacePortrait)
        {
            var portrait = CharacterSelectLayout.FacePortrait(width, height);
            graphics.BeginInset(portrait);
            scene.DrawFacePortrait(graphics, portrait.Width / (float)portrait.Height);
            graphics.Begin2D();
        }

        if (_faces is null || m.FaceClass != _facesClass)
        {
            (_facesClass, _faces) = (m.FaceClass, CharacterSelectArt.Faces(m.FaceClass));
        }

        var faces = _faces;
        for (var i = 0; i < CharacterFace.FaceCount; i++)
        {
            var rect = CharacterSelectLayout.FaceThumbDraw(width, height, i);
            var selected = i == m.FaceIndex;
            var hovered = i == m.HoveredFace;
            var opacity = selected ? 1f : hovered ? 0.78f : 0.59f;
            var boxOpacity = !selected && hovered ? 0.71f : 1f;
            graphics.Sprite(Art(selected || hovered ? CharacterSelectArt.FaceBoxSelected : CharacterSelectArt.FaceBoxNormal), rect, boxOpacity);
            var yOffset = m.FaceBrood == CharacterBrood.Tempskron ? 4 : 0;
            graphics.Sprite(Art(selected ? faces.Selected[i] : faces.Normal[i]), rect.X, rect.Y + yOffset, rect.Width, rect.Height, opacity);
        }

        var nameBoxX = width / 2 - 95;
        var nameY = CharacterSelectLayout.FaceNameDecorationY(height);
        graphics.Sprite(Art(CharacterSelectArt.FaceNameBox), nameBoxX, nameY, 64, 16);
        graphics.Sprite(Art(CharacterSelectArt.FaceNameBox), nameBoxX + 128, nameY, 64, 16);
        if (m.NameDisplay != _nameShown)
        {
            (_nameShown, _nameWithCaret) = (m.NameDisplay, m.NameDisplay + "_");
        }

        var (inputX, inputY) = CharacterSelectLayout.FaceInputPosition(width, height);
        SmallValue(m.CaretVisible ? _nameWithCaret : _nameShown, inputX, inputY, White);
        DrawThreeButtons(width, height);
    }

    private void DrawThreeButtons(int width, int height)
    {
        var y = CharacterSelectLayout.ButtonRowY(height);
        graphics.Sprite(Art(CharacterSelectArt.SelectButton), CharacterSelectLayout.ThreeButtonOkX(width), y);
        graphics.Sprite(Art(CharacterSelectArt.BackButton), CharacterSelectLayout.ThreeButtonBackX(width), y);
        graphics.Sprite(Art(CharacterSelectArt.ExitButton), CharacterSelectLayout.ThreeButtonQuitX(width), y);
    }

    private void DrawInfoPanel(int width, int height, CharSelectInfo info)
    {
        var boxX = width / 2 - 128;
        var boxY = height / 2 + height / 5;
        graphics.Sprite(Art(CharacterSelectArt.InfoBackPanel), boxX, boxY, 256, 128);
        // The overlay's top 33 rows hold the clan line: a character in a clan shows them and its rows move 20 px down.
        var inClan = info.ClanName.Length > 0;
        if (inClan)
        {
            graphics.Sprite(Art(CharacterSelectArt.InfoOverlay), boxX, boxY, 256, 128);
        }
        else
        {
            graphics.Sprite(Art(CharacterSelectArt.InfoOverlay), boxX, boxY + 13, 256, 95, 0, 33, 256, 95, White);
        }

        var valueX = boxX + InfoPanelValueColumnX;
        var top = boxY + (inClan ? 20 : 0) + InfoPanelValueBaselineAdjustment;
        if (inClan)
        {
            SmallValue(info.ClanName, valueX + 17, top, ClanColor);
        }

        SmallValue(info.Name, valueX, top + 20, NameColor);
        if (info.Level != _levelShown)
        {
            (_levelShown, _levelText) = (info.Level, info.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        SmallValue(_levelText, valueX, top + 40, LevelColor);
        SmallValue(info.BroodName, valueX, top + 60, NameColor);
        SmallValue(info.ClassName, valueX, top + 80, NameColor);
    }

    private void SmallValue(string text, float x, float y, Vector4 color) => graphics.Text(text, x, y, FontEm * InfoPanelValueScale, color);

    private void DrawRaceMark(ScreenRect rectangle, GlTexture mark, GlTexture logo)
    {
        graphics.Sprite(mark, rectangle);
        graphics.Sprite(logo, rectangle.X - 6, rectangle.Y + 118, 128, 32);
    }

    private void DrawRaceDescription(int x, int y, GlTexture description)
    {
        graphics.Sprite(Art(CharacterSelectArt.TribeBoxMain), x, y, 256, 256);
        graphics.Sprite(Art(CharacterSelectArt.TribeBoxEnd), x + 256, y, 32, 256);
        graphics.Sprite(description, x + 19, y + 12);
    }

    private GlTexture Art(string name) => graphics.Art(name);
}
