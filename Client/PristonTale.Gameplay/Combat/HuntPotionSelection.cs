using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.Combat;

public static class HuntPotionSelection
{
    public static OwnedItemView Select(IReadOnlyList<OwnedItemView> items, PotionResource family, string preferred,
        int needed, bool bagOnly = false)
    {
        OwnedItemView best = null;
        OwnedItemView unknown = null;
        var bestScore = (Insufficient: true, Minimum: int.MaxValue, Maximum: int.MaxValue, InBag: true);
        foreach (var item in items)
        {
            if (item.Kind != ItemKind.Potion || item.Count <= 0 || !(item.InBag || !bagOnly && item.BeltBox >= 0)
                || PotionDefinition.FamilyOf(item.CatalogKey) != family)
            {
                continue;
            }

            if (item.CatalogKey.Equals(preferred, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            var (minimum, maximum) = Amount(item.Potion, family);
            if (maximum <= 0)
            {
                if (unknown is null || item.Code > unknown.Code)
                {
                    unknown = item;
                }
                continue;
            }

            // Prefer the smallest guaranteed dose that reaches the threshold. If none do, prefer the strongest dose.
            var insufficient = minimum < needed;
            var score = (insufficient, insufficient ? -minimum : minimum, insufficient ? -maximum : maximum, item.InBag);
            if (best is null || score.CompareTo(bestScore) < 0)
            {
                best = item;
                bestScore = score;
            }
        }

        return best ?? unknown;
    }

    private static (int Min, int Max) Amount(PotionDefinition potion, PotionResource family) => (potion, family) switch
    {
        ({ } value, PotionResource.Life) => (value.LifeMin, value.LifeMax),
        ({ } value, PotionResource.Mana) => (value.ManaMin, value.ManaMax),
        ({ } value, PotionResource.Stamina) => (value.StaminaMin, value.StaminaMax),
        _ => (0, 0)
    };
}
