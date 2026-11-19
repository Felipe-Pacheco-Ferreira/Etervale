using System.Globalization;
using Discord;
using Discord.WebSocket;
using PristonTale.Application.GameMasters;
using PristonTale.Persistence.Abstractions.GameMasters;

namespace PristonTale.DiscordBot;

// /limparchat: an administrator empties a channel, newest messages first, after confirming with a button only they see.
// Discord deletes messages younger than 14 days in bulk and older ones one by one, so an old channel takes longer.
public sealed partial class DiscordBotService
{
    private const string PurgeButton = "purge";
    private const int PurgeLevel = GameMasterLevel.Administrator;
    private const int PurgePage = 100;

    // Bulk deletes take messages up to 14 days old; a margin keeps a message from aging past it while the batch is sent.
    private static readonly TimeSpan BulkDeleteAge = TimeSpan.FromDays(14) - TimeSpan.FromHours(1);

    private async Task AskPurgeAsync(SocketSlashCommand command)
    {
        if (LevelOf(command.User) < PurgeLevel)
        {
            await command.RespondAsync("⛔ Só administradores limpam canais.", ephemeral: true);
            return;
        }

        var options = command.Data.Options;
        var channel = options.FirstOrDefault(static option => option.Name == SlashCommands.ChannelOption)?.Value as IMessageChannel ?? command.Channel;
        var count = options.FirstOrDefault(static option => option.Name == SlashCommands.CountOption)?.Value is long asked
            ? (int)Math.Clamp(asked, 1, SlashCommands.PurgeLimit)
            : SlashCommands.PurgeLimit;
        var buttons = new ComponentBuilder().WithButton("🗑️ Apagar", PurgeId(channel.Id, count), ButtonStyle.Danger);
        await command.RespondAsync(BotTexts.PurgeQuestion(channel.Id, count, count == SlashCommands.PurgeLimit), components: buttons.Build(), ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    internal static string PurgeId(ulong channelId, int count) =>
        PurgeButton + ":" + channelId.ToString(CultureInfo.InvariantCulture) + ":" + count.ToString(CultureInfo.InvariantCulture);

    internal static (ulong ChannelId, int Count)? ParsePurge(string customId)
    {
        var parts = customId.Split(':');
        return parts.Length == 3 && parts[0] == PurgeButton
            && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var channel)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count is > 0 and <= SlashCommands.PurgeLimit
            ? (channel, count)
            : null;
    }

    private async Task PurgeAsync(SocketMessageComponent component, CancellationToken ct)
    {
        var level = LevelOf(component.User);
        if (level < PurgeLevel || ParsePurge(component.Data.CustomId) is not { } purge || _client.GetChannel(purge.ChannelId) is not ITextChannel channel)
        {
            await component.RespondAsync("⛔ Não posso limpar esse canal.", ephemeral: true);
            return;
        }

        await component.UpdateAsync(message =>
        {
            message.Content = $"🧹 Limpando <#{channel.Id}>…";
            message.Components = new ComponentBuilder().Build();
        });
        var deleted = 0;
        string answer;
        try
        {
            deleted = await DeleteNewestAsync(channel, purge.Count, ct);
            answer = $"✅ {deleted} mensagens apagadas em <#{channel.Id}>.";
        }
        catch (Discord.Net.HttpException exception)
        {
            logger.LogWarning(exception, "/limparchat stopped in {Channel}", channel.Id);
            answer = $"⚠️ Parei depois de {deleted} mensagens: o bot precisa de Gerenciar Mensagens em <#{channel.Id}>.";
        }

        if (channel.Id == settings.Discord.Channels.WorldStatusChannel)
        {
            _panelShown = "";
        }

        await gameMasters.AppendAuditAsync(new GmAuditEntry(0, AuditUser(component.User), DisplayName(component.User), level,
            "discord: limparchat " + channel.Name, PurgeLevel, true, deleted.ToString(CultureInfo.InvariantCulture) + " mensagens"), ct);
        await component.FollowupAsync(answer, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    // The newest messages first, a page at a time: the young ones in one bulk delete, the old ones one by one.
    private async Task<int> DeleteNewestAsync(ITextChannel channel, int count, CancellationToken ct)
    {
        var deleted = 0;
        while (deleted < count)
        {
            var page = (await channel.GetMessagesAsync(Math.Min(PurgePage, count - deleted)).FlattenAsync()).ToArray();
            if (page.Length == 0)
            {
                break;
            }

            var cutoff = time.GetUtcNow() - BulkDeleteAge;
            var young = page.Where(message => message.Timestamp > cutoff).ToArray();
            if (young.Length > 0)
            {
                await channel.DeleteMessagesAsync(young);
            }

            foreach (var old in page.Where(message => message.Timestamp <= cutoff))
            {
                ct.ThrowIfCancellationRequested();
                await old.DeleteAsync();
            }

            deleted += page.Length;
        }

        return deleted;
    }
}
