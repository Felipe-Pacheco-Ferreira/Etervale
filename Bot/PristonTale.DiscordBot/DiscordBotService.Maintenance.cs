using Discord;
using Discord.WebSocket;
using PristonTale.Application.GameMasters;
using PristonTale.Persistence.Abstractions.GameMasters;

namespace PristonTale.DiscordBot;

// /manutencao: a game master turns the maintenance notice on or off. It is announced in the world status channel with
// @everyone, the status panel turns yellow while it lasts, and worlds going down or up are not announced meanwhile.
public sealed partial class DiscordBotService
{
    private const int MaintenanceLevel = GameMasterLevel.Moderator;

    private async Task MaintenanceAsync(SocketSlashCommand command, CancellationToken ct)
    {
        await command.DeferAsync(ephemeral: true);
        var answer = await SetMaintenanceAsync(command, ct);
        await command.FollowupAsync(answer, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private async Task<string> SetMaintenanceAsync(SocketSlashCommand command, CancellationToken ct)
    {
        var level = LevelOf(command.User);
        if (level < MaintenanceLevel)
        {
            return "⛔ Só moderadores ou acima ligam a manutenção.";
        }

        var on = command.Data.Options.FirstOrDefault(static option => option.Name == SlashCommands.ActiveOption)?.Value is true;
        var reason = BotTexts.Cut((Option(command.Data.Options, SlashCommands.ReasonOption) ?? "").Trim(), SlashCommands.ReasonLimit);
        if (on == _state.Maintenance)
        {
            return on ? "A manutenção já está ligada." : "A manutenção já está desligada.";
        }

        await SaveStateAsync(state =>
        {
            state.Maintenance = on;
            state.MaintenanceReason = on ? reason : "";
        }, ct);
        await SendAsync(settings.Discord.Channels.WorldStatusChannel, [BotTexts.MaintenanceChanged(on, reason)], ping: true);
        try
        {
            await UpdateStatusPanelAsync(BotTexts.StatusPanel(await site.WorldsAsync(ct), on, reason), repost: true, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(exception, "The status panel waits for the next reading of the worlds");
        }

        await gameMasters.AppendAuditAsync(new GmAuditEntry(0, AuditUser(command.User), DisplayName(command.User), level,
            "discord: manutencao " + (on ? "on" : "off"), MaintenanceLevel, true, reason), ct);
        return on ? "🟡 Manutenção ligada." : "🟢 Manutenção encerrada.";
    }
}
