using System.Threading.Channels;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Royale;
using PristonTale.Simulation.Combat;

namespace PristonTale.Server.Hosting;

// The Battle Royale's prizes (RoyaleRules): each finished event's places (WorldServer.RoyaleResultRecorded, on the tick)
// are queued and the first RoyaleRules.PrizePlaces paid off the tick once per event (IRoyaleRepository.AwardAsync), with
// the gold of WorldServerOptions.Royale. A failed payment is logged.
internal sealed class RoyalePrizeService(WorldServer world, IRoyaleRepository royale, ILogger<RoyalePrizeService> logger) : BackgroundService
{
    private readonly Channel<RoyaleResult> _results = Channel.CreateUnbounded<RoyaleResult>(new UnboundedChannelOptions { SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        world.RoyaleResultRecorded = result => _results.Writer.TryWrite(result);
        await foreach (var result in _results.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var options = world.Options.Royale;
                var prizes = new List<RoyalePrize>(RoyaleRules.PrizePlaces);
                foreach (var place in result.Places)
                {
                    if (place.Place <= RoyaleRules.PrizePlaces && place.CharacterId > 0)
                    {
                        prizes.Add(new RoyalePrize(place.Place, place.CharacterId, options.PrizeFor(place.Place)));
                    }
                }

                foreach (var prize in await royale.AwardAsync(result.Key, prizes, "Prêmio do Battle Royale", "Parabéns pela sua colocação no Battle Royale!",
                    DateTime.UtcNow, stoppingToken))
                {
                    logger.LogInformation("Battle Royale prize: event {Key}, place {Place}, character {Character}, {Gold} gold", result.Key, prize.Place,
                        prize.CharacterId, prize.Gold);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not pay a Battle Royale's prizes");
            }
        }
    }
}
