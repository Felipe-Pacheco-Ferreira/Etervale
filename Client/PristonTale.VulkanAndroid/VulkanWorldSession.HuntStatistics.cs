using System.Collections.Concurrent;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;

internal sealed partial class VulkanWorldSession
{
    public sealed record HuntTotals(long Seconds, ulong Experience, ulong ExperienceLost, long Gold, long GoldSpent,
        long Damage, int Kills, int Deaths, int Levels, int HP, int MP, int RES, IReadOnlyList<HuntMember> Members)
    {
        public static readonly HuntTotals Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        public long DroppedGold { get; init; }
        public int Collected { get; init; }
        public double RecentExperiencePerHour { get; init; }
        public double RecentDamagePerSecond { get; init; }
        public int RecentKills { get; init; }
        public IReadOnlyList<HuntLoot> Items { get; init; } = [];
    }

    public readonly record struct HuntMember(ulong Id, string Name, long Damage, int Kills)
    {
        public ulong Experience { get; init; }
        public int Deaths { get; init; }
        public double RecentDamagePerSecond { get; init; }
        public bool Active { get; init; }
    }
    public readonly record struct HuntLoot(string Code, string Name, int Count);
    private readonly record struct HuntSample(long Seconds, ulong Experience, long Damage, int Kills);
    private readonly ConcurrentQueue<string> huntLog = new();
    private int huntLogVersion;
    private HuntTotals huntTotals = HuntTotals.Empty;
    private HuntTotals pendingHuntTotals;
    private OwnerState lastStatisticsOwner;
    private readonly Dictionary<ulong, HuntMemberLedger> memberTotals = [];
    private IReadOnlyList<PartyHuntInfo> lastPartyHunt = [];
    private bool hasPartyHunt;
    private string fatalHitBy;
    private int? fatalHitField;
    private long fatalHitAt;
    private readonly Dictionary<string, HuntLoot> collectedLoot = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<HuntSample> samples = new();
    private long statisticsAt;
    private long activeMs;
    private long damage;
    private long gold;
    private long spentGold;
    private long pickedGold;
    private int pickedItems;
    private ulong gainedExperience;
    private ulong lostExperience;
    private int levels;
    private int killsCount;
    private int deathsCount;
    private readonly int[] usedPotions = new int[3];
    private int resetHuntTotals;
    private int restHuntTotals;
    private int resetHuntGroup;
    private bool trackingStatistics;

    public HuntTotals HuntStatistics => Volatile.Read(ref huntTotals);
    public bool HuntStatisticsResting => Volatile.Read(ref restHuntTotals) != 0;
    public int HuntLogVersion => Volatile.Read(ref huntLogVersion);
    public string[] ReadHuntLog() => huntLog.ToArray();
    public void RestoreHuntLog(IEnumerable<string> lines)
    {
        foreach (var line in lines.TakeLast(300))
        {
            huntLog.Enqueue(line);
        }
    }

    public void ClearHuntLog()
    {
        huntLog.Clear();
        Interlocked.Increment(ref huntLogVersion);
    }

    public void ResetHuntStatistics() => Interlocked.Exchange(ref resetHuntTotals, 1);
    public void ResetHuntGroup() => Interlocked.Exchange(ref resetHuntGroup, 1);
    public void RestHuntStatistics(bool on) => Volatile.Write(ref restHuntTotals, on ? 1 : 0);

    public void RestoreHuntTotals(HuntTotals saved) => Volatile.Write(ref pendingHuntTotals, saved);

    private void ApplyHuntTotals(HuntTotals saved)
    {
        activeMs = saved.Seconds * 1000;
        damage = saved.Damage;
        gold = saved.Gold;
        spentGold = saved.GoldSpent;
        gainedExperience = saved.Experience;
        lostExperience = saved.ExperienceLost;
        deathsCount = saved.Deaths;
        killsCount = saved.Kills;
        levels = saved.Levels;
        usedPotions[0] = saved.HP;
        usedPotions[1] = saved.MP;
        usedPotions[2] = saved.RES;
        pickedGold = saved.DroppedGold;
        pickedItems = saved.Collected;
        foreach (var item in saved.Items)
        {
            collectedLoot[item.Code] = item;
        }

        Volatile.Write(ref huntTotals, saved with { Members = [] });
    }

    private void LogHunt(string line)
    {
        huntLog.Enqueue($"{DateTime.Now:HH:mm:ss} · {line}");
        while (huntLog.Count > 300)
        {
            huntLog.TryDequeue(out _);
        }

        Interlocked.Increment(ref huntLogVersion);
    }

    private void RefreshHuntStatistics()
    {
        var now = Environment.TickCount64;
        if (Interlocked.Exchange(ref pendingHuntTotals, null) is { } saved)
        {
            ApplyHuntTotals(saved);
        }
        if (Interlocked.Exchange(ref resetHuntGroup, 0) != 0)
        {
            memberTotals.Clear();
            ObservePartyHunt(lastPartyHunt, now);
        }

        if (Interlocked.Exchange(ref resetHuntTotals, 0) != 0)
        {
            damage = gold = spentGold = activeMs = 0;
            gainedExperience = lostExperience = 0;
            killsCount = deathsCount = levels = 0;
            Array.Clear(usedPotions);
            pickedGold = 0;
            pickedItems = 0;
            collectedLoot.Clear();
            samples.Clear();
            lastStatisticsOwner = null;
        }

        if (now - statisticsAt < 1000)
        {
            return;
        }

        if (trackingStatistics && statisticsAt != 0)
        {
            activeMs += now - statisticsAt;
        }

        trackingStatistics = !HuntStatisticsResting && (!HuntSettings.CountOnlyHunting || FreeHuntEnabled);
        statisticsAt = now;
        var seconds = activeMs / 1000;
        if (trackingStatistics)
        {
            samples.Enqueue(new HuntSample(seconds, gainedExperience, damage, killsCount));
        }
        while (samples.Count > 1 && samples.TryPeek(out var oldest) && seconds - oldest.Seconds > 600)
        {
            samples.Dequeue();
        }
        var first = samples.TryPeek(out var origin) ? origin : default;
        var shortSample = first;
        foreach (var sample in samples)
        {
            shortSample = sample;
            if (seconds - sample.Seconds <= 30)
            {
                break;
            }
        }

        var group = new HuntMember[memberTotals.Count];
        var groupIndex = 0;
        foreach (var member in memberTotals.Values)
        {
            group[groupIndex++] = member.Snapshot(now);
        }

        Volatile.Write(ref huntTotals, new HuntTotals(seconds, gainedExperience, lostExperience, gold, spentGold,
            damage, killsCount, deathsCount, levels, usedPotions[0], usedPotions[1], usedPotions[2], group)
        {
            DroppedGold = pickedGold, Collected = pickedItems, Items = collectedLoot.Values.ToArray(),
            RecentExperiencePerHour = samples.Count > 1 && seconds > first.Seconds ? (gainedExperience - first.Experience) * 3600d / (seconds - first.Seconds) : 0,
            RecentDamagePerSecond = samples.Count > 1 && seconds > shortSample.Seconds ? (damage - shortSample.Damage) / (double)(seconds - shortSample.Seconds) : 0,
            RecentKills = killsCount - first.Kills
        });
    }

    private void ObserveHuntPickup(GroundDropView drop)
    {
        if (HuntStatisticsResting || HuntSettings.CountOnlyHunting && !FreeHuntEnabled)
        {
            return;
        }

        if (drop.Gold)
        {
            pickedGold += drop.Amount;
            return;
        }

        pickedItems += drop.Amount;
        var prior = collectedLoot.GetValueOrDefault(drop.CatalogKey);
        collectedLoot[drop.CatalogKey] = new HuntLoot(drop.CatalogKey, drop.Name, prior.Count + drop.Amount);
    }

    private void ObserveHuntStatistics(PresenceUpdate update, long now)
    {
        foreach (var hit in update.Events)
        {
            if (hit.Kind == CombatEventKind.Hit && hit.TargetId == Entered.EntityId && hit.Killed)
            {
                if (huntDescriptions.TryGetValue(hit.AttackerId, out var monster))
                {
                    fatalHitBy = monster.Name;
                }
                else
                {
                    fatalHitBy = null;
                    foreach (var member in Party.Members)
                    {
                        if (member.EntityId == hit.AttackerId)
                        {
                            fatalHitBy = member.Name;
                            break;
                        }
                    }
                }

                fatalHitField = update.FieldId;
                fatalHitAt = now;
            }
        }

        var counting = !HuntStatisticsResting && (!HuntSettings.CountOnlyHunting || FreeHuntEnabled);
        if (update.Owner is { } state)
        {
            if (counting && lastStatisticsOwner is { } before)
            {
                if (state.Experience >= before.Experience)
                {
                    gainedExperience += state.Experience - before.Experience;
                }
                else
                {
                    lostExperience += before.Experience - state.Experience;
                }

                var delta = state.Money - before.Money;
                gold += Math.Max(0, delta);
                spentGold += Math.Max(0, -delta);
                levels += Math.Max(0, state.Level - before.Level);
                if (before.Life > 0 && state.Life <= 0)
                {
                    deathsCount++;
                }
            }

            if (lastStatisticsOwner is { Life: > 0 } && state.Life <= 0)
            {
                var killer = update.FieldId == fatalHitField && now - fatalHitAt <= 1500 ? fatalHitBy : null;
                LogHunt(string.IsNullOrWhiteSpace(killer) ? "Personagem morreu" : $"Personagem morreu · {killer}");
                fatalHitBy = null;
            }

            lastStatisticsOwner = state;
        }

        if (update.Party is { } updated)
        {
            foreach (var person in updated.Members)
            {
                if (!Party.Contains(person.EntityId))
                {
                    LogHunt($"{person.Name} entrou no grupo");
                }
            }

            foreach (var person in Party.Members)
            {
                if (!updated.Contains(person.EntityId))
                {
                    LogHunt($"{person.Name} saiu do grupo");
                    if (memberTotals.TryGetValue(person.EntityId, out var ledger))
                    {
                        ledger.Freeze(now);
                    }
                }
            }
        }

        if (update.PartyHunt is { } group)
        {
            if (!hasPartyHunt)
            {
                memberTotals.Clear();
                hasPartyHunt = true;
            }

            lastPartyHunt = group;
            ObservePartyHunt(group, now);
        }

        foreach (var hit in update.Events)
        {
            if (counting && hit.Kind == CombatEventKind.PotionApplied && hit.TargetId == Entered.EntityId)
            {
                var resources = (PotionResource)hit.Resources;
                if (resources.HasFlag(PotionResource.Life)) usedPotions[0]++;
                if (resources.HasFlag(PotionResource.Mana)) usedPotions[1]++;
                if (resources.HasFlag(PotionResource.Stamina)) usedPotions[2]++;
            }

            if (hit.Kind != CombatEventKind.Hit || !huntDescriptions.ContainsKey(hit.TargetId))
            {
                continue;
            }

            if (counting && (hit.AttackerId == Entered.EntityId || ownSummons.Contains(hit.AttackerId)))
            {
                damage += Math.Max(0, hit.Damage);
                if (hit.Killed) killsCount++;
            }

            if (hasPartyHunt)
            {
                continue;
            }

            foreach (var member in Party.Members)
            {
                if (member.EntityId == hit.AttackerId)
                {
                    GetMemberLedger(member).Add((ulong)Math.Max(0, hit.Damage), 0, hit.Killed ? 1u : 0u, 0);
                    break;
                }
            }
        }
    }

    private void ObservePartyHunt(IReadOnlyList<PartyHuntInfo> group, long now)
    {
        foreach (var member in Party.Members)
        {
            foreach (var entry in group)
            {
                if (entry.EntityId == member.EntityId)
                {
                    GetMemberLedger(member).Observe(entry.Counters, now);
                    break;
                }
            }
        }
    }

    private HuntMemberLedger GetMemberLedger(PartyMemberInfo member)
    {
        if (!memberTotals.TryGetValue(member.EntityId, out var ledger))
        {
            ledger = new HuntMemberLedger(member.EntityId, member.Name);
            memberTotals.Add(member.EntityId, ledger);
        }

        return ledger;
    }

    private sealed class HuntMemberLedger(ulong id, string name)
    {
        private const long RecentWindowMs = 30_000;
        private readonly Queue<(long At, long Damage)> samples = new();
        private PartyHuntCounters? last;
        private HuntMember totals = new(id, name, 0, 0);

        public void Observe(PartyHuntCounters next, long now)
        {
            if (last is { } previous)
            {
                Add(next.Damage - Math.Min(next.Damage, previous.Damage),
                    next.Experience - Math.Min(next.Experience, previous.Experience),
                    next.Kills - Math.Min(next.Kills, previous.Kills), next.Deaths - Math.Min(next.Deaths, previous.Deaths));
            }
            else
            {
                totals = totals with { Active = true, RecentDamagePerSecond = 0 };
                samples.Enqueue((now, totals.Damage));
            }

            last = next;
        }

        public void Add(ulong damage, ulong experience, uint kills, uint deaths)
        {
            totals = totals with
            {
                Damage = totals.Damage + (long)Math.Min(damage, (ulong)(long.MaxValue - totals.Damage)),
                Experience = totals.Experience + Math.Min(experience, ulong.MaxValue - totals.Experience),
                Kills = totals.Kills + (int)Math.Min(kills, (uint)(int.MaxValue - totals.Kills)),
                Deaths = totals.Deaths + (int)Math.Min(deaths, (uint)(int.MaxValue - totals.Deaths)),
                Active = true
            };
        }

        public HuntMember Snapshot(long now)
        {
            if (!totals.Active)
            {
                return totals;
            }

            while (samples.TryPeek(out var start) && now - start.At > RecentWindowMs)
            {
                samples.Dequeue();
            }

            var dps = samples.TryPeek(out var first) && now > first.At ? (totals.Damage - first.Damage) * 1000d / (now - first.At) : 0;
            samples.Enqueue((now, totals.Damage));
            return totals with { RecentDamagePerSecond = dps };
        }

        public void Freeze(long now)
        {
            totals = Snapshot(now) with { Active = false };
            samples.Clear();
            last = null;
        }
    }
}
