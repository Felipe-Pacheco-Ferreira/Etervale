using System.Text.RegularExpressions;

namespace PristonTale.Web.Services;

public enum TermsBlockKind
{
    Title,
    Subtitle,
    Notice,
    Heading,
    Paragraph
}

public readonly record struct TermsBlock(TermsBlockKind Kind, string Text);

// The rules and terms of use (content/termos.txt) the registration shows and asks the player to accept. The file is
// plain text, one block per line: the title, the game's name, the notice that starts with "LEITURA IMPORTANTE.", the
// numbered section headings in capitals ("1. OBJETO, ...") and the clauses ("1.1. ...").
public sealed partial class TermsOfUse(IReadOnlyList<TermsBlock> blocks)
{
    public static readonly string FileName = Path.Combine("content", "termos.txt");

    public IReadOnlyList<TermsBlock> Blocks { get; } = blocks;

    public static TermsOfUse Load(string path)
    {
        if (!File.Exists(path))
        {
            return new TermsOfUse([]);
        }

        var blocks = new List<TermsBlock>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var kind = blocks.Count switch
            {
                0 => TermsBlockKind.Title,
                1 => TermsBlockKind.Subtitle,
                _ when line.StartsWith("LEITURA IMPORTANTE.", StringComparison.Ordinal) => TermsBlockKind.Notice,
                _ when SectionHeading().IsMatch(line) => TermsBlockKind.Heading,
                _ => TermsBlockKind.Paragraph
            };
            blocks.Add(new TermsBlock(kind, line));
        }

        return new TermsOfUse(blocks);
    }

    // "12. BLESS CASTLE": a number, a dot and a title with no lower-case letter.
    [GeneratedRegex(@"^\d+\.\s+[^a-zà-ÿ]+$")]
    private static partial Regex SectionHeading();
}
