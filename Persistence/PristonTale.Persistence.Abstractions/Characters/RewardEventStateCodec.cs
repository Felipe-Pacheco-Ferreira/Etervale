using System.Text.Json;
using PristonTale.Simulation.Events;

namespace PristonTale.Persistence.Abstractions.Characters;

// The characters.events column: a character's events and rewards (RewardEventBook). Days are DateOnly day numbers of
// the server's local calendar; claims a bit an entry index.
public sealed record SavedRewardEvents(
    int Version,
    int WelcomeFirstDay,
    int WelcomeLastDay,
    int WelcomeDays,
    int WelcomeClaimed,
    int WeekStart,
    int[] Weekly,
    int WeeklyClaimed,
    int Day,
    int[] Daily,
    int DailyClaimed,
    int LevelClaimed);

public static class RewardEventStateCodec
{
    public const int CurrentVersion = 1;

    public static SavedRewardEvents Capture(RewardEventBook book)
    {
        var state = book.Capture();
        return new SavedRewardEvents(CurrentVersion, state.WelcomeFirstDay, state.WelcomeLastDay, state.WelcomeDays, state.WelcomeClaimed,
            state.WeekStart, [.. state.Weekly], state.WeeklyClaimed, state.Day, [.. state.Daily], state.DailyClaimed, state.LevelClaimed);
    }

    // {} (or no value, a record built without the column) reads as a book that counted nothing; anything else is
    // restored strictly (RewardEventBook.Restore).
    public static RewardEventState Read(JsonText json)
    {
        if (json.Value is null || CharacterStateCodec.IsBlank(json, JsonValueKind.Object))
        {
            return RewardEventState.Empty;
        }

        var saved = JsonSerializer.Deserialize<SavedRewardEvents>(json.Value, CharacterStateCodec.Options);
        if (saved is null || saved.Version != CurrentVersion || saved.Weekly is null || saved.Daily is null)
        {
            throw new InvalidDataException("Unsupported reward-events version.");
        }

        return new RewardEventState(saved.WelcomeFirstDay, saved.WelcomeLastDay, saved.WelcomeDays, saved.WelcomeClaimed, saved.WeekStart, saved.Weekly,
            saved.WeeklyClaimed, saved.Day, saved.Daily, saved.DailyClaimed, saved.LevelClaimed);
    }
}
