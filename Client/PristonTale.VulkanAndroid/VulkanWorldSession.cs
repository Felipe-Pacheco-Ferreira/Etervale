using System.Collections.Concurrent;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Sessions;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Contracts.World;

internal sealed class VulkanWorldSession(GameServerConnection connection, CharacterSummary character, EnterWorldOutcome entered) : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> chat = new();
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

    // An NPC in view by name, with its place in field coordinates; called on the game thread.
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
    public ulong? ClosestMonster(float range, int questCode)
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
    public QuestView Quest => Volatile.Read(ref quest);
    public IPlayChannel Play => Volatile.Read(ref play);
    public bool AutoHunt => Volatile.Read(ref autoHunt) != 0;
    public void SetAutoHunt(bool value) => Volatile.Write(ref autoHunt, value ? 1 : 0);
    public void SelectNpc(ulong id) => npcs.Enqueue(id);
    public bool TryReadNpc(out ulong id) => npcs.TryDequeue(out id);
    public bool TryReadPrompt(out QuestPrompt prompt) => prompts.TryDequeue(out prompt);
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
    public bool TryReadChat(out string line) => chat.TryDequeue(out line);

    public Task SendChatAsync(string text) => Volatile.Read(ref scene)?.SendChatAsync(text, false) ?? Task.CompletedTask;

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
        foreach (var removed in update.Removed)
        {
            huntMonsters.Remove(removed);
            npcPlaces.Remove(removed);
        }

        foreach (var line in update.Chat)
        {
            if (!string.IsNullOrEmpty(line.Text))
            {
                chat.Enqueue(line.Text);
            }
        }
        while (chat.Count > 32)
        {
            chat.TryDequeue(out _);
        }
    }

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

internal sealed record NpcPortraitFrame(ulong EntityId, float X, float Y, float Width, float Height);
