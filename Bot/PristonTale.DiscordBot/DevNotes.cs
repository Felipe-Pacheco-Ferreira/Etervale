using System.Globalization;

namespace PristonTale.DiscordBot;

// One development note of the devnotes folder: a folder per note, named yyyy-MM-dd-subject, holding nota.md and up to
// four images. nota.md starts with "# Title"; its first paragraph is the summary and the rest are the body's paragraphs.
// A line "Tipo: melhoria" marks an improvement (Discord's development channel only, not the news) and "Tipo: equipe" a
// staff tool (the staff's development channel only); a note without one is a new system.
public sealed record DevNote(string Id, DateOnly Date, string Title, string Summary, IReadOnlyList<string> Body, IReadOnlyList<string> Images)
{
    public const string FileName = "nota.md";
    private const string KindPrefix = "Tipo:";

    private static readonly Dictionary<string, DevNoteKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["melhoria"] = DevNoteKind.Improvement,
        ["equipe"] = DevNoteKind.Staff
    };

    public DevNoteKind Kind { get; init; }
    public const int MostImages = 4;
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];

    // Every note under the folder, oldest first; a folder without a nota.md or a title is passed over.
    public static IReadOnlyList<DevNote> ReadAll(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var notes = new List<DevNote>();
        foreach (var directory in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(directory, FileName);
            if (File.Exists(file) && Parse(Path.GetFileName(directory), File.ReadAllText(file), File.GetLastWriteTimeUtc(file)) is { } note)
            {
                notes.Add(note with
                {
                    Images = Directory.EnumerateFiles(directory)
                        .Where(static path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                        .Order(StringComparer.Ordinal)
                        .Take(MostImages)
                        .ToArray()
                });
            }
        }

        return notes;
    }

    public static DevNote? Parse(string id, string text, DateTime writtenUtc)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var titleLine = Array.FindIndex(lines, static line => line.Trim().Length > 0);
        if (titleLine < 0 || !lines[titleLine].TrimStart().StartsWith("# ", StringComparison.Ordinal))
        {
            return null;
        }

        var paragraphs = new List<string>();
        var current = new List<string>();
        var kind = DevNoteKind.System;
        foreach (var line in lines.Skip(titleLine + 1).Select(static line => line.Trim()))
        {
            if (line.StartsWith(KindPrefix, StringComparison.OrdinalIgnoreCase) && Kinds.TryGetValue(line[KindPrefix.Length..].Trim(), out var marked))
            {
                kind = marked;
            }
            else if (line.Length == 0)
            {
                Flush();
            }
            else
            {
                current.Add(line);
            }
        }

        Flush();
        var date = id.Length >= 10 && DateOnly.TryParseExact(id[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var named)
            ? named
            : DateOnly.FromDateTime(writtenUtc);
        return new DevNote(id, date, lines[titleLine].Trim()[2..].Trim(), paragraphs.FirstOrDefault() ?? "", paragraphs.Skip(1).ToArray(), [])
        {
            Kind = kind
        };

        void Flush()
        {
            if (current.Count > 0)
            {
                paragraphs.Add(string.Join("\n", current));
                current.Clear();
            }
        }
    }

    // The note as Discord shows it: the summary and the paragraphs.
    public string Text => string.Join("\n\n", Body.Prepend(Summary));
}

// Where a note goes: a new system everywhere (development channel and news), an improvement to the development channel,
// a staff tool to the staff's development channel.
public enum DevNoteKind
{
    System,
    Improvement,
    Staff
}
