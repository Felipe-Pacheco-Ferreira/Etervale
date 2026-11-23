using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PristonTale.Contracts.Characters;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Discord;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Tests;

internal static class DiscordPresenceTests
{
    private const string ApplicationId = "1234567890123456789";

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("discord presence: the activity shows the nick and level, the class title and field, the time in the world and the class emblem", ActivityAsync);
        yield return ("discord presence: a change of level, field, language or switch sends a new activity and the timer restarts at each world entry", TrackerAsync);
        yield return ("discord presence: IPC frames carry a little-endian opcode and length, and the handshake and SET_ACTIVITY bodies are Discord's", FramesAsync);
        yield return ("discord presence: the worker handshakes, sends the activity, answers a ping and refuses a non-numeric application id", WorkerAsync);
    }

    private static Task ActivityAsync()
    {
        try
        {
            ClientText.Use(ClientSettings.BrazilianPortugueseLanguage);
            var title = DataText.ClassTitle(PlayerClass.Archer, 3);
            var world = DiscordActivity.For(PlayingStatus.InField("SatroX", PlayerClass.Archer, 3, 71, "Ricarten"), 1_700_000_000);
            Assert.True(world.Details == "SatroX · Nível 71", $"details were {world.Details}");
            Assert.True(world.State == title + " · Ricarten" && world.StartUnixSeconds == 1_700_000_000, $"state {world.State}, start {world.StartUnixSeconds}");
            Assert.True(world.LargeImage == DiscordActivity.LogoImage && world.LargeText == DiscordActivity.GameName, "the large image is not the game's logo");
            Assert.True(world.SmallImage == "class_archer" && world.SmallText == title, $"small image {world.SmallImage} ({world.SmallText})");
            Assert.True(DiscordActivity.ClassImage(PlayerClass.MartialArtist) == "class_martialartist", "a class image key is not lower case");

            var menu = DiscordActivity.For(PlayingStatus.Menu, null);
            Assert.True(menu.Details == "No menu" && menu.State is null && menu.StartUnixSeconds is null && menu.SmallImage is null,
                $"the menu shows {menu}");

            var unnamed = DiscordActivity.For(PlayingStatus.InField("SatroX", PlayerClass.Knight, 0, 1, ""), 1);
            Assert.True(unnamed.State == DataText.ClassTitle(PlayerClass.Knight, 0), $"a field without a name left the state {unnamed.State}");
        }
        finally
        {
            ClientText.Use(ClientSettings.EnglishLanguage);
        }

        return Task.CompletedTask;
    }

    private static Task TrackerAsync()
    {
        var tracker = new DiscordPresenceTracker();
        var en = ClientSettings.EnglishLanguage;
        var ricarten = PlayingStatus.InField("SatroX", PlayerClass.Priestess, 1, 40, "Ricarten");

        Assert.True(!tracker.TryChange(false, ApplicationId, en, PlayingStatus.Menu, 100, out _), "a disabled presence sent something");
        Assert.True(!tracker.TryChange(true, "", en, PlayingStatus.Menu, 100, out _), "a presence without an application sent something");
        Assert.True(tracker.TryChange(true, ApplicationId, en, PlayingStatus.Menu, 100, out var menu) && menu.Activity.StartUnixSeconds is null,
            "switching the presence on did not send the menu");
        Assert.True(!tracker.TryChange(true, ApplicationId, en, PlayingStatus.Menu, 101, out _), "an unchanged menu was sent again");

        Assert.True(tracker.TryChange(true, ApplicationId, en, ricarten, 200, out var entered) && entered.Activity.StartUnixSeconds == 200,
            "entering the world did not start the timer");
        Assert.True(!tracker.TryChange(true, ApplicationId, en, ricarten, 260, out _), "an unchanged world status was sent again");
        Assert.True(tracker.TryChange(true, ApplicationId, en, ricarten with { Level = 41 }, 300, out var levelled)
            && levelled.Activity.Details == "SatroX · Level 41" && levelled.Activity.State == DataText.ClassTitle(PlayerClass.Priestess, 1) + " · Ricarten" && levelled.Activity.StartUnixSeconds == 200,
            "a level up did not update the details or restarted the timer");
        Assert.True(tracker.TryChange(true, ApplicationId, en, ricarten with { Level = 41, FieldName = "Pillai" }, 320, out var moved)
            && moved.Activity.State.EndsWith(" · Pillai", StringComparison.Ordinal), "a field change was not sent");
        Assert.True(tracker.TryChange(true, ApplicationId, ClientSettings.BrazilianPortugueseLanguage, ricarten with { Level = 41, FieldName = "Pillai" }, 330, out _),
            "a language change was not sent");

        Assert.True(tracker.TryChange(true, ApplicationId, en, PlayingStatus.Menu, 400, out var lobby) && lobby.Activity.StartUnixSeconds is null,
            "leaving for the character selection did not show the menu");
        Assert.True(tracker.TryChange(true, ApplicationId, en, PlayingStatus.InField("Kaelen", PlayerClass.Fighter, 0, 5, "Ricarten"), 500, out var switched)
            && switched.Activity.StartUnixSeconds == 500 && switched.Activity.SmallImage == "class_fighter" && switched.Activity.Details == "Kaelen · Level 5",
            "a character switch kept the old timer, name or class");

        Assert.True(tracker.TryChange(false, ApplicationId, en, ricarten, 600, out var off) && off.Activity is null && off == DiscordPresenceRequest.Clear,
            "switching the presence off did not clear it");
        Assert.True(!tracker.TryChange(false, ApplicationId, en, ricarten with { Level = 50 }, 700, out _), "a disabled presence sent a level up");
        Assert.True(tracker.TryChange(true, "42", en, ricarten, 800, out var other) && other.ApplicationId == "42", "another application id was not used");
        return Task.CompletedTask;
    }

    private static async Task FramesAsync()
    {
        var frame = DiscordIpcFrame.Encode(DiscordIpcOpcode.Frame, "{}"u8);
        Assert.True(frame.AsSpan(0, DiscordIpcFrame.HeaderSize).SequenceEqual(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0 }) && frame.Length == 10,
            $"the frame header was {Convert.ToHexString(frame)}");

        using (var stream = new MemoryStream())
        {
            stream.Write(DiscordIpcFrame.Encode(DiscordIpcOpcode.Ping, "ping"u8));
            stream.Write(DiscordIpcFrame.Encode(DiscordIpcOpcode.Close, "{\"code\":4000,\"message\":\"Invalid Client ID\"}"u8));
            stream.Position = 0;
            var ping = await DiscordIpcFrame.ReadAsync(stream, CancellationToken.None);
            var close = await DiscordIpcFrame.ReadAsync(stream, CancellationToken.None);
            Assert.True(ping.Opcode == DiscordIpcOpcode.Ping && Encoding.UTF8.GetString(ping.Payload) == "ping", "a ping frame did not read back");
            Assert.True(close.Opcode == DiscordIpcOpcode.Close && DiscordIpcMessages.ReadReply(close.Payload).Error == "Invalid Client ID",
                "a close frame's reason was lost");
        }

        using (var oversized = new MemoryStream([1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F]))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => DiscordIpcFrame.ReadAsync(oversized, CancellationToken.None), "an oversized frame was read");
        }

        using (var unknown = new MemoryStream([9, 0, 0, 0, 0, 0, 0, 0]))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => DiscordIpcFrame.ReadAsync(unknown, CancellationToken.None), "an unknown opcode was read");
        }

        using (var cut = new MemoryStream([1, 0, 0, 0, 4, 0, 0, 0, 1]))
        {
            await Assert.ThrowsAsync<EndOfStreamException>(() => DiscordIpcFrame.ReadAsync(cut, CancellationToken.None), "a cut frame was read");
        }

        using (var handshake = JsonDocument.Parse(DiscordIpcMessages.Handshake(ApplicationId)))
        {
            Assert.True(handshake.RootElement.GetProperty("v").GetInt32() == 1 && handshake.RootElement.GetProperty("client_id").GetString() == ApplicationId,
                "the handshake body is not Discord's");
        }

        var activity = new DiscordActivity("SatroX · Nível 71", "Sagittarion · Ricarten", 1_700_000_000, "logo", "Priston Avalon", "class_archer", "Sagittarion");
        using (var set = JsonDocument.Parse(DiscordIpcMessages.SetActivity(4321, activity, "7")))
        {
            var root = set.RootElement;
            var sent = root.GetProperty("args").GetProperty("activity");
            Assert.True(root.GetProperty("cmd").GetString() == "SET_ACTIVITY" && root.GetProperty("nonce").GetString() == "7"
                && root.GetProperty("args").GetProperty("pid").GetInt32() == 4321, "the SET_ACTIVITY command is malformed");
            Assert.True(sent.GetProperty("details").GetString() == activity.Details && sent.GetProperty("state").GetString() == "Sagittarion · Ricarten"
                && sent.GetProperty("timestamps").GetProperty("start").GetInt64() == 1_700_000_000, "the activity's lines or start were lost");
            var assets = sent.GetProperty("assets");
            Assert.True(assets.GetProperty("large_image").GetString() == "logo" && assets.GetProperty("small_image").GetString() == "class_archer"
                && assets.GetProperty("small_text").GetString() == "Sagittarion", "the activity's images were lost");
        }

        using (var menu = JsonDocument.Parse(DiscordIpcMessages.SetActivity(1, new DiscordActivity("In the menu", null, null, "logo", "Priston Avalon", null, null), "8")))
        {
            var sent = menu.RootElement.GetProperty("args").GetProperty("activity");
            Assert.True(!sent.TryGetProperty("state", out _) && !sent.TryGetProperty("timestamps", out _) && !sent.GetProperty("assets").TryGetProperty("small_image", out _),
                "the menu activity carries empty fields");
        }

        var error = DiscordIpcMessages.ReadReply("{\"cmd\":\"SET_ACTIVITY\",\"evt\":\"ERROR\",\"data\":{\"code\":4000,\"message\":\"bad\"},\"nonce\":\"3\"}"u8.ToArray());
        Assert.True(error == new DiscordIpcReply("SET_ACTIVITY", "ERROR", "3", "bad"), $"an error reply read as {error}");
    }

    private static async Task WorkerAsync()
    {
        var settings = new ClientSettings { DiscordApplicationId = "not-a-number" }.Validate(NullClientLog.Instance);
        Assert.True(settings.DiscordApplicationId == "" && settings.DiscordPresence, "a non-numeric application id was kept");
        Assert.True(new ClientSettings { DiscordApplicationId = ApplicationId }.Validate(NullClientLog.Instance).DiscordApplicationId == ApplicationId,
            "a valid application id was replaced");

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var discord = FakeDiscordAsync(listener);
        using (var presence = new DiscordPresence(new LoopbackConnector(port), NullClientLog.Instance))
        {
            presence.Update(new ClientSettings { DiscordApplicationId = ApplicationId }, PlayingStatus.InField("SatroX", PlayerClass.Archer, 0, 71, "Ricarten"));
            var (handshake, activity) = await discord.WaitAsync(TimeSpan.FromSeconds(10));
            using var hello = JsonDocument.Parse(handshake);
            using var set = JsonDocument.Parse(activity);
            Assert.True(hello.RootElement.GetProperty("client_id").GetString() == ApplicationId, "the worker handshook with another application");
            Assert.True(set.RootElement.GetProperty("args").GetProperty("activity").GetProperty("state").GetString()?.EndsWith(" · Ricarten", StringComparison.Ordinal) == true,
                "the worker did not send the activity after the ping");
        }
    }

    // Plays Discord's side: READY after the handshake, a ping before answering SET_ACTIVITY, then the pong is checked.
    private static async Task<(byte[] Handshake, byte[] Activity)> FakeDiscordAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var handshake = await DiscordIpcFrame.ReadAsync(stream, CancellationToken.None);
        Assert.True(handshake.Opcode == DiscordIpcOpcode.Handshake, "the first frame was not a handshake");
        await DiscordIpcFrame.WriteAsync(stream, DiscordIpcOpcode.Frame, "{\"cmd\":\"DISPATCH\",\"evt\":\"READY\",\"data\":{}}"u8.ToArray(), CancellationToken.None);
        var activity = await DiscordIpcFrame.ReadAsync(stream, CancellationToken.None);
        await DiscordIpcFrame.WriteAsync(stream, DiscordIpcOpcode.Ping, "p1"u8.ToArray(), CancellationToken.None);
        var pong = await DiscordIpcFrame.ReadAsync(stream, CancellationToken.None);
        Assert.True(pong.Opcode == DiscordIpcOpcode.Pong && pong.Payload.AsSpan().SequenceEqual("p1"u8), "a ping was not answered with its payload");
        var nonce = DiscordIpcMessages.ReadReply(activity.Payload).Nonce;
        await DiscordIpcFrame.WriteAsync(stream, DiscordIpcOpcode.Frame,
            Encoding.UTF8.GetBytes("{\"cmd\":\"SET_ACTIVITY\",\"data\":{},\"evt\":null,\"nonce\":\"" + nonce + "\"}"), CancellationToken.None);
        return (handshake.Payload, activity.Payload);
    }

    private sealed class LoopbackConnector(int port) : IDiscordIpcConnector
    {
        public async Task<Stream> TryConnectAsync(CancellationToken cancellation)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cancellation);
            return new NetworkStream(socket, ownsSocket: true);
        }
    }
}
