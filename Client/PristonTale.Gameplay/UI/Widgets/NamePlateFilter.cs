using Microsoft.Xna.Framework;
using PristonTale.Contracts.World;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// Which name boxes the settings let through (HudCombat.DrawNamePlates). A kind switched off still shows its name under
// the cursor or when clicked; hidden players (HideStrangers) show none, party members excepted. AllPlayers is the
// persistent character information (Backspace): every player's name, not only the hovered one's.
public readonly record struct NamePlateFilter(
    bool HideUnfocused,
    bool HidePlayers,
    bool HideNpcs,
    bool HideMonsters,
    bool HideStrangers,
    bool AllPlayers,
    bool ShowOwn,
    NameColor OwnColor)
{
    public static NamePlateFilter From(ClientSettings settings) => new(settings.HideUnfocusedNames, settings.HidePlayerNames, settings.HideNpcNames,
        settings.HideMonsterNames, settings.HideOtherPlayers, settings.PersistentCharacterInfo, settings.ShowOwnName, settings.OwnNameColor);

    // Whether an entity that the original rules would label keeps its box: kind, whether it is under the cursor or
    // clicked, a party member, or the local player.
    public bool Allows(EntityKind kind, bool focus, bool member, bool self)
    {
        if (self)
        {
            return ShowOwn;
        }

        if (kind == EntityKind.Player && HideStrangers && !member)
        {
            return false;
        }

        if (focus || member)
        {
            return true;
        }

        return !HideUnfocused && kind switch
        {
            EntityKind.Player => !HidePlayers,
            EntityKind.Npc => !HideNpcs,
            EntityKind.Monster => !HideMonsters,
            _ => true
        };
    }

    public static Color ColorOf(NameColor color) => color switch
    {
        NameColor.Gold => new Color(255, 210, 90),
        NameColor.Green => new Color(140, 255, 140),
        NameColor.Blue => new Color(130, 190, 255),
        NameColor.Pink => new Color(255, 150, 210),
        NameColor.Orange => new Color(255, 165, 70),
        _ => Color.White
    };
}
