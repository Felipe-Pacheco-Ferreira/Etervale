using System.Text.Json;

namespace PristonTale.Web.Services;

// One news post: plain text only (the pages encode it), a paragraph per line of Body.
public sealed record NewsItem(DateOnly Date, string Title, string Summary, IReadOnlyList<string> Body);

// The news of content/news.json beside the executable, newest first. The file is read again when it changes, so a post
// is published by editing it, without a restart; a file that does not parse keeps the posts read before and is logged.
public sealed class NewsFeed(string path)
{
    public static readonly string FileName = Path.Combine("content", "news.json");
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { AllowTrailingCommas = true };
    private readonly object _gate = new();
    private DateTime _readAt;
    private IReadOnlyList<NewsItem> _items = [];

    public static NewsFeed Load(string path) => new(path);

    public IReadOnlyList<NewsItem> Items(ILogger logger)
    {
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        lock (_gate)
        {
            if (written != _readAt)
            {
                _readAt = written;
                _items = Read(logger);
            }

            return _items;
        }
    }

    private IReadOnlyList<NewsItem> Read(ILogger logger)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<NewsItem>>(File.ReadAllText(path), Options) ?? [];
            items.RemoveAll(static item => string.IsNullOrWhiteSpace(item.Title));
            for (var index = 0; index < items.Count; index++)
            {
                items[index] = items[index] with { Summary = items[index].Summary ?? "", Body = items[index].Body ?? [] };
            }

            items.Sort(static (a, b) => b.Date.CompareTo(a.Date));
            return items;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The news file {Path} does not parse; keeping the posts read before", path);
            return _items;
        }
    }
}
