using System.Text.Json;
using PristonTale.Application.Chat;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;

namespace PristonTale.Server.Hosting;

internal sealed class PublicActivityService(WorldServer world, ChatDirectory chat, WhisperActivity whispers, IConfiguration settings,
    ILogger<PublicActivityService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings["PublicActivity:Path"] is not { Length: > 0 } configuredPath)
        {
            return;
        }

        var path = Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
        world.FeatRecorded = chat.RecordFeat;
        var whisperPath = settings["PublicActivity:WhisperPath"] is { Length: > 0 } configuredWhispers
            ? Path.GetFullPath(configuredWhispers, AppContext.BaseDirectory) : null;
        if (string.Equals(path, whisperPath, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Public chat and whisper snapshots must use separate paths");
            whisperPath = null;
        }

        var streamId = Guid.NewGuid();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                var snapshot = new PublicGameActivity(streamId, DateTimeOffset.UtcNow, world.PlayerCount, chat.RecentPublicMessages());
                await WriteAsync(path, snapshot, stoppingToken);
                if (whisperPath is not null)
                {
                    await WriteAsync(whisperPath, snapshot with { Messages = whispers.Recent() }, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not write the public game activity snapshot");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static async Task WriteAsync(string path, PublicGameActivity snapshot, CancellationToken ct)
    {
        var temporary = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot), ct);
        File.Move(temporary, path, overwrite: true);
    }
}
