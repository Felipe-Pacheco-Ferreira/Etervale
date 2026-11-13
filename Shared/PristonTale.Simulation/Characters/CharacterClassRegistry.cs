using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Characters.Classes;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Characters;

public sealed class CharacterClassRegistry
{
    private readonly Dictionary<PlayerClass, CharacterClass> _byClass;

    public CharacterClassRegistry()
    {
        CharacterClass[] all =
        [
            new FighterClass(),
            new MechanicianClass(),
            new ArcherClass(),
            new PikemanClass(),
            new AtalantaClass(),
            new KnightClass(),
            new MagicianClass(),
            new PriestessClass(),
            new AssassinClass(),
            new ShamanClass(),
            new MartialArtistClass()
        ];
        _byClass = all.ToDictionary(static c => c.Class);
        All = all;
    }

    public IReadOnlyList<CharacterClass> All { get; }

    public CharacterClass Get(PlayerClass playerClass) => _byClass[playerClass];

    // The attribute the class's damage grows with, by the kind of its own weapon (the family of its beginner weapon).
    public CharacterAttribute DamageAttribute(PlayerClass playerClass) =>
        Combat.WeaponDamage.DamageAttribute(Items.ItemEquipmentMetadata.KindOf(ClassProfiles.Of(playerClass).BeginnerWeapon),
            Get(playerClass).Definition.FunctionProfile);

    public PlayerCharacter CreatePlayer(PlayerClass playerClass, string name, int level = 1) =>
        new(Get(playerClass), name, level);
}
