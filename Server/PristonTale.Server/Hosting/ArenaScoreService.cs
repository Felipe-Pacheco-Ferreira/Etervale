using System.Globalization;
using System.Threading.Channels;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Arena;
using PristonTale.Simulation.Combat;

namespace PristonTale.Server.Hosting;

// The arena's storage (ArenaRules): each match's results (WorldServer.ArenaResultRecorded, on the tick) are queued and
// written off the tick as the week's points; draws change nothing. Every few minutes the prizes of the week before the
// world clock's week (WorldServer.ArenaWeek) are paid once per level band (IArenaRepository.AwardAsync), with the gold
// of WorldServerOptions.Arena; when the server is down for the whole of the week after, that week is not paid.
internal sealed class ArenaScoreService(WorldServer world, IArenaRepository arena, ILogger<ArenaScoreService> logger) : BackgroundService
{
    private static readonly TimeSpan PrizeCheck = TimeSpan.FromMinutes(5);

    private readonly Channel<ArenaResult> _results = Channel.CreateUnbounded<ArenaResult>(new UnboundedChannelOptions { SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        world.ArenaResultRecorded = result => _results.Writer.TryWrite(result);
        var prizes = PayPrizesAsync(stoppingToken);
        await foreach (var result in _results.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                foreach (var entry in result.Entries)
                {
                    if (entry.CharacterId > 0 && entry.Outcome != ArenaOutcome.Draw)
                    {
                        await arena.RecordAsync(ArenaRules.WeekKey(result.Week), result.Band, entry.CharacterId, entry.Outcome == ArenaOutcome.Won, ArenaRules.WinPoints,
                            ArenaRules.LossPoints, stoppingToken);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not store an arena match's results");
            }
        }

        await prizes;
    }

    private async Task PayPrizesAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PrizeCheck);
        do
        {
            try
            {
                var options = world.Options.Arena;
                var week = world.LastArenaWeek();
                var gold = new long[ArenaRules.PrizePlaces];
                for (var place = 1; place <= gold.Length; place++)
                {
                    gold[place - 1] = options.PrizeFor(place);
                }

                for (var band = 0; band < ArenaRules.Bands; band++)
                {
                    var body = string.Create(CultureInfo.InvariantCulture,
                        $"Prêmio da arena pela semana de {week:dd/MM/yyyy}, faixa de nível {ArenaRules.BandName(band)}. Parabéns!");
                    foreach (var prize in await arena.AwardAsync(ArenaRules.WeekKey(week), band, gold, "Prêmio da arena", body, DateTime.UtcNow, ct))
                    {
                        logger.LogInformation("Arena prize: band {Band}, place {Place}, {Name}, {Gold} gold", prize.Band, prize.Place, prize.Name, prize.Gold);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not pay the arena's weekly prizes");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
