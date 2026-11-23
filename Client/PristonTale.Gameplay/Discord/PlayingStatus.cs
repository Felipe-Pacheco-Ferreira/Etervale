using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.Discord;

// What the player is doing, as other people see it on Discord: in a menu (InWorld false) or in the world with a named
// character of a class, tier and level in a field. Compared every frame, so it holds no allocated data of its own.
public readonly record struct PlayingStatus(bool InWorld, string CharacterName, PlayerClass Class, int Tier, int Level, string FieldName)
{
    public static PlayingStatus Menu => default;

    public static PlayingStatus InField(string characterName, PlayerClass playerClass, int tier, int level, string fieldName) =>
        new(true, characterName ?? string.Empty, playerClass, tier, level, fieldName ?? string.Empty);
}
