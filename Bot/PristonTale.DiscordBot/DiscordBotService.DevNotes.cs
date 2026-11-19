using Discord;
using Discord.WebSocket;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Web.Services;

namespace PristonTale.DiscordBot;

// Development notes in Channels:Development: /devnota, signed with the member's display name (the server nickname), and
// the notes of the devnotes folder, published once each to Discord and to the site's news the game shows. A command's
// images are uploaded again with the post, since its own attachment links expire.
public sealed partial class DiscordBotService
{
    private static readonly HttpClient Downloads = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Color DevNoteColor = new(0xE0A040);

    private async Task DevNoteAsync(SocketSlashCommand command, CancellationToken ct)
    {
        await command.DeferAsync(ephemeral: true);
        var answer = await PostDevNoteAsync(command, ct);
        await command.FollowupAsync(answer, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private async Task<string> PostDevNoteAsync(SocketSlashCommand command, CancellationToken ct)
    {
        var level = LevelOf(command.User);
        if (level < settings.Discord.DevNoteMinGmLevel)
        {
            return "⛔ Seu cargo não publica notas de desenvolvimento.";
        }

        if (DevelopmentChannel() is not { } channel)
        {
            return "O canal de desenvolvimento não está configurado (Discord:Channels:Development).";
        }

        var options = command.Data.Options;
        var images = SlashCommands.ImageOptions
            .Select(name => options.FirstOrDefault(option => option.Name == name)?.Value as IAttachment)
            .OfType<IAttachment>()
            .ToArray();
        if (images.FirstOrDefault(static image => image.ContentType?.StartsWith("image/", StringComparison.Ordinal) != true) is { } notImage)
        {
            return $"⚠️ {notImage.Filename} não é uma imagem.";
        }

        if (images.FirstOrDefault(static image => image.Size > AttachmentLimit) is { } large)
        {
            return $"⚠️ {large.Filename} passa de 10 MB.";
        }

        var author = DisplayName(command.User);
        var title = BotTexts.DevNoteTitle(Option(options, SlashCommands.TitleOption) ?? "");
        try
        {
            var files = new List<(string Name, byte[] Bytes)>();
            foreach (var image in images)
            {
                files.Add((image.Filename, await Downloads.GetByteArrayAsync(image.Url, ct)));
            }

            await SendDevNoteAsync(channel, title, BotTexts.DevNoteText(Option(options, SlashCommands.TextOption) ?? ""),
                BotTexts.DevNoteSignature(author, time.GetUtcNow(), TimeZoneInfo.Local), command.User.GetAvatarUrl(), files);
        }
        catch (Exception exception) when (exception is HttpRequestException or Discord.Net.HttpException
            || (exception is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(exception, "/devnota failed");
            return "⚠️ Não consegui publicar a nota agora. Tente de novo em instantes.";
        }

        await gameMasters.AppendAuditAsync(new GmAuditEntry(0, AuditUser(command.User), author, level, "discord: devnota",
            settings.Discord.DevNoteMinGmLevel, true, title), ct);
        return $"✅ Nota publicada em <#{channel.Id}>.";
    }

    // Every new system and improvement of the devnotes folder is in the site's news, once by its title and day (an
    // improvement with its badge), whether or not Discord has it yet. Then each note not published yet goes to its Discord
    // channel (a staff tool to the staff's) and is marked; a failure, or a channel not configured yet, is tried again on
    // the next turn.
    private async Task WatchDevNotesAsync(CancellationToken ct)
    {
        var notes = DevNote.ReadAll(BotSettings.BesideExecutable(settings.DevNotes.Path));
        if (settings.DevNotes.NewsPath.Length > 0)
        {
            var news = new NewsFeed(BotSettings.BesideExecutable(settings.DevNotes.NewsPath));
            foreach (var note in notes.Where(BotTexts.InNews))
            {
                if (!news.Items(logger).Any(item => item.Date == note.Date && item.Title == note.Title))
                {
                    news.Publish(BotTexts.DevNoteNews(note, settings.DevNotes.Author));
                }
            }
        }

        foreach (var note in notes.Where(note => !_state.DevNotes.Contains(note.Id)))
        {
            var target = note.Kind == DevNoteKind.Staff ? settings.Discord.Channels.StaffDevelopment : settings.Discord.Channels.Development;
            if (target == 0 || _client.GetChannel(target) is not IMessageChannel channel)
            {
                continue;
            }

            var files = new List<(string Name, byte[] Bytes)>();
            foreach (var image in note.Images)
            {
                files.Add((Path.GetFileName(image), await File.ReadAllBytesAsync(image, ct)));
            }

            var signature = BotTexts.DevNoteSignature(settings.DevNotes.Author, new DateTimeOffset(note.Date.ToDateTime(TimeOnly.MinValue)), TimeZoneInfo.Utc);
            await SendDevNoteAsync(channel, BotTexts.DevNoteTitle(note.Title, note.Kind), BotTexts.DevNoteText(note.Text), signature, null, files);
            await SaveStateAsync(state => state.DevNotes.Add(note.Id), ct);
            logger.LogInformation("Development note {Note} published", note.Id);
        }
    }

    private IMessageChannel? DevelopmentChannel() =>
        settings.Discord.Channels.Development == 0 ? null : _client.GetChannel(settings.Discord.Channels.Development) as IMessageChannel;

    // Embeds sharing one URL show as one post with an image gallery.
    private async Task SendDevNoteAsync(IMessageChannel channel, string title, string text, string signature, string? avatar,
        IReadOnlyList<(string Name, byte[] Bytes)> images)
    {
        var files = images.Select(static (image, index) => new FileAttachment(new MemoryStream(image.Bytes), $"print{index + 1}{Path.GetExtension(image.Name)}")).ToList();
        try
        {
            var link = $"https://discord.com/channels/{settings.Discord.GuildId}/{channel.Id}";
            var embeds = new List<Embed>
            {
                new EmbedBuilder()
                    .WithTitle(title)
                    .WithUrl(link)
                    .WithDescription(text)
                    .WithColor(DevNoteColor)
                    .WithFooter(signature, avatar)
                    .WithImageUrl(files.Count > 0 ? "attachment://" + files[0].FileName : null)
                    .Build()
            };
            embeds.AddRange(files.Skip(1).Select(file => new EmbedBuilder().WithUrl(link).WithImageUrl("attachment://" + file.FileName).Build()));
            var ping = settings.Discord.PingEveryone;
            var content = ping ? BotTexts.Everyone : null;
            if (files.Count > 0)
            {
                await channel.SendFilesAsync(files, content, embeds: embeds.ToArray(), allowedMentions: Mentions(ping));
            }
            else
            {
                await channel.SendMessageAsync(content, embeds: embeds.ToArray(), allowedMentions: Mentions(ping));
            }
        }
        finally
        {
            foreach (var file in files)
            {
                file.Dispose();
            }
        }
    }
}
