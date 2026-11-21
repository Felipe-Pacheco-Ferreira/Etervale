namespace PristonTale.Persistence.Abstractions.BossRoulette;

// A drawn boss roulette as stored: the field, the boss, the winner (0 when unknown), the prize's catalog key and every
// entrant (JSON: character, name, damage and chance).
public sealed record BossRouletteRecord(int FieldId, int BossCode, string BossName, long WinnerCharacterId, string PrizeKey, string Entrants);

// The boss roulette's draws (boss_roulette): each kept for audit, its prize mailed to the winner.
public interface IBossRouletteRepository
{
    // Keeps the draw and mails the prize (one item) to the winner's account with this subject and body; the mail's id, or
    // null when the winner's character does not exist (the draw is kept without a mail).
    Task<long?> RecordAsync(BossRouletteRecord draw, string subject, string body, DateTime nowUtc, CancellationToken ct = default);
}
