using System.Globalization;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Gameplay.World;

public enum HuntLogKind : byte
{
    Started,
    Stopped,
    Death,
    Joined,
    Left
}

// Why the free hunt stopped, worded by the page in the player's language.
public enum HuntStopReason : byte
{
    Manual,
    MapChange,
    NoParty,
    FullBag,
    NoLifePotion,
    PickupRefused,
    TownAfterDeath,
    ProfileImported,
    Interface
}

// A log line: the local time, what happened, why it stopped and the name it concerns (the killer, the member).
public readonly record struct HuntLogEntry(string Time, HuntLogKind Kind, HuntStopReason Reason, string Name)
{
    private const char Separator = '|';

    public string Write() => string.Join(Separator, Time, Kind, Reason, Name);

    public static bool TryRead(string text, out HuntLogEntry entry)
    {
        entry = default;
        var parts = text?.Split(Separator, 4);
        if (parts is not { Length: 4 } || !Enum.TryParse<HuntLogKind>(parts[1], out var kind) || !Enum.TryParse<HuntStopReason>(parts[2], out var reason)
            || !Enum.IsDefined(kind) || !Enum.IsDefined(reason))
        {
            return false;
        }

        entry = new HuntLogEntry(parts[0], kind, reason, parts[3]);
        return true;
    }
}

public sealed record HuntTotals(long Seconds, ulong Experience, ulong ExperienceLost, long Gold, long GoldSpent,
    long Damage, int Kills, int Deaths, int Levels, int HP, int MP, int RES)
{
    public static readonly HuntTotals Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public long DroppedGold { get; init; }
    public int Collected { get; init; }
    public double RecentExperiencePerHour { get; init; }
    public double RecentDamagePerSecond { get; init; }
    public int RecentKills { get; init; }
    public IReadOnlyList<HuntLoot> Items { get; init; } = [];
    public IReadOnlyList<HuntMember> Members { get; init; } = [];
}

public readonly record struct HuntMember(ulong Id, string Name, long Damage, int Kills)
{
    public ulong Experience { get; init; }
    public int Deaths { get; init; }
    public double RecentDamagePerSecond { get; init; }
    public bool Active { get; init; }
}

public readonly record struct HuntLoot(string Code, string Name, int Count);

// What a character keeps of its hunts beside the client settings: the totals (without the party) and the latest log lines.
public sealed record HuntRecord(HuntTotals Totals, string[] Log);

public sealed partial class FreeHuntSession
{
    public const int MaxLogLines = 300;
    // The DPS shown counts the last thirty seconds; the hour rates and recent kills the last ten minutes.
    private const long RecentSampleSeconds = 30;
    private const long WindowSeconds = 600;
    private readonly record struct HuntSample(long Seconds, ulong Experience, long Damage, int Kills);
    private readonly Queue<HuntLogEntry> _log = new();
    private readonly Dictionary<ulong, HuntMemberLedger> _members = [];
    private readonly Dictionary<string, HuntLoot> _collected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<HuntSample> _samples = new();
    private readonly int[] _usedPotions = new int[3];
    private IReadOnlyList<PartyHuntInfo> _lastPartyHunt = [];
    private bool _hasPartyHunt;
    private OwnerState _lastStatisticsOwner;
    private string _fatalHitBy;
    private int? _fatalHitField;
    private long _fatalHitAt;
    private long _statisticsAt;
    private long _activeMs;
    private long _damage;
    private long _gold;
    private long _spentGold;
    private long _pickedGold;
    private int _pickedItems;
    private ulong _gainedExperience;
    private ulong _lostExperience;
    private int _levels;
    private int _kills;
    private int _deaths;
    private bool _tracking;

    public HuntTotals Totals { get; private set; } = HuntTotals.Empty;

    public bool StatisticsResting { get; private set; }

    // Raised with each log line, so the page knows to read the log again.
    public int LogVersion { get; private set; }

    public IReadOnlyCollection<HuntLogEntry> LogEntries => _log;

    private bool Counting => !StatisticsResting && (!Settings.CountOnlyHunting || _enabled);

    public void RestStatistics(bool on) => StatisticsResting = on;

    public void ClearLog()
    {
        _log.Clear();
        LogVersion++;
    }

    public void ResetStatistics()
    {
        _damage = _gold = _spentGold = _activeMs = 0;
        _gainedExperience = _lostExperience = 0;
        _kills = _deaths = _levels = 0;
        Array.Clear(_usedPotions);
        _pickedGold = 0;
        _pickedItems = 0;
        _collected.Clear();
        _samples.Clear();
        _lastStatisticsOwner = null;
        _statisticsAt = 0;
    }

    public void ResetGroup()
    {
        _members.Clear();
        ObservePartyHunt(_lastPartyHunt, Environment.TickCount64);
    }

    public HuntRecord Record()
    {
        var lines = new string[_log.Count];
        var index = 0;
        foreach (var entry in _log)
        {
            lines[index++] = entry.Write();
        }

        return new HuntRecord(Totals with { Members = [], RecentDamagePerSecond = 0, RecentExperiencePerHour = 0, RecentKills = 0 }, lines);
    }

    public void Restore(HuntRecord record)
    {
        _log.Clear();
        foreach (var line in record?.Log ?? [])
        {
            if (HuntLogEntry.TryRead(line, out var entry))
            {
                _log.Enqueue(entry);
            }
        }

        while (_log.Count > MaxLogLines)
        {
            _log.Dequeue();
        }

        LogVersion++;
        if (record?.Totals is not { } saved)
        {
            return;
        }

        _activeMs = saved.Seconds * 1000;
        _damage = saved.Damage;
        _gold = saved.Gold;
        _spentGold = saved.GoldSpent;
        _gainedExperience = saved.Experience;
        _lostExperience = saved.ExperienceLost;
        _deaths = saved.Deaths;
        _kills = saved.Kills;
        _levels = saved.Levels;
        _usedPotions[0] = saved.HP;
        _usedPotions[1] = saved.MP;
        _usedPotions[2] = saved.RES;
        _pickedGold = saved.DroppedGold;
        _pickedItems = saved.Collected;
        _collected.Clear();
        foreach (var item in saved.Items ?? [])
        {
            _collected[item.Code] = item;
        }

        Totals = saved with { Members = [] };
    }

    private void Log(HuntLogKind kind, HuntStopReason reason = HuntStopReason.Manual, string name = "")
    {
        _log.Enqueue(new HuntLogEntry(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), kind, reason, name ?? ""));
        while (_log.Count > MaxLogLines)
        {
            _log.Dequeue();
        }

        LogVersion++;
    }

    // Once a second: the time hunted, the samples behind the rates and the totals the page shows.
    private void RefreshStatistics(long now)
    {
        if (now - _statisticsAt < 1000)
        {
            return;
        }

        if (_tracking && _statisticsAt != 0)
        {
            _activeMs += now - _statisticsAt;
        }

        _tracking = Counting;
        _statisticsAt = now;
        var seconds = _activeMs / 1000;
        if (_tracking)
        {
            _samples.Enqueue(new HuntSample(seconds, _gainedExperience, _damage, _kills));
        }

        while (_samples.Count > 1 && _samples.TryPeek(out var oldest) && seconds - oldest.Seconds > WindowSeconds)
        {
            _samples.Dequeue();
        }

        var first = _samples.TryPeek(out var origin) ? origin : default;
        var recent = first;
        foreach (var sample in _samples)
        {
            recent = sample;
            if (seconds - sample.Seconds <= RecentSampleSeconds)
            {
                break;
            }
        }

        var group = new HuntMember[_members.Count];
        var groupIndex = 0;
        foreach (var member in _members.Values)
        {
            group[groupIndex++] = member.Snapshot(now);
        }

        Totals = new HuntTotals(seconds, _gainedExperience, _lostExperience, _gold, _spentGold, _damage, _kills, _deaths, _levels,
            _usedPotions[0], _usedPotions[1], _usedPotions[2])
        {
            DroppedGold = _pickedGold,
            Collected = _pickedItems,
            Items = [.. _collected.Values],
            Members = group,
            RecentExperiencePerHour = _samples.Count > 1 && seconds > first.Seconds ? (_gainedExperience - first.Experience) * 3600d / (seconds - first.Seconds) : 0,
            RecentDamagePerSecond = _samples.Count > 1 && seconds > recent.Seconds ? (_damage - recent.Damage) / (double)(seconds - recent.Seconds) : 0,
            RecentKills = _kills - first.Kills
        };
    }

    // A drop the server confirmed picked up (GameScene.GroundPickup).
    public void ObservePickup(GroundDropView drop)
    {
        if (!Counting)
        {
            return;
        }

        if (drop.Gold)
        {
            _pickedGold += drop.Amount;
            return;
        }

        _pickedItems += drop.Amount;
        var prior = _collected.GetValueOrDefault(drop.CatalogKey);
        _collected[drop.CatalogKey] = new HuntLoot(drop.CatalogKey, drop.Name, prior.Count + drop.Amount);
    }

    private void ObserveStatistics(PresenceUpdate update, long now)
    {
        var self = scene.SelfEntityId;
        foreach (var hit in update.Events)
        {
            if (hit.Kind == CombatEventKind.Hit && hit.TargetId == self && hit.Killed)
            {
                _fatalHitBy = _monsters.TryGetValue(hit.AttackerId, out var monster) ? monster.Name : MemberName(hit.AttackerId);
                _fatalHitField = update.FieldId;
                _fatalHitAt = now;
            }
        }

        var counting = Counting;
        if (update.Owner is { } state)
        {
            if (counting && _lastStatisticsOwner is { } before)
            {
                if (state.Experience >= before.Experience)
                {
                    _gainedExperience += state.Experience - before.Experience;
                }
                else
                {
                    _lostExperience += before.Experience - state.Experience;
                }

                var delta = state.Money - before.Money;
                _gold += Math.Max(0, delta);
                _spentGold += Math.Max(0, -delta);
                _levels += Math.Max(0, state.Level - before.Level);
                if (before.Life > 0 && state.Life <= 0)
                {
                    _deaths++;
                }
            }

            if (_lastStatisticsOwner is { Life: > 0 } && state.Life <= 0)
            {
                // The killer is named only from a fatal hit seen just before, in the same field.
                var killer = update.FieldId == _fatalHitField && now - _fatalHitAt <= 1500 ? _fatalHitBy : null;
                Log(HuntLogKind.Death, name: killer ?? "");
                _fatalHitBy = null;
            }

            _lastStatisticsOwner = state;
        }

        if (update.Party is { } updated)
        {
            foreach (var person in updated.Members)
            {
                if (!_party.Contains(person.EntityId))
                {
                    Log(HuntLogKind.Joined, name: person.Name);
                }
            }

            foreach (var person in _party.Members)
            {
                if (!updated.Contains(person.EntityId))
                {
                    Log(HuntLogKind.Left, name: person.Name);
                    if (_members.TryGetValue(person.EntityId, out var ledger))
                    {
                        ledger.Freeze(now);
                    }
                }
            }

            _party = updated;
        }

        if (update.PartyHunt is { } group)
        {
            if (!_hasPartyHunt)
            {
                _members.Clear();
                _hasPartyHunt = true;
            }

            _lastPartyHunt = group;
            ObservePartyHunt(group, now);
        }

        foreach (var hit in update.Events)
        {
            if (counting && hit.Kind == CombatEventKind.PotionApplied && hit.TargetId == self)
            {
                var resources = (PotionResource)hit.Resources;
                _usedPotions[0] += resources.HasFlag(PotionResource.Life) ? 1 : 0;
                _usedPotions[1] += resources.HasFlag(PotionResource.Mana) ? 1 : 0;
                _usedPotions[2] += resources.HasFlag(PotionResource.Stamina) ? 1 : 0;
            }

            if (hit.Kind != CombatEventKind.Hit || !_monsters.ContainsKey(hit.TargetId))
            {
                continue;
            }

            if (counting && (hit.AttackerId == self || _ownSummons.Contains(hit.AttackerId)))
            {
                _damage += Math.Max(0, hit.Damage);
                _kills += hit.Killed ? 1 : 0;
            }

            // A server without the party hunt counters: the members' hits seen here stand in for them.
            if (!_hasPartyHunt && MemberOf(hit.AttackerId) is { } member)
            {
                Ledger(member).Add((ulong)Math.Max(0, hit.Damage), 0, hit.Killed ? 1u : 0u, 0);
            }
        }
    }

    private string MemberName(ulong entity) => MemberOf(entity)?.Name;

    private PartyMemberInfo MemberOf(ulong entity)
    {
        foreach (var member in _party.Members)
        {
            if (member.EntityId == entity)
            {
                return member;
            }
        }

        return null;
    }

    private void ObservePartyHunt(IReadOnlyList<PartyHuntInfo> group, long now)
    {
        foreach (var member in _party.Members)
        {
            foreach (var entry in group)
            {
                if (entry.EntityId == member.EntityId)
                {
                    Ledger(member).Observe(entry.Counters, now);
                    break;
                }
            }
        }
    }

    private HuntMemberLedger Ledger(PartyMemberInfo member)
    {
        if (!_members.TryGetValue(member.EntityId, out var ledger))
        {
            ledger = new HuntMemberLedger(member.EntityId, member.Name);
            _members.Add(member.EntityId, ledger);
        }

        return ledger;
    }

    // A member's totals since it was first seen in the party or the panel was reset: the server's counters grow, and each
    // change adds its difference; once it leaves, its totals stay frozen for the session.
    private sealed class HuntMemberLedger(ulong id, string name)
    {
        private const long RecentWindowMs = 30_000;
        private readonly Queue<(long At, long Damage)> _samples = new();
        private PartyHuntCounters? _last;
        private HuntMember _totals = new(id, name, 0, 0);

        public void Observe(PartyHuntCounters next, long now)
        {
            if (_last is { } previous)
            {
                Add(next.Damage - Math.Min(next.Damage, previous.Damage), next.Experience - Math.Min(next.Experience, previous.Experience),
                    next.Kills - Math.Min(next.Kills, previous.Kills), next.Deaths - Math.Min(next.Deaths, previous.Deaths));
            }
            else
            {
                _totals = _totals with { Active = true, RecentDamagePerSecond = 0 };
                _samples.Enqueue((now, _totals.Damage));
            }

            _last = next;
        }

        public void Add(ulong damage, ulong experience, uint kills, uint deaths)
        {
            _totals = _totals with
            {
                Damage = _totals.Damage + (long)Math.Min(damage, (ulong)(long.MaxValue - _totals.Damage)),
                Experience = _totals.Experience + Math.Min(experience, ulong.MaxValue - _totals.Experience),
                Kills = _totals.Kills + (int)Math.Min(kills, (uint)(int.MaxValue - _totals.Kills)),
                Deaths = _totals.Deaths + (int)Math.Min(deaths, (uint)(int.MaxValue - _totals.Deaths)),
                Active = true
            };
        }

        public HuntMember Snapshot(long now)
        {
            if (!_totals.Active)
            {
                return _totals;
            }

            while (_samples.TryPeek(out var start) && now - start.At > RecentWindowMs)
            {
                _samples.Dequeue();
            }

            var dps = _samples.TryPeek(out var first) && now > first.At ? (_totals.Damage - first.Damage) * 1000d / (now - first.At) : 0;
            _samples.Enqueue((now, _totals.Damage));
            return _totals with { RecentDamagePerSecond = dps };
        }

        public void Freeze(long now)
        {
            _totals = Snapshot(now) with { Active = false };
            _samples.Clear();
            _last = null;
        }
    }
}
