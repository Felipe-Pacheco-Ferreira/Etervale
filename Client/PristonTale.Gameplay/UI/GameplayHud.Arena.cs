using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Simulation.Combat;

namespace PristonTale.Gameplay.UI;

// The arena (ArenaRules): a line at the top of the screen while the local player queues ("in the 1x1 arena queue") or
// plays a match (its side's and the other side's players standing and the time left; grey once a blow took it out).
// Joining and leaving the queue are chat lines ("/arena 1x1", the quick menu's arena entries).
public sealed partial class GameplayHud
{
    private static readonly Color ArenaColor = new(255, 220, 140);
    private static readonly Color ArenaOutColor = new(190, 190, 190);
    private ArenaInfo _arenaShown;
    private string _arenaLine;

    // The local player's arena queue or match, null for neither.
    public ArenaInfo Arena { get; private set; }

    public void ObserveArena(PresenceUpdate update)
    {
        if (update.Arena is { } arena)
        {
            Arena = arena.Active ? arena : null;
        }
    }

    private void DrawArena(HudLayout layout)
    {
        if (Arena is not { } arena)
        {
            return;
        }

        // Formatted once per change, not every frame.
        if (!ReferenceEquals(arena, _arenaShown))
        {
            _arenaShown = arena;
            var mode = ArenaRules.ModeName(arena.Mode);
            _arenaLine = arena.Queued
                ? ClientText.Format("Arena.Queued", mode)
                : ClientText.Format(arena.Out ? "Arena.Out" : "Arena.Status", mode, arena.OwnStanding, arena.FoeStanding, arena.SecondsLeft / 60,
                    (arena.SecondsLeft % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        }

        _painter.TextCentered(_arenaLine, 0, layout.Width, DuelLineY, arena.Out ? ArenaOutColor : ArenaColor);
    }
}
