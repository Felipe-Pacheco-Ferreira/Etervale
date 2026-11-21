using System.Text.Json;
using System.Threading.Channels;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.BossRoulette;

namespace PristonTale.Server.Hosting;

// The boss roulette's storage (BossRoulette): each draw (WorldServer.BossRouletteDrawn, on the tick) is queued, kept for
// audit and its prize mailed to the winner off the tick (IBossRouletteRepository.RecordAsync). A failure is logged.
internal sealed class BossRouletteService(WorldServer world, IBossRouletteRepository roulette, ILogger<BossRouletteService> logger) : BackgroundService
{
    private readonly Channel<BossRouletteResult> _draws = Channel.CreateUnbounded<BossRouletteResult>(new UnboundedChannelOptions { SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        world.BossRouletteDrawn = draw => _draws.Writer.TryWrite(draw);
        await foreach (var draw in _draws.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var winner = draw.WinnerEntry;
                var record = new BossRouletteRecord(draw.FieldId, draw.BossCode, draw.BossName, winner.CharacterId, draw.PrizeKey,
                    JsonSerializer.Serialize(draw.Entries));
                var mail = await roulette.RecordAsync(record, "Roleta do boss", $"Você ganhou {draw.PrizeName} na roleta do boss {draw.BossName}!", DateTime.UtcNow,
                    stoppingToken);
                logger.LogInformation("Boss roulette: {Boss} in field {Field}, {Count} entrants, {Winner} ({Character}) won {Prize}, mail {Mail}", draw.BossName,
                    draw.FieldId, draw.Entries.Count, winner.Name, winner.CharacterId, draw.PrizeKey, mail);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not store a boss roulette's draw");
            }
        }
    }
}
