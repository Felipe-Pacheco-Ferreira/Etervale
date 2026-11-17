using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.Configuration;
using PristonTale.Client.Net;
using PristonTale.Contracts.Characters;
using PristonTale.Server.Net;

namespace PristonTale.Tests;

// The game server's WebSocket edge for the browser client: the same session over another transport, its origin check
// and its settings.
internal static class WebSocketEdgeTests
{
    private const string AllowedOrigin = "https://play.example";

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("websocket: a client logs in and lists its characters over the WebSocket edge; the edge is off by default", LoginAsync);
        yield return ("websocket: a browser ticket is account-bound, expires and can be redeemed only once", BrowserTicketAsync);
        yield return ("websocket: the edge refuses pages of other origins, requests off its path and connections past the per-address cap until one closes",
            RefusalsAsync);
        yield return ("websocket: edge settings out of range, a plain listener off loopback or an origin browsers never send stop the start", SettingsAsync);
    }

    private static (string, string)[] EdgeSettings(params (string, string)[] more) =>
    [
        ("Network:WebSocket:Enabled", "true"),
        ("Network:WebSocket:Url", "http://127.0.0.1:0"),
        ("Network:WebSocket:AllowedOrigins:0", AllowedOrigin),
        .. more
    ];

    private static async Task LoginAsync()
    {
        await using (var plain = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith()))
        {
            Assert.True(plain.WebSocketAddress is null, "the WebSocket edge listened without being enabled");
        }

        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith(), EdgeSettings());
        var address = server.WebSocketAddress ?? throw new InvalidOperationException("the enabled edge reported no address");
        Assert.True(address.Scheme == "ws" && address.AbsolutePath == WebSocketEdgeSettings.DefaultPath, $"the edge's address {address}");
        var account = await server.CreatePlayerAsync(new PlayerSetup("Surfer", PlayerClass.Archer, 12, MultiplayerServer.HuntingField, MultiplayerServer.Camp));
        await using var connection = new GameServerConnection(address.AbsoluteUri);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var outcome = await connection.LoginAsync(account, MultiplayerServer.Password, "en", timeout.Token);
        var characters = await connection.ListCharactersAsync(timeout.Token);
        Assert.True(outcome.Ok && characters.Any(static character => character is { Name: "Surfer", Level: 12 }),
            $"the WebSocket login: {outcome.Message}, {characters.Count} characters");
    }

    private static async Task BrowserTicketAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith(), EdgeSettings());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        var account = await server.CreateAccountAsync("Resume");
        var other = await server.CreateAccountAsync("Other");
        var ticket = await server.IssueBrowserTicketAsync(account, TimeSpan.FromMinutes(1), ct);
        var expired = await server.IssueBrowserTicketAsync(account, TimeSpan.FromSeconds(-1), ct);
        var endpoint = server.WebSocketAddress!.AbsoluteUri;

        async Task<bool> LoginWithAsync(string name, string secret)
        {
            await using var connection = new GameServerConnection(endpoint);
            return (await connection.LoginAsync(name, secret, "en", ct)).Ok;
        }

        Assert.True(!await LoginWithAsync(other, ticket), "another account redeemed the ticket");
        Assert.True(!await LoginWithAsync(account, expired), "an expired ticket opened a session");
        Assert.True(await LoginWithAsync(account, ticket), "the ticket did not open a session");
        Assert.True(!await LoginWithAsync(account, ticket), "a consumed ticket opened another session");
    }

    private static async Task RefusalsAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith(), EdgeSettings(("Network:MaxConnectionsPerIp", "1")));
        var address = server.WebSocketAddress!;
        Assert.True(!await OpensAsync(address, "https://evil.example"), "a page of another origin opened the socket");
        using (var http = new HttpClient())
        {
            var offPath = new UriBuilder(address) { Scheme = "http", Path = "/other" }.Uri;
            Assert.True((await http.GetAsync(offPath)).StatusCode == HttpStatusCode.NotFound, "a request off the edge's path was answered");
        }

        using var first = new ClientWebSocket();
        first.Options.SetRequestHeader("Origin", AllowedOrigin);
        await first.ConnectAsync(address, CancellationToken.None);
        Assert.True(!await OpensAsync(address, AllowedOrigin), "a second connection from the address passed its cap");
        first.Abort();
        var reopened = false;
        for (var attempt = 0; attempt < 50 && !reopened; attempt++)
        {
            reopened = await OpensAsync(address, AllowedOrigin);
        }

        Assert.True(reopened, "a closed WebSocket session kept its address's connection slot");
    }

    // Whether a WebSocket from the given page origin completes its upgrade.
    private static async Task<bool> OpensAsync(Uri address, string origin)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", origin);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.ConnectAsync(address, timeout.Token);
            return true;
        }
        catch (WebSocketException)
        {
            return false;
        }
    }

    private static Task SettingsAsync()
    {
        static WebSocketEdgeSettings Read(params (string Key, string Value)[] values) => WebSocketEdgeSettings.From(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build());

        Assert.True(!Read().Enabled && Read(("Network:WebSocket:Url", "nonsense")) is { Enabled: false }, "a disabled edge reads no further");
        foreach (var (key, value) in new[]
        {
            ("Network:WebSocket:Url", "ftp://127.0.0.1:10010"),
            ("Network:WebSocket:Url", "http://0.0.0.0:10010"),
            ("Network:WebSocket:Path", "game"),
            ("Network:WebSocket:AllowedOrigins:0", "https://site.example/page"),
            ("Network:WebSocket:AllowedOrigins:0", "https://site.example:443"),
            ("Network:WebSocket:AllowedOrigins:0", "https://user@site.example"),
            ("Network:WebSocket:KnownProxies:0", "proxy.example")
        })
        {
            Assert.Throws<InvalidOperationException>(() => Read(("Network:WebSocket:Enabled", "true"), (key, value)), $"refused: {key} = {value}");
        }

        Assert.True(Read(("Network:WebSocket:Enabled", "true")).AllowsOrigin(null) && !Read(("Network:WebSocket:Enabled", "true")).AllowsOrigin(AllowedOrigin),
            "an edge with no allowed origins admits clients without one and refuses every page");
        return Task.CompletedTask;
    }
}
