using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Localization;
using PristonTale.Simulation.Combat;

namespace PristonTale.Gameplay.UI;

// The Battle Royale (RoyaleRules): a line at the top of the screen while the local player is registered (how many are)
// or in the event (players standing of all, when the zone shrinks and the time left; grey once out and watching), and
// the safe zone as a dotted circle on the minimap. Registering and leaving are chat lines ("/royale", the quick menu).
public sealed partial class GameplayHud
{
    private static readonly Color RoyaleColor = new(140, 220, 255);
    private RoyaleInfo _royaleShown;
    private string _royaleLine;

    // The local player's Battle Royale registration or event, null for neither.
    public RoyaleInfo Royale { get; private set; }

    public void ObserveRoyale(PresenceUpdate update)
    {
        if (update.Royale is { } royale)
        {
            Royale = royale.Active ? royale : null;
        }
    }

    private (Vector3 Center, float Radius)? MinimapZone() =>
        Royale is { Registered: false } royale ? (new Vector3(RoyaleRules.Center.X, 0f, RoyaleRules.Center.Z), royale.Radius) : null;

    private void DrawRoyale(HudLayout layout)
    {
        if (Royale is not { } royale)
        {
            return;
        }

        // Formatted once per change, not every frame.
        if (!ReferenceEquals(royale, _royaleShown))
        {
            _royaleShown = royale;
            _royaleLine = royale.Registered
                ? ClientText.Format("Royale.Registered", royale.Total)
                : ClientText.Format(royale.Out ? "Royale.Out" : royale.SecondsToShrink > 0 ? "Royale.Status" : "Royale.Closing", royale.Standing, royale.Total,
                    Clock(royale.SecondsToShrink), Clock(royale.SecondsLeft));
        }

        _painter.TextCentered(_royaleLine, 0, layout.Width, DuelLineY, royale.Out ? ArenaOutColor : RoyaleColor);
    }

    private static string Clock(int seconds) =>
        (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
}
