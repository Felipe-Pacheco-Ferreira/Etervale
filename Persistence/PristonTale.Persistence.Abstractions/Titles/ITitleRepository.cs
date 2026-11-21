namespace PristonTale.Persistence.Abstractions.Titles;

// Who holds a position title (TitleRules): the title code, the character and its name.
public sealed record TitleHolderRecord(int Title, long CharacterId, string Name);

// The position titles' holders (title_holders), one row per title.
public interface ITitleRepository
{
    Task<IReadOnlyList<TitleHolderRecord>> HoldersAsync(CancellationToken ct = default);

    // The title passes to this character, replacing whoever held it.
    Task SetHolderAsync(TitleHolderRecord holder, DateTime nowUtc, CancellationToken ct = default);
}
