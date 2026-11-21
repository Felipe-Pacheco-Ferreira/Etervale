using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

// One in a drawn boss roulette, as stored for audit: the character (0 when unknown), its name, the damage it dealt and
// its chance (0 to 1).
public sealed record BossRouletteEntry(long CharacterId, string Name, long Damage, float Chance);

// A drawn boss roulette: the field, the boss (its code and name), every entrant, the winner's index and the prize's
// catalog key and name.
public sealed record BossRouletteResult(int FieldId, int BossCode, string BossName, IReadOnlyList<BossRouletteEntry> Entries, int Winner, string PrizeKey,
    string PrizeName)
{
    public BossRouletteEntry WinnerEntry => Entries[Winner];
}

// The boss roulette (BossRoulette): the fields draw it when a scheduled boss dies; after the fields step every player is
// told the winner (BossRouletteWinner, which the client may hide), the winner its prize (BossRouletteWon), and
// BossRouletteDrawn hands the result to storage, which mails the prize and keeps the draw.
public sealed partial class WorldServer
{
    public Action<BossRouletteResult>? BossRouletteDrawn { get; set; }

    // Tick only, after the fields step.
    private void AnnounceRoulettes()
    {
        foreach (var field in _activeFields)
        {
            foreach (var draw in field.RouletteDraws)
            {
                var prizeName = _worldData.Current.Items.TryGet(ItemCodes.Parse(draw.Prize), out var prize) ? prize.Name : draw.Prize;
                var entries = new BossRouletteEntry[draw.Entrants.Length];
                for (var index = 0; index < entries.Length; index++)
                {
                    var entrant = draw.Entrants[index];
                    var characterId = _connections.TryGetValue(entrant.Player.EntityId, out var connection) ? CharacterIdOf(connection.Session) ?? 0 : 0;
                    entries[index] = new BossRouletteEntry(characterId, entrant.Player.Character.Name, entrant.Damage, entrant.Chance);
                }

                var winner = draw.Entrants[draw.Winner].Player;
                Announce(GameText.Of(GameTextId.BossRouletteWinner, winner.Character.Name, prizeName, draw.Boss.Name));
                Notify(winner.EntityId, ChatKind.System, GameText.Of(GameTextId.BossRouletteWon, prizeName, draw.Boss.Name));
                BossRouletteDrawn?.Invoke(new BossRouletteResult(draw.FieldId, draw.Boss.Code, draw.Boss.Name, entries, draw.Winner, draw.Prize, prizeName));
            }

            field.RouletteDraws.Clear();
        }
    }
}
