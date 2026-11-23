using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.Discord;

// The activity Discord shows on the player's profile: the application's name comes from the Discord application, then
// Details ("SatroX · Level 71"), State ("Sagittarion · Ricarten"), the time elapsed since StartUnixSeconds, the game's
// logo as the large image and the class's emblem as the small one. Image keys name the application's art assets.
public sealed record DiscordActivity(
    string Details,
    string State,
    long? StartUnixSeconds,
    string LargeImage,
    string LargeText,
    string SmallImage,
    string SmallText)
{
    public const string GameName = "Priston Avalon";
    public const string LogoImage = "logo";
    public const string ClassImagePrefix = "class_";
    public const string Separator = " · ";

    // Discord refuses a details or state line shorter than 2 or longer than 128 characters.
    public const int ShortestText = 2;
    public const int LongestText = 128;

    public static DiscordActivity For(PlayingStatus status, long? startUnixSeconds)
    {
        if (!status.InWorld)
        {
            return new DiscordActivity(Fit(ClientText.Get("Discord.Menu")), null, null, LogoImage, GameName, null, null);
        }

        var title = DataText.ClassTitle(status.Class, status.Tier);
        return new DiscordActivity(
            Fit(ClientText.Format("Discord.NameLevel", status.CharacterName, status.Level)),
            Fit(string.IsNullOrEmpty(status.FieldName) ? title : title + Separator + status.FieldName),
            startUnixSeconds,
            LogoImage,
            GameName,
            ClassImage(status.Class),
            Fit(title));
    }

    public static string ClassImage(PlayerClass playerClass) => ClassImagePrefix + playerClass.ToString().ToLowerInvariant();

    private static string Fit(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < ShortestText)
        {
            return null;
        }

        return text.Length <= LongestText ? text : text[..LongestText];
    }
}
