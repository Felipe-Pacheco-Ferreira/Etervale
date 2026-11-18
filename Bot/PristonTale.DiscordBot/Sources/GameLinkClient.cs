using System.Net.Http.Headers;
using System.Net.Http.Json;
using PristonTale.Contracts.Social;

namespace PristonTale.DiscordBot.Sources;

// The game server's bot link (BotLinkService): Discord lines for the trade channel and remote game-master commands.
public sealed class GameLinkClient(HttpClient http, GameLinkSettings settings)
{
    public bool Enabled => settings.Url.Length > 0;

    public async Task<bool> SayAsync(string author, string text, CancellationToken ct)
    {
        using var request = Request(BotLinkPaths.Chat, new BotChatRequest(author, text));
        using var response = await http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<BotCommandResponse> CommandAsync(BotCommandRequest command, CancellationToken ct)
    {
        using var request = Request(BotLinkPaths.Command, command);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BotCommandResponse>(ct) ?? new BotCommandResponse("Unknown", []);
    }

    private HttpRequestMessage Request<T>(string path, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.Url), path)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Key);
        return request;
    }
}
