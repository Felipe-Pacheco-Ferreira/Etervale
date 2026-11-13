using System.Globalization;
using System.Xml.Linq;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;

namespace PristonTale.Web.Services;

// The classes' titles in Brazilian Portuguese, read from the game client's data texts (keys Class.<PlayerClass>.<tier>);
// a title the file lacks is the class profile's English one.
public sealed class ClassTitles(IReadOnlyDictionary<string, string> titles)
{
    public static readonly string FileName = Path.Combine("content", "class-titles.pt-BR.resx");
    private const string KeyPrefix = "Class.";

    public static IReadOnlyList<PlayerClass> All { get; } = Enum.GetValues<PlayerClass>();

    public static ClassTitles Load(string path)
    {
        var document = XDocument.Load(path);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in document.Root?.Elements("data") ?? [])
        {
            var name = (string?)data.Attribute("name");
            var value = (string?)data.Element("value");
            if (name is not null && value is not null && name.StartsWith(KeyPrefix, StringComparison.Ordinal))
            {
                entries[name] = value;
            }
        }

        return new ClassTitles(entries);
    }

    public string Of(PlayerClass playerClass, int tier)
    {
        var english = ClassProfiles.Of(playerClass).Titles;
        var index = Math.Clamp(tier, 0, english.Count - 1);
        var key = KeyPrefix + playerClass + "." + index.ToString(CultureInfo.InvariantCulture);
        return titles.TryGetValue(key, out var title) ? title : english[index];
    }

    // A stored job code; a code no class has shows as a dash.
    public string Of(int jobCode, int tier) => Enum.IsDefined((PlayerClass)jobCode) ? Of((PlayerClass)jobCode, tier) : "—";

    // The address form of a class in the rankings' filter (?classe=...).
    public static string Slug(PlayerClass playerClass) => playerClass.ToString().ToLowerInvariant();

    public static PlayerClass? FromSlug(string? slug)
    {
        foreach (var playerClass in All)
        {
            if (string.Equals(Slug(playerClass), slug, StringComparison.Ordinal))
            {
                return playerClass;
            }
        }

        return null;
    }
}
