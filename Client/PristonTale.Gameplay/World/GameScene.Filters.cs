using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.World;

// "Hide other players": players outside the party are not drawn and cannot be clicked; "prioritize players": a player
// under the cursor wins over a nearer monster.
public sealed partial class GameScene
{
    private readonly List<ulong> _remotePlayers = new();
    private readonly HashSet<ulong> _filteredPlayers = new();

    // Whether a player is in the local player's party (the HUD knows the party).
    public Func<ulong, bool> IsPartyMember { get; set; } = static _ => false;

    private bool PreferPlayers => _presentation?.Requested.PrioritizePlayers == true;

    private void UpdatePlayerFilter()
    {
        if (_remote is null || _renderer is null)
        {
            return;
        }

        var hide = _presentation?.Requested.HideOtherPlayers == true;
        if (!hide && _filteredPlayers.Count == 0)
        {
            return;
        }

        _remote.CollectPlayers(_remotePlayers);
        _filteredPlayers.Clear();
        Span<ActorHandle> handles = stackalloc ActorHandle[PlayerActorHandles.Most];
        foreach (var id in _remotePlayers)
        {
            var filtered = hide && id != _remote.SelfEntityId && !IsPartyMember(id);
            var count = _remote.CopyActorHandles(id, handles);
            for (var index = 0; index < count; index++)
            {
                _renderer.SetActorFiltered(handles[index], filtered);
            }

            if (filtered)
            {
                _filteredPlayers.Add(id);
            }
        }
    }
}
