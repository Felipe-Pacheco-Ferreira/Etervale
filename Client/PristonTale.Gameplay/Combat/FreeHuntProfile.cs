using System.Text.Json;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.Combat;

public static class FreeHuntProfile
{
    private const string Prefix = "ETERVALE-HUNT-1:";
    public const int MaximumCodeLength = 32_768;

    public static string Encode(FreeHuntSettings settings) =>
        Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(settings with { ShareAccount = false }));

    // An imported code: the options it carries, never the destination's account sharing.
    public static bool TryDecode(string code, out FreeHuntSettings settings)
    {
        settings = null;
        code = code?.Trim();
        if (code is null || code.Length > MaximumCodeLength || !code.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] json;
        try
        {
            json = Convert.FromBase64String(code[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        return TryRead(json, keepAccount: false, out settings);
    }

    // The hunt panel's options as JSON, checked as a code is, keeping the account sharing it chose.
    public static bool TryRead(string json, out FreeHuntSettings settings) =>
        TryRead(System.Text.Encoding.UTF8.GetBytes(json ?? ""), keepAccount: true, out settings);

    private static bool TryRead(byte[] json, bool keepAccount, out FreeHuntSettings settings)
    {
        settings = null;
        try
        {
            var profile = JsonSerializer.Deserialize<FreeHuntSettings>(json);
            if (profile is null || profile.Skills is null || profile.SkillManaFloor is null || profile.LevelByCategory is null
                || !ValidPotion(profile.HP) || !ValidPotion(profile.MP) || !ValidPotion(profile.RES)
                || profile.Radius is < FreeHuntSettings.UnitsPerStep or > FreeHuntSettings.MaximumRadius
                || profile.LootRadius is < FreeHuntSettings.UnitsPerStep or > FreeHuntSettings.MaximumRadius
                || profile.SamePotionMs is < 500 or > 10_000 || profile.AnyPotionMs is < 500 or > 10_000
                || profile.ResumeMs is < 0 or > 60_000 || profile.PursuitPercent is < 0 or > 120
                || profile.TargetTimeout is < 2 or > 90 || profile.IgnoreSeconds is < 1 or > 300
                || profile.LootAfterSeconds is < 0 or > 600 || profile.CriticalHp is < 0 or > 100
                || profile.AreaMinimum is < 1 or > 40 || profile.AreaRadius is < 0 or > 2048 || profile.AreaCastRange is < 0 or > 2048
                || profile.SummonCount is < 1 or > MonsterCrystals.PetSlots
                || profile.SkillManaFloor.Values.Any(percent => percent is < 0 or > 100)
                || profile.MaxLifePotions is < 0 or > 9999 || profile.MaxManaPotions is < 0 or > 9999 || profile.MaxStaminaPotions is < 0 or > 9999
                || !Enum.IsDefined(profile.LootMode) || !Enum.IsDefined(profile.CriticalAction)
                || !Enum.IsDefined(profile.NoHealthPotion) || !Enum.IsDefined(profile.FullBag) || !Enum.IsDefined(profile.CompanionTarget))
            {
                return false;
            }

            settings = profile with
            {
                ShareAccount = keepAccount && profile.ShareAccount,
                OnlyMonsters = profile.OnlyMonsters ?? "",
                IgnoredMonsters = profile.IgnoredMonsters ?? "",
                AlwaysLoot = profile.AlwaysLoot ?? "",
                NeverLoot = profile.NeverLoot ?? "",
                ExcludedPotions = profile.ExcludedPotions ?? "",
                Crystal = profile.Crystal ?? "",
                ForceKey = profile.ForceKey ?? ""
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ValidPotion(PotionHuntRule rule) => rule is { Threshold: >= 0 and <= 100, Key: not null };
}
