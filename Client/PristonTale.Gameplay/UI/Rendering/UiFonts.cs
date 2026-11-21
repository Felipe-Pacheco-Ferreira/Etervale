using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI.Rendering;

// The interface font of the shown language: the Latin font (Fonts/UI) for the Latin-script languages, or a language's
// own font (Fonts/UI-<code>, tools/ui/build_font.py --language) for a script the Latin font lacks. Every text drawer
// reads Current, so a change of language changes the font everywhere. A language's font loads the first time it is
// asked for; one the content lacks (the browser fetches only the starting language's) falls back to the Latin font.
public sealed class UiFonts
{
    public const string LatinAsset = "Fonts/UI";

    // The languages drawn with a font of their own.
    public static readonly IReadOnlyList<string> OwnFontLanguages = [ClientSettings.SimplifiedChineseLanguage, ClientSettings.JapaneseLanguage];

    private readonly ContentManager _content;
    private readonly Dictionary<string, SpriteFont> _loaded = new(StringComparer.Ordinal);

    public UiFonts(ContentManager content)
    {
        _content = content;
        Latin = content.Load<SpriteFont>(LatinAsset);
        Current = Latin;
    }

    public SpriteFont Latin { get; }

    public SpriteFont Current { get; private set; }

    public static bool HasOwnFont(string language) => OwnFontLanguages.Contains(language);

    public static string AssetOf(string language) => HasOwnFont(language) ? LatinAsset + "-" + language : LatinAsset;

    // The font a language draws with; called every frame, so it allocates nothing once the font is loaded.
    public SpriteFont For(string language)
    {
        if (!HasOwnFont(language))
        {
            return Latin;
        }

        if (!_loaded.TryGetValue(language, out var font))
        {
            try
            {
                font = _content.Load<SpriteFont>(AssetOf(language));
            }
            catch (Exception error) when (error is ContentLoadException or IOException)
            {
                font = Latin;
            }

            _loaded[language] = font;
        }

        return font;
    }

    public void Use(string language) => Current = For(language);
}
