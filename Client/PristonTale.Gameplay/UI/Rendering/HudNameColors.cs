using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI.Rendering;

// The colours of names over the scene and in speech bubbles, by who carries them, and how far from the camera an
// overhead name or bubble is still shown.
public static class HudNameColors
{
    public static readonly Color Player = new(255, 255, 200);
    public static readonly Color Party = new(220, 255, 160);
    public static readonly Color Monster = new(255, 210, 210);
    public static readonly Color HoveredMonster = new(255, 230, 200);
    public static readonly Color Npc = new(180, 180, 255);

    // A player with the PvP mode on, and a murderer (PvpRules).
    public static readonly Color Pvp = new(255, 150, 110);
    public static readonly Color Murderer = new(255, 60, 50);

    // The "[Clan]" line over a player's name.
    public static readonly Color Clan = new(150, 255, 200);

    // A title's line over a player's name, by its rarity (TitleRarity: common, rare, epic, legendary).
    private static readonly Color[] TitleColors = [new(235, 235, 235), new(110, 180, 255), new(200, 130, 255), new(255, 200, 70)];

    public static Color Title(Simulation.Titles.TitleRarity rarity) => TitleColors[(int)rarity];

    public const float OverheadDepth = 12 * 64;
}
