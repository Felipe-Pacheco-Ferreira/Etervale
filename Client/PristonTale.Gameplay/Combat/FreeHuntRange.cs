using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.Combat;

public static class FreeHuntRange
{
    public const int MinimumMetres = 5;
    public const int DefaultMetres = 15;
    public const int MaximumMetres = 30;

    public static int Clamp(int metres) => Math.Clamp(metres, MinimumMetres, MaximumMetres);

    public static float InWorldUnits(int metres) => Clamp(metres) * GameUnits.WorldUnitsPerMetre;
}
