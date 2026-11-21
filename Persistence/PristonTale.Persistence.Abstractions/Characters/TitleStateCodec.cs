using System.Text.Json;
using PristonTale.Simulation.Titles;

namespace PristonTale.Persistence.Abstractions.Characters;

// The characters.titles column: a character's titles (TitleBook). Earned holds a bit per permanent title code; Shown is
// a title code (0 none).
public sealed record SavedTitles(int Version, uint Earned, int Shown, long MonsterKills, long BossKills, int BestAging);

public static class TitleStateCodec
{
    public const int CurrentVersion = 1;

    public static SavedTitles Capture(TitleBook book)
    {
        var state = book.Capture();
        return new SavedTitles(CurrentVersion, state.Earned, (int)state.Shown, state.MonsterKills, state.BossKills, state.BestAging);
    }

    // {} (or no value, a record built without the column) reads as a character with no title and nothing counted;
    // anything else is restored strictly (TitleBook.Restore).
    public static TitleState Read(JsonText json)
    {
        if (json.Value is null || CharacterStateCodec.IsBlank(json, JsonValueKind.Object))
        {
            return TitleState.Empty;
        }

        var saved = JsonSerializer.Deserialize<SavedTitles>(json.Value, CharacterStateCodec.Options);
        if (saved is null || saved.Version != CurrentVersion || saved.Shown is < 0 or > byte.MaxValue)
        {
            throw new InvalidDataException("Unsupported titles version.");
        }

        return new TitleState(saved.Earned, (TitleCode)saved.Shown, saved.MonsterKills, saved.BossKills, saved.BestAging);
    }
}
