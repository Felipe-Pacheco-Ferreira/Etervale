using System.Threading.Channels;
using PristonTale.Application.Castle;
using PristonTale.Application.Clans;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Arena;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Persistence.Abstractions.Titles;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Titles;

namespace PristonTale.Server.Hosting;

// The position titles (TitleRules): the holders kept in storage are handed to the world at start; every
// TitleRules.PositionCheck the positions read from storage are looked at again (the arena's first of the week before in
// each band, the chief of the clan that owns the castle, the first of the level ranking) and handed to the world, which
// passes a title only to another character. Each change the world makes (WorldServer.TitlePassed, on the tick, also the
// Battle Royale's winner) is queued and stored off the tick. A position nobody holds leaves the title where it is.
internal sealed class TitleService(WorldServer world, ITitleRepository titles, IArenaRepository arena, IRankingRepository rankings, CastleAppService castle,
    ClanAppService clans, ILogger<TitleService> logger) : BackgroundService
{
    private static readonly TimeSpan LoadRetry = TimeSpan.FromSeconds(30);

    private readonly Channel<TitleHolder> _passed = Channel.CreateUnbounded<TitleHolder>(new UnboundedChannelOptions { SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        world.TitlePassed = holder => _passed.Writer.TryWrite(holder);
        await LoadAsync(stoppingToken);
        var positions = CheckPositionsAsync(stoppingToken);
        await foreach (var holder in _passed.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await titles.SetHolderAsync(new TitleHolderRecord((int)holder.Title, holder.CharacterId, holder.Name), DateTime.UtcNow, stoppingToken);
                logger.LogInformation("Title {Title} passed to {Name} ({Character})", holder.Title, holder.Name, holder.CharacterId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not store the holder of title {Title}", holder.Title);
            }
        }

        await positions;
    }

    // Retried until it works: the world drops no shown position and the positions are not looked at before.
    private async Task LoadAsync(CancellationToken ct)
    {
        while (true)
        {
            try
            {
                var holders = new List<TitleHolder>();
                foreach (var row in await titles.HoldersAsync(ct))
                {
                    holders.Add(new TitleHolder((TitleCode)row.Title, row.CharacterId, row.Name));
                }

                await world.LoadTitleHoldersAsync(holders);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not load the title holders; trying again");
            }

            await Task.Delay(LoadRetry, ct);
        }
    }

    private async Task CheckPositionsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TitleRules.PositionCheck);
        do
        {
            try
            {
                var week = ArenaRules.WeekKey(world.LastArenaWeek());
                for (var band = 0; band < ArenaRules.Bands; band++)
                {
                    if ((await arena.TopAsync(week, band, 1, ct)) is [{ Points: > 0 } first])
                    {
                        await world.AwardTitleAsync(new TitleHolder(TitleRules.ArenaKing(band), first.CharacterId, first.Name));
                    }
                }

                if (castle.OwnerClanId is > 0 and var owner && clans.ChiefOf(owner) is { } chief)
                {
                    await world.AwardTitleAsync(new TitleHolder(TitleCode.CastleLord, chief.CharacterId, chief.Name));
                }

                if ((await rankings.TopAsync(null, 1, ct)) is [var strongest])
                {
                    await world.AwardTitleAsync(new TitleHolder(TitleCode.Strongest, strongest.Id, strongest.Name));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not check the title positions");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
