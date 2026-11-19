using PristonTale.Gameplay.Effects;

namespace PristonTale.Tests;

// The "limit other players' effects" setting: which skill effects it hides, which it counts and which always show.
internal static class OtherPlayerEffectsTests
{
    private const ulong Self = 1;
    private const ulong Other = 2;
    private const ulong Selected = 3;
    private const ulong Monster = 4;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("other players' effects are hidden far from the camera and capped near it, while the player's, its target's and monsters' always show",
            ClassifyAsync);
    }

    private static Task ClassifyAsync()
    {
        var near = 100f * 100f;
        var far = (OtherPlayerEffects.Distance + 1f) * (OtherPlayerEffects.Distance + 1f);
        Assert.True(OtherPlayerEffects.Classify(true, Other, Monster, Self, Selected, true, near) == EffectLimit.Counted
            && OtherPlayerEffects.Classify(true, Other, Monster, Self, Selected, true, far) == EffectLimit.Hidden,
            "another player's effect was not counted near the camera or hidden far from it");
        Assert.True(OtherPlayerEffects.Classify(false, Other, Monster, Self, Selected, true, far) == EffectLimit.None
            && OtherPlayerEffects.Classify(true, Self, Monster, Self, Selected, true, far) == EffectLimit.None
            && OtherPlayerEffects.Classify(true, Other, Self, Self, Selected, true, far) == EffectLimit.None
            && OtherPlayerEffects.Classify(true, Selected, Monster, Self, Selected, true, far) == EffectLimit.None
            && OtherPlayerEffects.Classify(true, Other, Selected, Self, Selected, true, far) == EffectLimit.None
            && OtherPlayerEffects.Classify(true, Monster, Other, Self, Selected, false, far) == EffectLimit.None,
            "an effect the setting must leave alone was limited");
        Assert.True(OtherPlayerEffects.Allows(EffectLimit.Counted, OtherPlayerEffects.Limit - 1)
            && !OtherPlayerEffects.Allows(EffectLimit.Counted, OtherPlayerEffects.Limit)
            && !OtherPlayerEffects.Allows(EffectLimit.Hidden, 0)
            && OtherPlayerEffects.Allows(EffectLimit.None, OtherPlayerEffects.Limit * 2),
            "the count near the camera did not cap other players' effects");
        return Task.CompletedTask;
    }
}
