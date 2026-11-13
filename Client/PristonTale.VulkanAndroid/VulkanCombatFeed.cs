using Android.Content;
using Android.Graphics;
using Android.Views;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

// The combat feed on the right: each kill, experience, gold and quest count gained rises and fades over a few seconds,
// under a counter of the kills of this session that flares at each one. Fed from the owner, the target and the quest
// as the HUD refreshes; draws nothing while there is nothing to show.
internal sealed class VulkanCombatFeed : View
{
    private const int MostLines = 5;
    private const float LineSeconds = 3.2f;
    private const float FlareSeconds = .6f;
    private static readonly Color KillColor = Color.Rgb(255, 196, 120);
    private static readonly Color ExperienceColor = Color.Rgb(250, 224, 120);
    private static readonly Color GoldColor = Color.Rgb(255, 214, 80);
    private static readonly Color QuestColor = Color.Rgb(150, 230, 190);

    private readonly float _height;
    private readonly List<(string Text, Color Color, long Born)> _lines = new(MostLines);
    private readonly RectF _box = new();
    private OwnerState _owner;
    private int _questKills = -1;
    private int _kills;
    private string _killText = "";
    private long _flare;

    public VulkanCombatFeed(Context context, float screenHeight) : base(context)
    {
        _height = screenHeight;
    }

    public void Observe(OwnerState owner)
    {
        if (_owner is { } before && owner.Level == before.Level)
        {
            if (owner.Experience > before.Experience)
            {
                Add($"+{owner.Experience - before.Experience:N0} EXP", ExperienceColor);
            }

            if (owner.Money > before.Money)
            {
                Add($"+{owner.Money - before.Money:N0} ouro", GoldColor);
            }
        }
        else if (_owner is not null && owner.Level > _owner.Level)
        {
            Add($"Nível {owner.Level}!", VulkanHudArt.EdgeLight);
        }

        _owner = owner;
    }

    // A monster the player struck died; its name is known when it was the target shown last.
    public void Kill(ulong id, TargetView lastTarget)
    {
        _kills++;
        _killText = $"⚔ {_kills} {(_kills == 1 ? "abate" : "abates")}";
        _flare = Android.OS.SystemClock.ElapsedRealtimeNanos();
        Add(lastTarget is not null && lastTarget.Id == id ? $"{lastTarget.Name} derrotado" : "Monstro derrotado", KillColor);
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
            Add($"{quest.TargetName}  {Math.Min(quest.Kills, quest.KillsNeeded)}/{quest.KillsNeeded}", QuestColor);
        }

        _questKills = quest.Kills;
    }

    private void Add(string text, Color color)
    {
        if (_lines.Count == MostLines)
        {
            _lines.RemoveAt(0);
        }

        _lines.Add((text, color, Android.OS.SystemClock.ElapsedRealtimeNanos()));
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        var now = Android.OS.SystemClock.ElapsedRealtimeNanos();
        var h = _height;
        var size = .034f * h;
        var lineHeight = size * 1.35f;
        var right = Width - .01f * h;
        if (_kills > 0)
        {
            var flare = Math.Clamp(1f - (now - _flare) / 1e9f / FlareSeconds, 0f, 1f);
            var chip = size * (1f + flare * .35f);
            _box.Set(right - chip * 5.6f, 0, right, chip * 1.5f);
            VulkanHudArt.Plate(canvas, _box, chip * .75f);
            VulkanHudArt.Label(canvas, _killText, _box.CenterX(), _box.CenterY(), chip * .78f,
                flare > 0f ? VulkanHudArt.EdgeLight : KillColor, Paint.Align.Center!);
        }

        _lines.RemoveAll(line => (now - line.Born) / 1e9f > LineSeconds);
        var bottom = Height - lineHeight * .5f;
        for (var index = _lines.Count - 1; index >= 0; index--)
        {
            var (text, color, born) = _lines[index];
            var age = (now - born) / 1e9f / LineSeconds;
            var alpha = (int)(255 * Math.Clamp((1f - age) * 3f, 0f, 1f));
            var y = bottom - (_lines.Count - 1 - index) * lineHeight - age * lineHeight * .8f;
            VulkanHudArt.Label(canvas, text, right, y, size, Color.Argb(alpha, color.R, color.G, color.B), Paint.Align.Right!);
        }

        if (_lines.Count > 0 || now - _flare < FlareSeconds * 1e9f)
        {
            PostInvalidateOnAnimation();
        }
    }
}
