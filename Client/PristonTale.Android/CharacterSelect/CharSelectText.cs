using System.Globalization;
using System.Resources;
using PristonTale.Contracts.Texts;

namespace PristonTale.Mobile.CharacterSelect;

// The desktop client's Brazilian Portuguese texts (ClientText.pt-BR.resx, linked into this assembly by the project
// file), read by the desktop's keys.
internal static class CharSelectText
{
    private static readonly ResourceManager Resources = new("PristonTale.Mobile.ClientTextPtBr", typeof(CharSelectText).Assembly);

    public static string Format(GameText text) => text.IsNone ? ""
        : string.Format(CultureInfo.InvariantCulture, Get("Game." + text.Id), [.. text.Args]);

    public static string Get(string key)
    {
        try
        {
            return Resources.GetString(key, CultureInfo.InvariantCulture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }
}
