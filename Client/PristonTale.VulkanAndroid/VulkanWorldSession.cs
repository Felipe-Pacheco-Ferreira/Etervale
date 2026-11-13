using System.Collections.Concurrent;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Sessions;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.Localization;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.Social;

internal sealed class VulkanWorldSession(GameServerConnection connection, CharacterSummary character, EnterWorldOutcome entered) : IAsyncDisposable
{
    private readonly ConcurrentQueue<ChatEntry> chat = new();
    // The lines the player sent, for its own speech bubble (the server does not echo them).
    private readonly ConcurrentQueue<string> spoken = new();
    private OwnerState owner;
    private QuestView quest;
    private IPlayChannel play;
    private int autoHunt;
    private ulong huntTarget;
    private int attackTap;
    private WorldMapPose mapPose;
    private ulong attackTarget;
    private NpcPortraitFrame npcPortrait;
    public NpcPortraitFrame NpcPortrait => Volatile.Read(ref npcPortrait);
    public void SetNpcPortrait(NpcPortraitFrame frame) => Volatile.Write(ref npcPortrait, frame);
    public ulong AttackTarget => Volatile.Read(ref attackTarget);
    public WorldMapPose MapPose => Volatile.Read(ref mapPose);
    public void AttackSelected() => Interlocked.Exchange(ref attackTap, 1);
    private readonly Dictionary<ulong, int> huntMonsters = new();
    private readonly Dictionary<ulong, (string Name, System.Numerics.Vector3 Position)> npcPlaces = new();
    private int keepHunting;

    // Whether the hunt goes on after its kills are counted, while the quest still wants a drop.
    public bool KeepHunting
    {
        get => Volatile.Read(ref keepHunting) != 0;
        set => Volatile.Write(ref keepHunting, value ? 1 : 0);
    }

    // The extracted desktop assets the world reads; set once the world is loaded.
    public string AssetRoot { get; set; }

    // The automatic quest; set once the world is loaded.
    public QuestNavigator Navigator { get; set; }

    // The graphics quality the menu changes; set once the world is loaded.
    public MobileGraphics Graphics { get; set; }

    // An NPC in view by name, with its place in field coordinates; called on the game thread.
    // Where the NPCs in view stand, on the render plane, leaving out those within keepFree of goal (the one walked to).
    public List<Microsoft.Xna.Framework.Vector2> NpcPoints(Microsoft.Xna.Framework.Vector2 goal, float keepFree)
    {
        var points = new List<Microsoft.Xna.Framework.Vector2>(npcPlaces.Count);
        foreach (var (_, place) in npcPlaces)
        {
            var point = new Microsoft.Xna.Framework.Vector2(place.Position.X, -place.Position.Z);
            if (Microsoft.Xna.Framework.Vector2.Distance(point, goal) > keepFree)
            {
                points.Add(point);
            }
        }

        return points;
    }

    public bool TryFindNpc(string name, out ulong id, out System.Numerics.Vector3 position)
    {
        foreach (var (entity, place) in npcPlaces)
        {
            if (place.Name == name)
            {
                (id, position) = (entity, place.Position);
                return true;
            }
        }

        (id, position) = (0, default);
        return false;
    }

    // The nearest living monster within range, or null; called on the game thread.
    public ulong? ClosestMonster(float range)
    {
        ulong? closest = null;
        var best = range * range;
        foreach (var (id, _) in huntMonsters)
        {
            if (scene.TrackQuestMonster(id, out var distance) && distance < best)
            {
                (closest, best) = (id, distance);
            }
        }

        return closest;
    }

    // Whether a living monster of the quest's code is in view; called on the game thread.
    public bool HasHuntTarget(int questCode)
    {
        foreach (var (id, code) in huntMonsters)
        {
            if (code == questCode && scene.TrackQuestMonster(id, out _))
            {
                return true;
            }
        }

        return false;
    }
    private readonly ConcurrentQueue<ulong> npcs = new();
    private readonly ConcurrentQueue<QuestPrompt> prompts = new();
    // Monsters the player struck, until they die or leave view; a death among them is a kill for the HUD.
    private readonly HashSet<ulong> struck = new();
    private readonly ConcurrentQueue<ulong> kills = new();
    private TargetView target;
    private IReadOnlyList<MapMark> npcMarks = [];
    public QuestView Quest => Volatile.Read(ref quest);
    public IPlayChannel Play => Volatile.Read(ref play);
    public bool AutoHunt => Volatile.Read(ref autoHunt) != 0;
    public void SetAutoHunt(bool value) => Volatile.Write(ref autoHunt, value ? 1 : 0);
    public void SelectNpc(ulong id) => npcs.Enqueue(id);
    public bool TryReadNpc(out ulong id) => npcs.TryDequeue(out id);
    public bool TryReadPrompt(out QuestPrompt prompt) => prompts.TryDequeue(out prompt);

    // The events and rewards window as the server last sent it, and how each request of it ended.
    private readonly ConcurrentQueue<RewardEventsResultInfo> rewardResults = new();
    private RewardEventsInfo rewards;
    public RewardEventsInfo Rewards => Volatile.Read(ref rewards);
    public bool TryReadRewardResult(out RewardEventsResultInfo result) => rewardResults.TryDequeue(out result);
    private GameScene scene;
    private int runToggle;
    public void ToggleRun() => Interlocked.Increment(ref runToggle);
    public bool TakeRunToggle() => (Interlocked.Exchange(ref runToggle, 0) & 1) != 0;

    private Task closing = Task.CompletedTask;

    public CharacterSummary Character { get; } = character;
    public EnterWorldOutcome Entered { get; } = entered;
    public OwnerState Owner => Volatile.Read(ref owner);

    public void Bind(GameScene world, IPlayChannel channel)
    {
        Volatile.Write(ref scene, world);
        Volatile.Write(ref play, channel);
    }

    public void UpdateAutoHunt(bool manualMovement)
    {
        Volatile.Write(ref attackTarget, AutoHunt ? scene.AttackTargetId : scene.SelectedCombatTarget);
        var position = scene.PlayerPosition;
        var forward = scene.PlayerForward;
        var pose = new WorldMapPose(new System.Numerics.Vector3(position.X, position.Y, -position.Z),
            MathF.Atan2(forward.X, -forward.Z), scene.ActiveFieldDefinition?.FieldCode ?? Entered.FieldId);
        if (pose != MapPose)
        {
            Volatile.Write(ref mapPose, pose);
        }
        if (manualMovement || Owner is not { Life: > 0 } || Quest is not { TargetQuestCode: > 0 } active || active.Kills >= active.KillsNeeded && !KeepHunting)
        {
            SetAutoHunt(false);
        }

        var manualAttack = Interlocked.Exchange(ref attackTap, 0) != 0 && Owner is { Life: > 0 };
        var next = 0UL;
        var code = AutoHunt ? Quest?.TargetQuestCode : null;
        if (manualAttack || code.HasValue)
        {
            var distance = float.PositiveInfinity;
            foreach (var (id, monsterCode) in huntMonsters)
            {
                if ((manualAttack || monsterCode == code) && scene.TrackQuestMonster(id, out var range) && range < distance)
                {
                    next = id;
                    distance = range;
                }
            }
        }

        if (manualAttack)
        {
            SetAutoHunt(false);
            var selected = scene.SelectedCombatTarget;
            var target = scene.TrackQuestMonster(selected, out _) ? selected : next;
            if (target != 0)
            {
                scene.SetQuestHuntTarget(target, automatic: false);
            }
            else if (huntTarget != 0 && scene.AttackTargetId == huntTarget)
            {
                scene.SetQuestHuntTarget(0);
            }
            huntTarget = 0;
            return;
        }
        if (next != huntTarget)
        {
            if (next != 0 || scene.AttackTargetId == huntTarget)
            {
                scene.SetQuestHuntTarget(next);
            }
            huntTarget = next;
        }
    }
    public void PublishOwner(OwnerState value) => Volatile.Write(ref owner, value);
    public void SetClosing(Task task) => closing = task;
    public bool TryReadChat(out ChatEntry line) => chat.TryDequeue(out line);

    public bool TryReadSpoken(out string line) => spoken.TryDequeue(out line);
    public bool TryReadKill(out ulong id) => kills.TryDequeue(out id);

    // The target the HUD frames, or null; set on the game thread.
    public TargetView Target => Volatile.Read(ref target);

    // The NPCs in view with their names and places in field coordinates, for the full-screen map; read on the UI thread.
    public IReadOnlyList<MapMark> NpcMarks => Volatile.Read(ref npcMarks);

    // Publishes the entity the player fights or picked, allocating only when what the frame shows changed.
    public void PublishTarget(HudCombat combat, ulong id)
    {
        var shown = Target;
        if (!combat.TryGetTargetState(id, out var name, out var level, out var life, out var maxLife))
        {
            if (shown is not null)
            {
                Volatile.Write(ref target, null);
            }

            return;
        }

        if (shown is null || shown.Id != id || shown.Life != life || shown.MaxLife != maxLife || shown.Level != level || shown.Name != name)
        {
            Volatile.Write(ref target, new TargetView(id, name, level, life, maxLife));
        }
    }

    // Sends a local line; once sent, the line shows in the chat and over the player's head, as the server does not echo it.
    public async Task SendChatAsync(string text)
    {
        if (Volatile.Read(ref scene) is not { } current)
        {
            return;
        }

        await current.SendChatAsync(text, false);
        if (!ChatRules.IsCommand(ChatRules.WithoutRoute(text)))
        {
            var shown = ChatRules.Compose(Character.Name, text);
            chat.Enqueue(new ChatEntry(shown, ChatKind.Player));
            spoken.Enqueue(shown);
        }
    }

    public void Observe(PresenceUpdate update)
    {
        if (update.Quest is { } view)
        {
            var previous = Quest;
            if (previous?.Code != view.Code || previous.State != view.State)
            {
                SetAutoHunt(false);
            }
            Volatile.Write(ref quest, view);
        }
        if (update.Rewards is { } rewardWindow)
        {
            Volatile.Write(ref rewards, rewardWindow);
        }
        if (update.RewardResult is { } rewardResult)
        {
            rewardResults.Enqueue(rewardResult);
        }
        if (update.QuestPrompt is { } prompt)
        {
            prompts.Enqueue(prompt);
        }
        foreach (var spawn in update.Spawns)
        {
            if (spawn.Kind == EntityKind.Monster && spawn.OwnerId == 0)
            {
                huntMonsters[spawn.EntityId] = spawn.QuestCode;
            }
            else if (spawn.Kind == EntityKind.Npc && !string.IsNullOrEmpty(spawn.Name))
            {
                npcPlaces[spawn.EntityId] = (spawn.Name, spawn.Position);
            }
        }
        foreach (var combat in update.Events)
        {
            if (combat.Kind == CombatEventKind.Hit && combat.AttackerId == Entered.EntityId && huntMonsters.ContainsKey(combat.TargetId))
            {
                struck.Add(combat.TargetId);
            }
        }
        for (var index = 0; index < update.States.Count; index++)
        {
            var state = update.States[index];
            if (state.Life <= 0 && struck.Remove(state.EntityId))
            {
                kills.Enqueue(state.EntityId);
            }
        }
        foreach (var removed in update.Removed)
        {
            struck.Remove(removed);
            huntMonsters.Remove(removed);
            npcPlaces.Remove(removed);
        }
        if (update.Spawns.Count > 0 || update.Removed.Count > 0)
        {
            var marks = new List<MapMark>(npcPlaces.Count);
            foreach (var (_, place) in npcPlaces)
            {
                marks.Add(new MapMark(place.Name, place.Position.X, place.Position.Z));
            }

            Volatile.Write(ref npcMarks, marks);
        }

        foreach (var line in update.Chat)
        {
            var text = line.Line.IsNone ? line.Text : ClientText.Format(line.Line);
            if (!string.IsNullOrEmpty(text))
            {
                chat.Enqueue(new ChatEntry(text, line.Kind));
            }
        }
        while (chat.Count > 32)
        {
            chat.TryDequeue(out _);
        }
    }

    internal readonly record struct ChatEntry(string Text, ChatKind Kind);

    internal sealed record WorldMapPose(System.Numerics.Vector3 Position, float Facing, int FieldId);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await closing;
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}

// A named place on the map, in field coordinates.
internal sealed record MapMark(string Name, float X, float Z);
internal sealed record NpcPortraitFrame(ulong EntityId, float X, float Y, float Width, float Height);
