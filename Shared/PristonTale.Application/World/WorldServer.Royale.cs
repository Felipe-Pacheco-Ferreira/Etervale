using System.Globalization;
using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

// A finished Battle Royale: its key (RoyaleRules.EventKey) and every player's place, best first.
public sealed record RoyaleResult(long Key, IReadOnlyList<RoyalePlace> Places);

// The Battle Royale (RoyaleRules), on the tick thread. Registration opens RegistrationMinutes before each of
// WorldServerOptions.Royale's hours, told to everyone; "/royale" registers the player (keeping where it registered
// from) and "/royale sair" takes it out. At the hour the registered players still free and with no character change
// in flight start, up to MaximumPlayers; fewer than MinimumPlayers cancel it. Each fights as its stand-in
// (WorldPlayer.TakeStandIn, RoyaleKits) at its place around the arena's centre, its running statuses dropped. A blow or
// the zone that would kill takes the player out (StepFights, TakeOutOfRoyale) to watch; dying, leaving the field (which
// gives its own character back where it went) or the world does too. Potions appear in the zone. At the end every player
// still in the event gets its own character back and goes, with its whole life, to where it registered from, gains its
// experience and is told its place; everyone reads the winner. RoyaleResultRecorded hands the places to the prizes'
// storage.
public sealed partial class WorldServer
{
    private readonly List<ulong> _royaleRegistered = [];
    private readonly CombatRng _royaleRng = new(0x0B7A1E);
    private RoyaleEvent? _royale;
    private RoyaleView? _royaleRegisteredView;
    private long _nextRoyaleId;
    private bool _royaleOpen;
    private bool _nextPotionLife;

    // A finished event's places, on the tick thread: the receiver pays the prizes off the tick.
    public Action<RoyaleResult>? RoyaleResultRecorded { get; set; }

    // The running event, for tests and the snapshot.
    internal RoyaleEvent? RunningRoyale => _royale;

    // Registers the player (join) or takes it out of the registration.
    public Task<RoyaleJoin?> JoinRoyaleAsync(PlayerSession session, bool join) =>
        EnqueueAsync<RoyaleJoin?>(session.EntityId, () => TryGetPlaying(session, out var connection) ? JoinRoyale(connection, join) : null);

    private RoyaleJoin JoinRoyale(Connection connection, bool join)
    {
        var player = connection.Player;
        var answer = join ? Register(connection) : Unregister(player);
        Notify(player.EntityId, ChatKind.System, answer switch
        {
            RoyaleJoin.Registered => GameText.Of(GameTextId.RoyaleRegistered, _royaleRegistered.Count),
            RoyaleJoin.Left => GameText.Of(GameTextId.RoyaleLeft),
            RoyaleJoin.Closed => GameText.Of(GameTextId.RoyaleClosed, RoyaleHours()),
            RoyaleJoin.Level => GameText.Of(GameTextId.RoyaleLevel, RoyaleRules.MinimumLevel),
            RoyaleJoin.Full => GameText.Of(GameTextId.RoyaleFull, RoyaleRules.MaximumPlayers),
            RoyaleJoin.NotRegistered => GameText.Of(GameTextId.RoyaleNotRegistered),
            _ => GameText.Of(GameTextId.RoyaleBusy)
        });
        return answer;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private string RoyaleHours() =>
        string.Join(", ", Simulation.World.Calendar.EventSchedule.Hours(_options.Royale.Hours).Select(static hour => hour.ToString("00", CultureInfo.InvariantCulture) + ":00"));

    private RoyaleJoin Register(Connection connection)
    {
        var player = connection.Player;
        if (!_royaleOpen)
        {
            return RoyaleJoin.Closed;
        }

        if (player.RoyaleEntry is not null)
        {
            return RoyaleJoin.Registered;
        }

        if (player.Level < RoyaleRules.MinimumLevel)
        {
            return RoyaleJoin.Level;
        }

        if (!FreeForRoyale(connection))
        {
            return RoyaleJoin.Busy;
        }

        if (_royaleRegistered.Count >= RoyaleRules.MaximumPlayers)
        {
            return RoyaleJoin.Full;
        }

        player.RoyaleEntry = new RoyaleEntry(connection.FieldId, player.Position);
        _royaleRegistered.Add(player.EntityId);
        return RoyaleJoin.Registered;
    }

    private RoyaleJoin Unregister(WorldPlayer player)
    {
        if (player.RoyaleEntry is null)
        {
            return RoyaleJoin.NotRegistered;
        }

        player.RoyaleEntry = null;
        _royaleRegistered.Remove(player.EntityId);
        return RoyaleJoin.Left;
    }

    // Free to register or to start: alive, shown, in no duel, arena queue, match or event, and not in a field a return
    // scroll cannot leave (Bellatra, the quest arena), since the event sends it back where it registered.
    private static bool FreeForRoyale(Connection connection)
    {
        var player = connection.Player;
        return !player.IsDead && !player.Hidden && player.Duel is null && player.Arena is null && player.ArenaQueued is null && player.Royale is null
            && Array.IndexOf(ItemUses.TravelRules.ReturnBlockedFields, connection.FieldId) < 0;
    }

    // Tick thread, from StepFights.
    private void StepRoyale(float dtMs)
    {
        var local = _clock.GetLocalNow();
        var hours = _options.Royale.Hours;
        var open = RoyaleRules.RegistrationOpen(hours, local);
        if (open != _royaleOpen)
        {
            _royaleOpen = open;
            if (open)
            {
                NotifyEveryone(GameText.Of(GameTextId.RoyaleOpen, Number(RoyaleRules.MinutesToStart(local)), Number(RoyaleRules.MinimumLevel)));
            }
            else if (RoyaleRules.Starts(hours, local) && _royale is null)
            {
                StartRoyale(local);
            }
            else
            {
                ClearRegistrations(GameText.Of(GameTextId.RoyaleSkipped));
            }
        }

        if (_royale is { } royale)
        {
            StepRoyaleEvent(royale, dtMs);
        }
    }

    private void ClearRegistrations(GameText told)
    {
        foreach (var id in _royaleRegistered)
        {
            if (_connections.TryGetValue(id, out var connection) && connection.Player.RoyaleEntry is not null)
            {
                connection.Player.RoyaleEntry = null;
                Notify(id, ChatKind.System, told);
            }
        }

        _royaleRegistered.Clear();
    }

    private void StartRoyale(DateTimeOffset local)
    {
        var entrants = new List<Connection>();
        foreach (var id in _royaleRegistered)
        {
            if (!_connections.TryGetValue(id, out var connection) || connection.Player.RoyaleEntry is null)
            {
                continue;
            }

            // A character change in flight would commit onto the stand-in: that player sits this one out.
            if (FreeForRoyale(connection) && connection.Session?.SaveGate.CurrentCount != 0)
            {
                entrants.Add(connection);
            }
            else
            {
                connection.Player.RoyaleEntry = null;
                Notify(id, ChatKind.System, GameText.Of(GameTextId.RoyaleSkipped));
            }
        }

        _royaleRegistered.Clear();
        if (entrants.Count < RoyaleRules.MinimumPlayers)
        {
            foreach (var connection in entrants)
            {
                connection.Player.RoyaleEntry = null;
            }

            NotifyEveryone(GameText.Of(GameTextId.RoyaleCancelled, Number(entrants.Count), Number(RoyaleRules.MinimumPlayers)));
            return;
        }

        var royale = new RoyaleEvent(++_nextRoyaleId, RoyaleRules.EventKey(local));
        var field = GetField(RoyaleRules.FieldCode);
        var items = _worldData.Current.Items;
        var kits = new Dictionary<CharacterClass, ItemInstance[]>();
        for (var index = 0; index < entrants.Count; index++)
        {
            var connection = entrants[index];
            var player = connection.Player;
            var seat = new RoyaleSeat(royale, index, player.EntityId, player.Character.Name, connection.Session?.Loaded?.Id ?? 0L, player.RoyaleEntry!);
            royale.Seats.Add(seat);
            player.RoyaleEntry = null;
            player.Royale = seat;
            player.Dismount();
            if (!kits.TryGetValue(player.Character.Class, out var kit))
            {
                kits[player.Character.Class] = kit = RoyaleKits.Kit(player.Character.Class, items);
            }

            player.TakeStandIn(RoyaleKits.StandIn(player.Character, kit));
            var place = field.Terrain.PlaceOnFloor(RoyaleRules.Place(index, entrants.Count));
            Relocate(connection, RoyaleRules.FieldCode, place, inVillage: false);
            field.Statuses.Take(player.EntityId);
            player.EnterField(place, inVillage: false);
        }

        _royale = royale;
        field.EventDrops = royale.Potions;
        NotifyEveryone(GameText.Of(GameTextId.RoyaleStarted, entrants.Count));
    }

    private void StepRoyaleEvent(RoyaleEvent royale, float dtMs)
    {
        royale.Step(dtMs);
        foreach (var seat in royale.Seats)
        {
            if (seat.Out && !_connections.ContainsKey(seat.EntityId))
            {
                continue;
            }

            var inEvent = _connections.TryGetValue(seat.EntityId, out var connection) && ReferenceEquals(connection.Player.Royale, seat);
            if (inEvent && connection!.FieldId != RoyaleRules.FieldCode)
            {
                // Out of the arena's field on its own: it stays where it went, with its own character and none of what the
                // stand-in was given (statuses).
                _fields[connection.FieldId].Statuses.Take(seat.EntityId);
                connection.Player.ReturnHome();
                connection.Player.Royale = null;
                inEvent = false;
            }

            if (!inEvent)
            {
                royale.TakeOut(seat);
            }
            else if (connection!.Player.IsDead)
            {
                // Nobody dies here (a game master's kill does): it watches standing, as the others out do.
                TakeOutOfRoyale(seat);
                connection.Player.Revive(1, 0f);
            }
        }

        royale.ZoneMsLeft -= dtMs;
        if (royale.ZoneMsLeft <= 0f)
        {
            royale.ZoneMsLeft += RoyaleRules.ZoneDamageEveryMs;
            HurtOutsideZone(royale);
        }

        royale.PotionMsLeft -= dtMs;
        if (royale.PotionMsLeft <= 0f)
        {
            royale.PotionMsLeft += RoyaleRules.PotionEveryMs;
            PlacePotions(royale);
        }

        if (RoyaleRules.Over(royale.Standing, royale.ElapsedMs))
        {
            EndRoyale(royale);
        }
    }

    // The zone takes its share of life from everyone standing outside it; what would kill leaves 1 and takes it out.
    private void HurtOutsideZone(RoyaleEvent royale)
    {
        var radius = royale.Radius;
        foreach (var seat in royale.Seats)
        {
            if (seat.Out || !_connections.TryGetValue(seat.EntityId, out var connection) || RoyaleRules.InZone(connection.Player.Position, radius))
            {
                continue;
            }

            var player = connection.Player;
            var damage = RoyaleRules.ZoneDamage(player.MaxLife);
            if (player.Life <= damage)
            {
                player.ApplyDamage(player.Life - 1);
                TakeOutOfRoyale(seat);
            }
            else
            {
                player.ApplyDamage(damage);
            }
        }
    }

    private void PlacePotions(RoyaleEvent royale)
    {
        var field = GetField(RoyaleRules.FieldCode);
        for (var count = RoyaleRules.PotionsFor(royale.Standing); count > 0; count--)
        {
            var spot = RoyaleRules.PointInZone(royale.Radius, _royaleRng.NextUnit(), _royaleRng.NextUnit());
            _nextPotionLife = !_nextPotionLife;
            var id = field.GroundLoot.PlaceEventItem(_nextPotionLife ? RoyaleRules.LifePotion : RoyaleRules.ManaPotion, field.Terrain.PlaceOnFloor(spot),
                field.NowMs, _royaleRng);
            if (id != Guid.Empty)
            {
                royale.Potions.Add(id);
            }
        }
    }

    // A blow, the zone or a death left the player out: it watches until the end.
    private void TakeOutOfRoyale(RoyaleSeat seat)
    {
        if (seat.Out)
        {
            return;
        }

        seat.Event.TakeOut(seat);
        if (_connections.TryGetValue(seat.EntityId, out var connection))
        {
            connection.Player.ClearAttackTarget();
        }

        Notify(seat.EntityId, ChatKind.System, GameText.Of(GameTextId.RoyaleOut, seat.Event.Standing));
    }

    private void EndRoyale(RoyaleEvent royale)
    {
        _royale = null;
        var field = GetField(RoyaleRules.FieldCode);
        field.EventDrops = null;
        foreach (var potion in royale.Potions)
        {
            field.GroundLoot.Withdraw(potion);
        }

        var ranking = royale.Ranking();
        var places = new List<RoyalePlace>(ranking.Count);
        for (var index = 0; index < ranking.Count; index++)
        {
            var seat = ranking[index];
            var experience = 0UL;
            if (_connections.TryGetValue(seat.EntityId, out var connection) && ReferenceEquals(connection.Player.Royale, seat))
            {
                var player = connection.Player;
                field.Statuses.Take(seat.EntityId);
                player.ReturnHome();
                player.Royale = null;
                SendBack(connection, (seat.Origin.FieldId, seat.Origin.Position));
                experience = RoyaleRules.Experience(player.Level, player.Character.Experience);
                player.Character.AwardExperience(experience);
                Notify(seat.EntityId, ChatKind.System, GameText.Of(GameTextId.RoyalePlace, Number(index + 1), experience.ToString(CultureInfo.InvariantCulture)));
            }

            places.Add(new RoyalePlace(index + 1, seat.CharacterId, seat.Name, experience));
        }

        NotifyEveryone(GameText.Of(GameTextId.RoyaleWinner, ranking[0].Name));
        PassTitle(new TitleHolder(Simulation.Titles.TitleCode.Survivor, ranking[0].CharacterId, ranking[0].Name));
        RoyaleResultRecorded?.Invoke(new RoyaleResult(royale.Key, places));
    }

    // A picked potion is drunk at once by a player standing in the event: a share of its whole life or mana.
    internal static bool DrinkRoyalePotion(WorldPlayer player, uint code)
    {
        if (player.Royale is not { Out: false } || !RoyaleRules.IsPotion(code))
        {
            return false;
        }

        var character = player.Character;
        if (code == RoyaleRules.LifePotion)
        {
            player.Heal(RoyaleRules.PotionRestore(character.MaxLife));
        }
        else
        {
            character.Mana = Math.Min(character.MaxMana, character.Mana + RoyaleRules.PotionRestore(character.MaxMana));
        }

        return true;
    }

    // Where a save puts a player: one in an arena match or the Battle Royale where it goes back to, else where it is.
    private static (int FieldId, Vector3 Position) SavedPlace(Connection connection) =>
        connection.Player.Royale is { } seat ? (seat.Origin.FieldId, seat.Origin.Position)
        : connection.Player.Arena is { } arena ? arena.Match.Origins[arena.Side][arena.Member]
        : (connection.FieldId, connection.Player.Position);

    // What a player's HUD shows of the Battle Royale, null when it neither registered nor fights.
    private RoyaleView? RoyaleViewOf(WorldPlayer player)
    {
        if (player.Royale is { } seat)
        {
            return seat.Event.ViewOf(seat);
        }

        if (player.RoyaleEntry is null)
        {
            return null;
        }

        if (_royaleRegisteredView?.Total != _royaleRegistered.Count)
        {
            _royaleRegisteredView = new RoyaleView(true, 0, _royaleRegistered.Count, 0, 0, 0, false);
        }

        return _royaleRegisteredView;
    }
}
