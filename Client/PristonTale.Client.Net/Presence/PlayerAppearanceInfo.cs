using PristonTale.Contracts.Characters;

namespace PristonTale.Client.Net.Presence;

public sealed record PlayerAppearanceInfo(
    string Name,
    PlayerClass Class,
    int Tier,
    int FaceIndex,
    int HairIndex,
    string ModelName,
    string ModelName2,
    int Level,
    string WeaponKey = "", string ArmorKey = "", string OffHandKey = "", bool InVillage = false)
{
    // Premium looks: the helper pet (1 Terry, 2 Nepsys, 3 Io, 4 Mute), the Phoenix and the big head (1 or 2).
    public int HelperPet { get; init; }
    public bool Phoenix { get; init; }
    public int BigHead { get; init; }

    // Riding the travel mount (MountRules).
    public bool Mounted { get; init; }

    // The PvP mode is on, the player is a murderer (PvpRules), and it fights a duel (DuelRules).
    public bool Pvp { get; init; }
    public bool Murderer { get; init; }
    public bool Duelling { get; init; }

    // The side (1 or 2) of the arena match the player still fights in, 0 for none (ArenaRules).
    public int ArenaSide { get; init; }

    // The title shown over the name (TitleRules), 0 for none.
    public int Title { get; init; }

    // How the held weapon and off-hand item glow.
    public Simulation.Items.HeldItemGlow WeaponGlow { get; init; } = Simulation.Items.HeldItemGlow.None;
    public Simulation.Items.HeldItemGlow OffHandGlow { get; init; } = Simulation.Items.HeldItemGlow.None;

    // The sign of the personal shop the player sells from; empty when it sells nothing.
    public string ShopSign { get; init; } = "";

    // The player's clan (0 for none), its name and its mark's version (0 the default mark).
    public long ClanId { get; init; }
    public string ClanName { get; init; } = "";
    public int ClanMark { get; init; }
}
