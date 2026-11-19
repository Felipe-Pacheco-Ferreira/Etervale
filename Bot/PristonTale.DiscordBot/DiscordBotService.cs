using System.Globalization;
using Discord;
using Discord.WebSocket;
using PristonTale.Application.Support;
using PristonTale.DiscordBot.Sources;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot;

// The bot on Discord: the slash commands, the chat relays both ways, the announcements and the support tickets. Discord's
// handlers hand their work to the thread pool at once, so the gateway is never held by storage or the game server.
public sealed partial class DiscordBotService(BotSettings settings, BotQueries queries, SiteClient site, SiteData data, ClassTitles titles, GameLinks links,
    IGameMasterRepository gameMasters, IServiceProvider services, TimeProvider time, ILogger<DiscordBotService> logger) : BackgroundService
{
    // Discord's upload limit for a server without boosts.
    private const long AttachmentLimit = 10 * 1024 * 1024;
    private readonly DiscordSocketClient _client = new(new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
        AlwaysDownloadUsers = false
    });

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SupportTickets? _tickets = services.GetService(typeof(SupportTickets)) as SupportTickets;
    private readonly string _statePath = BotSettings.BesideExecutable(BotState.FileName);
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private BotState _state = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _state = await BotState.LoadAsync(_statePath, stoppingToken);
        _client.Log += LogAsync;
        _client.Ready += () => Offload(OnReadyAsync);
        _client.SlashCommandExecuted += command => Offload(() => HandleCommandAsync(command, stoppingToken));
        _client.ButtonExecuted += component => Offload(() => HandleButtonAsync(component, stoppingToken));
        _client.MessageReceived += message => Offload(async () =>
        {
            if (!await AnswerFromThreadAsync(message, stoppingToken))
            {
                await RelayToGameAsync(message, stoppingToken);
            }
        });
        await _client.LoginAsync(TokenType.Bot, settings.Discord.Token);
        await _client.StartAsync();
        try
        {
            await _ready.Task.WaitAsync(stoppingToken);
            await WatchAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _client.StopAsync();
            await _client.LogoutAsync();
            _client.Dispose();
        }
    }

    private Task Offload(Func<Task> work)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "A Discord event failed");
            }
        });
        return Task.CompletedTask;
    }

    private async Task OnReadyAsync()
    {
        if (_client.GetGuild(settings.Discord.GuildId) is not { } guild)
        {
            logger.LogError("The bot is not in the guild {Guild} (Discord:GuildId)", settings.Discord.GuildId);
            return;
        }

        try
        {
            await guild.BulkOverwriteApplicationCommandAsync(SlashCommands.Build(titles));
            logger.LogInformation("Discord bot ready in {Guild}", guild.Name);
        }
        finally
        {
            // Without its commands (the applications.commands scope missing) the bot still relays and announces.
            _ready.TrySetResult();
        }
    }

    private async Task HandleCommandAsync(SocketSlashCommand command, CancellationToken ct)
    {
        if (command.Data.Name == SlashCommands.DevNote)
        {
            await DevNoteAsync(command, ct);
            return;
        }

        if (command.Data.Name == SlashCommands.Maintenance)
        {
            await MaintenanceAsync(command, ct);
            return;
        }

        if (command.Data.Name == SlashCommands.Purge)
        {
            await AskPurgeAsync(command);
            return;
        }

        var gameMaster = command.Data.Name == SlashCommands.GameMaster;
        await command.DeferAsync(ephemeral: gameMaster);
        string answer;
        try
        {
            answer = command.Data.Name switch
            {
                SlashCommands.Status => await queries.StatusAsync(ct),
                SlashCommands.Ranking => await queries.RankingAsync(ClassTitles.FromSlug(Option(command.Data.Options, SlashCommands.ClassOption)), ct),
                SlashCommands.Events => await queries.EventsAsync(ct),
                SlashCommands.News => await queries.NewsAsync(ct),
                SlashCommands.GameMaster => await GameMasterAsync(command, ct),
                _ => "❓ Comando desconhecido."
            };
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "/{Command} failed", command.Data.Name);
            answer = "⚠️ Não consegui ler os dados agora. Tente de novo em instantes.";
        }

        foreach (var message in BotTexts.Pack(answer.Split('\n')))
        {
            await command.FollowupAsync(message, ephemeral: gameMaster, allowedMentions: AllowedMentions.None);
        }
    }

    private Task<string> GameMasterAsync(SocketSlashCommand command, CancellationToken ct)
    {
        var level = LevelOf(command.User);
        var sub = command.Data.Options.FirstOrDefault();
        if (level == 0)
        {
            return Task.FromResult("⛔ Só game masters usam /gm.");
        }

        if (sub is null || GameMasterActions.Find(sub.Name) is not { } action)
        {
            return Task.FromResult("❓ Comando desconhecido.");
        }

        var argument = action.Argument is null ? null : Option(sub.Options, action.Argument);
        if (action.ArgumentRequired && action.Argument is not null && string.IsNullOrWhiteSpace(argument))
        {
            return Task.FromResult($"Informe {action.Argument}.");
        }

        return queries.GameMasterAsync(AuditUser(command.User), DisplayName(command.User), level, action.Line(argument), ct);
    }

    private async Task RelayToGameAsync(SocketMessage message, CancellationToken ct)
    {
        if (!settings.Watch.RelayToGame || !links.AnyEnabled || message.Channel.Id != settings.Discord.Channels.TradeChat
            || message.Author.IsBot || message.Author.IsWebhook || message is not SocketUserMessage || string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        // Discord's trade channel reaches the trade channel of every server; ⚠️ when any of them refused or did not answer.
        var author = DisplayName(message.Author);
        var sent = await Task.WhenAll(links.All.Where(static link => link.Enabled).Select(link => SayAsync(link, author, message.CleanContent, ct)));
        if (sent.Contains(false))
        {
            await message.AddReactionAsync(new Emoji("⚠️"));
        }
    }

    private async Task<bool> SayAsync(GameLinkClient link, string author, string text, CancellationToken ct)
    {
        try
        {
            return await link.SayAsync(author, text, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Discord line not delivered to the game server {Server}: {Error}", link.Server, exception.Message);
            return false;
        }
    }

    private int LevelOf(IUser user) => user is SocketGuildUser member
        ? GameMasterActions.LevelOf(member.Roles.Select(static role => role.Id), settings.Discord.GameMasterRoles)
        : 0;

    private static string DisplayName(IUser user) => user is SocketGuildUser member ? member.DisplayName : user.GlobalName ?? user.Username;

    // The user id the game's audit trail keeps for a Discord member.
    private static string AuditUser(IUser user) => "discord:" + user.Id.ToString(CultureInfo.InvariantCulture);

    private static string? Option(IEnumerable<SocketSlashCommandDataOption> options, string name) =>
        options.FirstOrDefault(option => option.Name == name)?.Value?.ToString();

    private async Task SendAsync(ulong channelId, IEnumerable<string> lines, bool ping = false)
    {
        if (channelId == 0 || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            return;
        }

        // A ping is the first line of the batch, so it stays within Discord's limit and rings once.
        var messages = BotTexts.Pack(lines);
        ping &= settings.Discord.PingEveryone && messages.Count > 0;
        await SendMessagesAsync(channel, ping ? BotTexts.Pack(lines.Prepend(BotTexts.Everyone)) : messages, ping);
    }

    private async Task SendMessagesAsync(IMessageChannel channel, IEnumerable<string> messages, bool ping = false)
    {
        foreach (var message in messages)
        {
            await channel.SendMessageAsync(message, allowedMentions: Mentions(ping));
        }
    }

    // Only the bot's own @everyone ever pings: the game's words are escaped and mentions stay off otherwise.
    private static AllowedMentions Mentions(bool ping) => ping ? new AllowedMentions(AllowedMentionTypes.Everyone) : AllowedMentions.None;

    private Task LogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            _ => LogLevel.Debug
        };
        logger.Log(level, message.Exception, "Discord {Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _stateGate.Dispose();
        _ticketGate.Dispose();
        base.Dispose();
    }
}
