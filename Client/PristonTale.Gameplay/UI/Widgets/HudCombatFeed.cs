using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Widgets;

public enum CombatFeedKind : byte
{
    Kill,
    Experience,
    Gold,
    Level,
    Quest
}

// A line of the feed: a kill (the monster's name), experience or gold gained (amount), a level reached (amount) or a quest
// count grown (count of needed, the quest's monster as name). Sequence counts the lines added.
public readonly record struct CombatFeedLine(int Sequence, CombatFeedKind Kind, long Amount, int Count, int Needed, string Name);

// The touch HUD's combat feed, the Android client's (touch-feed.js draws it): each kill of a monster the player struck,
// experience and gold gained, a level reached and a quest count grown, newest last, and the kills since entering the
// world. Kept as data in a ring of the newest lines; the page words and animates them.
public sealed class HudCombatFeed
{
    public const int Capacity = 8;
    private readonly CombatFeedLine[] _lines = new CombatFeedLine[Capacity];
    private OwnerState _owner;
    private int _questKills = -1;

    public int Added { get; private set; }
    public int Kills { get; private set; }
    public int Count => Math.Min(Added, Capacity);

    // The newest line at index 0.
    public CombatFeedLine Newest(int index) => _lines[(Added - 1 - index) % Capacity];

    // Within a level, the experience and gold gained; a level gained says so instead.
    public void Observe(OwnerState owner)
    {
        if (owner is null || ReferenceEquals(owner, _owner))
        {
            return;
        }

        if (_owner is { } before && owner.Level == before.Level)
        {
            if (owner.Experience > before.Experience)
            {
                Add(CombatFeedKind.Experience, (long)(owner.Experience - before.Experience));
            }

            if (owner.Money > before.Money)
            {
                Add(CombatFeedKind.Gold, owner.Money - before.Money);
            }
        }
        else if (_owner is not null && owner.Level > _owner.Level)
        {
            Add(CombatFeedKind.Level, owner.Level);
        }

        _owner = owner;
    }

    public void Observe(QuestView quest)
    {
        if (quest is not { KillsNeeded: > 0 })
        {
            _questKills = -1;
            return;
        }

        if (_questKills >= 0 && quest.Kills > _questKills)
        {
            Add(CombatFeedKind.Quest, 0, Math.Min(quest.Kills, quest.KillsNeeded), quest.KillsNeeded, quest.TargetName);
        }

        _questKills = quest.Kills;
    }

    public void Kill(string name)
    {
        Kills++;
        Add(CombatFeedKind.Kill, 0, name: name);
    }

    private void Add(CombatFeedKind kind, long amount, int count = 0, int needed = 0, string name = "")
    {
        _lines[Added % Capacity] = new CombatFeedLine(Added + 1, kind, amount, count, needed, name ?? "");
        Added++;
    }
}
