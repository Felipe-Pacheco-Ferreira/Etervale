using System.Net.Http.Json;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot.Sources;

// A world the site offers (/api/worlds) and the address of its edge's /status.
public sealed record WorldInfo(string Name, string Region, string Kind, string Endpoint, string Status);

// What a world's /status answered: players in the world and the connection cap (0 when uncapped).
public sealed record WorldLoad(int Players, int Capacity);

// A world and its load; Load is null when the world did not answer.
public sealed record WorldReport(WorldInfo World, WorldLoad? Load)
{
    public bool Online => Load is not null;
}

// The website's public JSON: the world list, each world's load and the news posts.
public sealed class SiteClient(HttpClient http)
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<WorldReport>> WorldsAsync(CancellationToken ct)
    {
        var worlds = await http.GetFromJsonAsync<WorldInfo[]>("/api/worlds", ct) ?? [];
        var reports = new WorldReport[worlds.Length];
        for (var index = 0; index < worlds.Length; index++)
        {
            reports[index] = new WorldReport(worlds[index], await LoadAsync(worlds[index], ct));
        }

        return reports;
    }

    public async Task<IReadOnlyList<NewsItem>> NewsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<NewsItem[]>("/api/client/news", ct) ?? [];

    private async Task<WorldLoad?> LoadAsync(WorldInfo world, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(StatusTimeout);
        try
        {
            return await http.GetFromJsonAsync<WorldLoad>(world.Status, timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            || (exception is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }
}
