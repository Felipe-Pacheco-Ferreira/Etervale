using PristonTale.Application.Saves;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Repositories;
using PristonTale.Simulation.Items;

namespace PristonTale.Server.Hosting;

internal sealed class GameMailRewardService(GameMailReader reader, GameMailClaims claims, WorldServer world,
    WorldDataHolder catalog, ICharacterSaves saves, ILogger<GameMailRewardService> logger)
{
    public async Task<string> ClaimAsync(PlayerSession session, long mailId)
    {
        try
        {
            var mail = await reader.OpenAsync(session.AccountId, mailId, CancellationToken.None);
            if (mail is null || mail.Gold is < 0 or > int.MaxValue || (mail.Items.Length == 0 && mail.Gold == 0))
            {
                return "unavailable";
            }

            if (mail.ClaimedAt.HasValue)
            {
                return "already";
            }

            var items = new List<ItemDefinition>();
            foreach (var attachment in mail.Items)
            {
                var definition = catalog.Current.Items.All.FirstOrDefault(item =>
                    string.Equals(item.CatalogKey, attachment.ItemKey, StringComparison.OrdinalIgnoreCase));
                if (definition is null || attachment.Quantity is < 1 or > 100)
                {
                    return "unavailable";
                }

                items.AddRange(Enumerable.Repeat(definition, attachment.Quantity));
            }

            using var lease = await new DurableOperations(saves).AdmitAsync(session, TimeSpan.Zero, CancellationToken.None);
            return await lease.RunAsync(
                player => world.PrepareMailRewardAsync(player, rng => items.Select(item => ItemRoller.Create(item, 1, rng)), (int)mail.Gold),
                (player, prepared) => world.CommitMailRewardAsync(player, prepared, (int)mail.Gold),
                prepared => prepared.Outcome ? "ok" : "space",
                failure => failure == DurableFailure.Conflict ? "already" : "retry",
                async (player, prepared) => await claims.StoreAsync(mailId, player.Stamp(prepared.Snapshot!)) ? null : DurableFailure.Conflict);
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or WorldBusyException)
        {
            logger.LogWarning(exception, "Mail claim failed for {MailId}", mailId);
            return "retry";
        }
    }
}
