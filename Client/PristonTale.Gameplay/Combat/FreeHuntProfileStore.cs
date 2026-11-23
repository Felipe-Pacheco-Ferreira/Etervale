using System.Security.Cryptography;
using System.Text;

namespace PristonTale.Gameplay.Combat;

// Where the free hunt's options are kept (ClientSettings.FreeHuntProfiles, transfer codes): each character's own, and,
// once a character shares them with its account, the account's, which every character of the account on this client
// takes, each keeping its own skill list and mana floors. The account is named by a hash of its server and name, never in
// plain text.
public static class FreeHuntProfileStore
{
    private const string CharacterPrefix = "character:";
    private const string AccountPrefix = "account:";

    public static string CharacterKey(string character) => CharacterPrefix + character;

    public static string AccountKey(string scope) =>
        AccountPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope ?? "")));

    public static FreeHuntSettings Load(IReadOnlyDictionary<string, string> profiles, string character, string scope)
    {
        var personal = Read(profiles, CharacterKey(character)) ?? new FreeHuntSettings();
        var shared = scope.Length > 0 ? Read(profiles, AccountKey(scope)) : null;
        var settings = shared is not null
            ? shared with { ShareAccount = true, Skills = personal.Skills, SkillManaFloor = personal.SkillManaFloor }
            : personal with { ShareAccount = false };
        return settings.UpgradeLootCategories();
    }

    // The profiles with the character's options kept, and the account's kept or dropped as the options share them.
    public static Dictionary<string, string> Save(IReadOnlyDictionary<string, string> profiles, string character, string scope, FreeHuntSettings settings)
    {
        var saved = new Dictionary<string, string>(profiles ?? new Dictionary<string, string>())
        {
            [CharacterKey(character)] = FreeHuntProfile.Encode(settings)
        };
        if (scope.Length > 0)
        {
            if (settings.ShareAccount)
            {
                saved[AccountKey(scope)] = FreeHuntProfile.Encode(settings with { Skills = [], SkillManaFloor = [] });
            }
            else
            {
                saved.Remove(AccountKey(scope));
            }
        }

        return saved;
    }

    private static FreeHuntSettings Read(IReadOnlyDictionary<string, string> profiles, string key) =>
        profiles is not null && profiles.TryGetValue(key, out var code) && FreeHuntProfile.TryDecode(code, out var settings) ? settings : null;
}
