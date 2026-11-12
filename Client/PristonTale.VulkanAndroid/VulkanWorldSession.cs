using System.Collections.Concurrent;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Sessions;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Characters;

internal sealed class VulkanWorldSession(GameServerConnection connection, CharacterSummary character, EnterWorldOutcome entered) : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> chat = new();
    private OwnerState owner;
    private GameScene scene;
    private int runToggle;
    public void ToggleRun() => Interlocked.Increment(ref runToggle);
    public bool TakeRunToggle() => (Interlocked.Exchange(ref runToggle, 0) & 1) != 0;

    private Task closing = Task.CompletedTask;

    public CharacterSummary Character { get; } = character;
    public EnterWorldOutcome Entered { get; } = entered;
    public OwnerState Owner => Volatile.Read(ref owner);

    public void Bind(GameScene world) => Volatile.Write(ref scene, world);
    public void PublishOwner(OwnerState value) => Volatile.Write(ref owner, value);
    public void SetClosing(Task task) => closing = task;
    public bool TryReadChat(out string line) => chat.TryDequeue(out line);

    public Task SendChatAsync(string text) => Volatile.Read(ref scene)?.SendChatAsync(text, false) ?? Task.CompletedTask;

    public void Observe(PresenceUpdate update)
    {
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
