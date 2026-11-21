using PristonTale.Client.Net.Presence;

namespace PristonTale.Gameplay.World;

// The local player's PvP mode (PvpRules). The state lives on the server: TogglePvp asks for the other state, the server
// answers in a system line, and the player's own description carries the mode and its arena side that Fights reads (its
// name box shows the mode and the murderer's mark, HudCombat).
public sealed partial class GameScene
{
    public bool PvpOn { get; private set; }

    // The local player's side (1 or 2) in an arena match it still fights in, 0 for none (ArenaRules).
    public int ArenaSide { get; private set; }

    // Shows a title the player owns over its name, or none (0); the server answers with the titles (TitleRules).
    public void ChooseTitle(int title)
    {
        if (_playChannel is not null)
        {
            SendUpstream(_playChannel.ChooseTitleAsync(title, _presenceCts.Token), "title");
        }
    }

    public void TogglePvp()
    {
        if (_playChannel is not null)
        {
            SendUpstream(_playChannel.SendPvpSwitchAsync(!PvpOn, _presenceCts.Token), "pvp");
        }
    }

    // Fighting in the Battle Royale and not out (RoyaleRules): every player the local one sees there is a foe, as the
    // server shows it no one else.
    public bool RoyaleFighting { get; private set; }

    private void ObserveRoyale(PresenceUpdate update)
    {
        if (update.Royale is { } royale)
        {
            RoyaleFighting = royale.Fighting;
        }
    }

    private void ObserveOwnPvp(EntitySpawnInfo spawn)
    {
        if (spawn.Player is { } appearance)
        {
            PvpOn = appearance.Pvp;
            ArenaSide = appearance.ArenaSide;
        }
    }
}
