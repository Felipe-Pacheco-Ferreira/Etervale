using System.Text;
using System.Xml.Linq;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Tests;

// The interface fonts (Content/Fonts/UI.spritefont and a language's own UI-<code>.spritefont, UiFonts,
// tools/ui/build_font.py) bake only their character regions: every character a language's texts use (its client phrases
// and data texts, its text files and its world catalog) must be among its font's, or it would draw as the default
// character.
internal static class FontCoverageTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("every language's font bakes every character of that language's texts", CoverageAsync);
    }

    private static async Task CoverageAsync()
    {
        var root = LocalizationTests.RepositoryRoot();
        var fonts = new Dictionary<string, (int Start, int End)[]>();
        (int Start, int End)[] FontOf(string language)
        {
            var asset = UiFonts.AssetOf(language);
            if (!fonts.TryGetValue(asset, out var regions))
            {
                // A region of the space character is a whitespace-only element, which only a whitespace-preserving load keeps.
                var font = XDocument.Load(Path.Combine(root, "Client", "PristonTale", "Content", asset + ".spritefont"), LoadOptions.PreserveWhitespace);
                regions = [.. font.Descendants("CharacterRegion").Select(region => ((int)region.Element("Start")!.Value[0], (int)region.Element("End")!.Value[0]))];
                fonts[asset] = regions;
            }

            return regions;
        }

        var missing = new SortedDictionary<int, string>();
        void Check(string text, string language, string source)
        {
            var regions = FontOf(language);
            foreach (var rune in text.EnumerateRunes())
            {
                if (rune.Value >= 32 && rune.Value != 0xFEFF && !regions.Any(region => rune.Value >= region.Start && rune.Value <= region.End))
                {
                    missing.TryAdd(rune.Value, $"{language}: {source}");
                }
            }
        }

        var localization = Path.Combine(root, "Client", "PristonTale.Gameplay", "Localization");
        foreach (var resx in Directory.EnumerateFiles(localization, "*.resx"))
        {
            var name = Path.GetFileNameWithoutExtension(resx);
            var language = name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : ClientSettings.EnglishLanguage;
            foreach (var data in XDocument.Load(resx).Descendants("data"))
            {
                Check(data.Element("value")!.Value, language, Path.GetFileName(resx) + " " + data.Attribute("name")!.Value);
            }
        }

        foreach (var folder in Directory.EnumerateDirectories(Path.Combine(root, "Client", "PristonTale", "Texts")))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                Check(await File.ReadAllTextAsync(file), Path.GetFileName(folder), Path.GetRelativePath(folder, file));
            }
        }

        foreach (var json in Directory.EnumerateFiles(Path.Combine(root, "tools", "PristonTale.WorldImport", "Translations"), "*.json"))
        {
            Check(await File.ReadAllTextAsync(json), Path.GetFileNameWithoutExtension(json), Path.GetFileName(json));
        }

        Assert.True(missing.Count == 0, "a font lacks " + string.Join(", ", missing.Take(12).Select(pair => $"U+{pair.Key:X4} ({pair.Value})")));
    }
}
