namespace PristonTale.Persistence.Abstractions.Royale;

// A Battle Royale place to pay: the place, the character and its gold.
public sealed record RoyalePrize(int Place, long CharacterId, long Gold);

// The Battle Royale's prizes (royale_prizes). An event is its server-local yyyymmddhh.
public interface IRoyaleRepository
{
    // Pays an event once: each place's character is mailed its gold (none for 0) with this subject and body; nothing
    // when the event was already paid. The places it paid.
    Task<IReadOnlyList<RoyalePrize>> AwardAsync(long eventKey, IReadOnlyList<RoyalePrize> prizes, string subject, string body, DateTime nowUtc,
        CancellationToken ct = default);
}
