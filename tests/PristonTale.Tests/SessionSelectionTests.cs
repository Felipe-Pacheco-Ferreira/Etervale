using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Client.Net;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class SessionSelectionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("selection return: a durable leave releases its character and the same session can enter another character", ReentryAsync);
        yield return ("selection return: a blocked or failed save retains the claim and can be retried", SaveFailureAsync);
        yield return ("selection return: disconnect during a blocked save cannot reopen the closed session", DisconnectAsync);
        yield return ("selection return: racing entries after a return still admit only one", EntryRaceAsync);
        yield return ("selection return: browser ticket connection re-enters after leaving without redeeming the ticket again", BrowserConnectionAsync);
    }

    private static CharacterRecord Record(string name = "Returner", long id = 7) =>
        CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), 1, name, 0, PlayerClass.Fighter, 3) with { Id = id };

    private static SessionAppService Service(SessionRegistry registry, ICharacterRepository repository) =>
        new(registry, new CharacterClassRegistry(), repository, new ImmediateSaves(repository), new WorldCatalog());

    private static async Task<PlayerSession> PlayingAsync(SessionRegistry registry, SessionAppService service, string name = "Returner")
    {
        var session = registry.Create(1, "selection-account");
        Assert.True((await service.EnterWorldAsync(session.SessionToken, name)).Ok && session.TryBeginPlay(), "fixture did not enter play");
        return session;
    }

    private static CharacterSaveSnapshot Left(PlayerSession session) =>
        CharacterRecordFactory.Capture(session.Loaded!, session.Character!, 3, new Vector3(123, 0, 456));

    private static async Task ReentryAsync()
    {
        var book = new MemoryCharacterBook();
        book.Records["Returner"] = Record();
        book.Records["Second"] = Record("Second", 8);
        var registry = new SessionRegistry();
        var service = Service(registry, book);
        var session = await PlayingAsync(registry, service);
        var token = session.SessionToken;
        session.MobileShopOpen = true;
        session.LeftDead = true;
        var left = Left(session);
        session.EndPlay(returnToSelection: true);
        Assert.True(await service.SaveAndReturnToSelectionAsync(token, left), "durable leave failed");
        Assert.True(session.Character is null && session.Loaded is null && session.CharacterName is null
            && !session.MobileShopOpen && !session.LeftDead && !session.IsPlaying && !session.IsLeavingForSelection, "old character state survived return");
        Assert.True(registry.TryGet(token, out var same) && ReferenceEquals(same, session), "return replaced the authenticated session");
        var other = registry.Create(1, "another-login");
        Assert.True((await service.EnterWorldAsync(other.SessionToken, "Returner")).Ok, "the old character claim was not released");
        registry.Remove(other.SessionToken);
        Assert.True((await service.EnterWorldAsync(token, "Second")).Ok && session.TryBeginPlay(), "the same token could not select another character");
        session.EndPlay(returnToSelection: true);
        Assert.True(await service.SaveAndReturnToSelectionAsync(token, Left(session)), "second leave failed");
        Assert.True((await service.EnterWorldAsync(token, "missing-character")).Failure == EnterWorldFailure.NotFound, "return admitted a missing character");
        Assert.True((await service.EnterWorldAsync(token, "Returner")).Ok && session.SpawnPosition.X == 123 && session.SpawnPosition.Z == 456,
            "re-entry did not hydrate the saved departure position");
    }

    private static async Task SaveFailureAsync()
    {
        var repository = new MemoryCharacters(Record());
        var registry = new SessionRegistry();
        var service = Service(registry, repository);
        var session = await PlayingAsync(registry, service);
        session.EndPlay(returnToSelection: true);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeUpdate = async _ =>
        {
            started.TrySetResult(true);
            return await release.Task;
        };
        var save = service.SaveAndReturnToSelectionAsync(session.SessionToken, Left(session));
        await started.Task;
        Assert.True((await service.EnterWorldAsync(session.SessionToken, "Returner")).Failure == EnterWorldFailure.AlreadyEntering,
            "entry passed while the old save was pending");
        var other = registry.Create(1, "competitor");
        Assert.True((await service.EnterWorldAsync(other.SessionToken, "Returner")).Failure == EnterWorldFailure.AlreadyActive,
            "the character claim was released before storage accepted it");
        release.SetResult(false);
        Assert.True(!await save && session.IsLeavingForSelection && session.Loaded is not null && session.Character is not null,
            "failed storage reopened or cleared the session");
        repository.BeforeUpdate = null;
        Assert.True(await service.SaveAndReturnToSelectionAsync(session.SessionToken, Left(session)), "a retained leave could not retry");
        Assert.True((await service.EnterWorldAsync(session.SessionToken, "Returner")).Ok, "retry did not restore selection");
    }

    private static async Task DisconnectAsync()
    {
        var repository = new MemoryCharacters(Record());
        var registry = new SessionRegistry();
        var service = Service(registry, repository);
        var session = await PlayingAsync(registry, service);
        session.EndPlay(returnToSelection: true);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeUpdate = async _ =>
        {
            started.TrySetResult(true);
            return await release.Task;
        };
        var save = service.SaveAndReturnToSelectionAsync(session.SessionToken, Left(session));
        await started.Task;
        registry.Remove(session.SessionToken);
        var next = registry.Create(1, "replacement");
        Assert.True((await service.EnterWorldAsync(next.SessionToken, "Returner")).Ok, "disconnect kept the character claimed");
        release.SetResult(true);
        Assert.True(await save && !session.TryBeginPlay() && !session.IsLeavingForSelection, "save completion resurrected a closed session");
        Assert.True((await service.EnterWorldAsync(session.SessionToken, "Returner")).Failure == EnterWorldFailure.InvalidSession,
            "a closed token entered after the leave save");
        var third = registry.Create(1, "claim-racer");
        Assert.True((await service.EnterWorldAsync(third.SessionToken, "Returner")).Failure == EnterWorldFailure.AlreadyActive,
            "the old save completion released the replacement session's claim");
    }

    private static async Task EntryRaceAsync()
    {
        var repository = new MemoryCharacters(Record());
        var registry = new SessionRegistry();
        var service = Service(registry, repository);
        var session = await PlayingAsync(registry, service);
        session.EndPlay(returnToSelection: true);
        Assert.True(await service.SaveAndReturnToSelectionAsync(session.SessionToken, Left(session)), "leave failed");
        var entries = await Task.WhenAll(service.EnterWorldAsync(session.SessionToken, "Returner"), service.EnterWorldAsync(session.SessionToken, "Returner"));
        Assert.True(entries.Count(entry => entry.Ok) == 1 && entries.Single(entry => !entry.Ok).Failure == EnterWorldFailure.AlreadyEntering,
            "return permitted two concurrent entries");
    }

    private static async Task BrowserConnectionAsync()
    {
        await using var server = await MultiplayerServer.StartAsync(MultiplayerServer.WorldWith(),
            ("Network:WebSocket:Enabled", "true"), ("Network:WebSocket:Url", "http://127.0.0.1:0"),
            ("Network:WebSocket:AllowedOrigins:0", "https://play.example"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        var account = await server.CreatePlayerAsync(new PlayerSetup("BrowserReturn", PlayerClass.Fighter, 10,
            MultiplayerServer.HuntingField, MultiplayerServer.Camp));
        await server.CreatePlayerAsync(new PlayerSetup("ForeignCharacter", PlayerClass.Fighter, 10,
            MultiplayerServer.HuntingField, MultiplayerServer.Camp));
        var ticket = await server.IssueBrowserTicketAsync(account, TimeSpan.FromMinutes(1), ct);
        var endpoint = server.WebSocketAddress!.AbsoluteUri;
        await using var connection = new GameServerConnection(endpoint);
        Assert.True((await connection.LoginAsync(account, ticket, "en", ct)).Ok, "browser ticket login failed");
        var token = connection.SessionToken;
        for (var pass = 0; pass < 2; pass++)
        {
            var entered = await connection.EnterWorldAsync("BrowserReturn", ct);
            Assert.True(entered.Ok, $"same browser connection entry {pass}: {entered.Failure}");
            var play = connection.OpenPlayChannel(ct);
            await using (var updates = play.ReadAllAsync(ct).GetAsyncEnumerator(ct))
            {
                Assert.True(await updates.MoveNextAsync(), "the re-entered channel sent no world state");
                play.Recycle(updates.Current);
            }
            await play.DisposeAsync();
            Assert.True(connection.SessionToken == token && (await connection.ListCharactersAsync(ct)).Any(character => character.Name == "BrowserReturn"),
                "leaving changed the authenticated token or lost the roster");
            Assert.True((await connection.EnterWorldAsync("ForeignCharacter", ct)).Failure == EnterWorldFailure.NotFound,
                "return admitted a character belonging to another account");
        }
        await using var replay = new GameServerConnection(endpoint);
        Assert.True(!(await replay.LoginAsync(account, ticket, "en", ct)).Ok, "return made a consumed browser ticket reusable");
    }
}
