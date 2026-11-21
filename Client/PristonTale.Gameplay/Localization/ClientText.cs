using System.Globalization;
using System.Resources;
using PristonTale.Engine3D.Rendering;
using PristonTale.Contracts.Texts;
using System.Collections.Concurrent;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items.Mixing;

namespace PristonTale.Gameplay.Localization;

// The client's phrases: ClientText.resx holds the English ones, ClientText.<language>.resx their translations, keyed
// by name. The shown language follows client-settings.json (Use); a phrase a translation lacks falls back to English.
// Formats take their arguments in invariant culture.
public static class ClientText
{
    private static readonly ResourceManager Resources = new(typeof(ClientText));
    private static readonly ConcurrentDictionary<GameTextId, string> GameKeys = new();
    private static CultureInfo _culture = CultureInfo.GetCultureInfo(ClientSettings.EnglishLanguage);

    public static string Language { get; private set; } = ClientSettings.EnglishLanguage;

    public static CultureInfo Culture => _culture;

    public static void Use(string language)
    {
        var normalized = ClientSettings.NormalizeLanguage(language);
        if (normalized != Language)
        {
            Language = normalized;
            _culture = CultureInfo.GetCultureInfo(normalized);
        }
    }

    public static string Get(string key) => Resources.GetString(key, _culture) ?? key;

    // A phrase in a language other than the shown one, such as the one a browser page asks for.
    public static string Get(string key, string language) =>
        Resources.GetString(key, CultureInfo.GetCultureInfo(ClientSettings.NormalizeLanguage(language))) ?? key;

    public static string Format(string key, object arg0) => string.Format(CultureInfo.InvariantCulture, Get(key), arg0);

    public static string Format(string key, object arg0, object arg1) => string.Format(CultureInfo.InvariantCulture, Get(key), arg0, arg1);

    public static string Format(string key, object arg0, object arg1, object arg2) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), arg0, arg1, arg2);

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);

    // A line of the server or the simulation, worded by the phrase GameKey names.
    public static string Format(GameText text) => text.IsNone ? ""
        : text.Id == GameTextId.ClassTitle ? ClassTitle(text)
        : text.Args.IsEmpty ? Get(GameKey(text.Id))
        : text.Id is GameTextId.QuestName or GameTextId.EliteQuestProgress or GameTextId.EliteQuestDone ? TitledQuest(text)
        : text.Id is GameTextId.TitleGained or GameTextId.TitleLost or GameTextId.TitleEarned or GameTextId.TitleTaken ? Titled(text)
        : string.Format(CultureInfo.InvariantCulture, Get(GameKey(text.Id)), [.. text.Args]);

    // The quest texts whose first argument is a quest window title, worded from the client's data.
    private static string TitledQuest(GameText text)
    {
        object[] args = [.. text.Args];
        args[0] = DataText.QuestTitle(text.Args[0]);
        return string.Format(CultureInfo.InvariantCulture, Get(GameKey(text.Id)), args);
    }

    // The title texts, whose title argument (the first of TitleEarned and TitleTaken, the second of the others) is a code.
    private static string Titled(GameText text)
    {
        object[] args = [.. text.Args];
        var index = text.Id is GameTextId.TitleEarned or GameTextId.TitleTaken ? 0 : 1;
        if (index < args.Length && int.TryParse(text.Args[index], CultureInfo.InvariantCulture, out var code))
        {
            args[index] = TitleName(code);
        }

        return string.Format(CultureInfo.InvariantCulture, Get(GameKey(text.Id)), args);
    }

    // A title's name by its code (TitleRules), empty for none; the keys are built once, as the name boxes ask every frame.
    public static string TitleName(int code) => Simulation.Titles.TitleRules.IsTitle(code) ? Get(TitleKeys[code]) : "";

    private static readonly string[] TitleKeys = Enumerable.Range(0, Simulation.Titles.TitleRules.Count)
        .Select(static code => "Title." + (Simulation.Titles.TitleCode)code).ToArray();

    public static string Get(GameTextId id) => Get(GameKey(id));

    private static string ClassTitle(GameText text) =>
        text.Args is [var playerClass, var tier] && int.TryParse(playerClass, CultureInfo.InvariantCulture, out var number)
            && Enum.IsDefined((PlayerClass)number) && int.TryParse(tier, CultureInfo.InvariantCulture, out var index)
            ? DataText.ClassTitle((PlayerClass)number, index)
            : "";

    // Built once per id: the HUD words some game texts every frame.
    // A mix's bonus by its stat ("Mix.Bonus.<MixStat>"): the regeneration and absorption values keep one decimal.
    public static string MixBonus(MixBonus bonus)
    {
        var decimals = bonus.Stat is Simulation.Items.Mixing.MixStat.Absorb or Simulation.Items.Mixing.MixStat.LifeRegen
            or Simulation.Items.Mixing.MixStat.ManaRegen or Simulation.Items.Mixing.MixStat.StaminaRegen;
        var value = bonus.Value.ToString(decimals ? "0.0" : "0.##", CultureInfo.InvariantCulture) + (bonus.Percent ? "%" : "");
        return Format("Mix.Bonus." + bonus.Stat, value);
    }

    public static string GameKey(GameTextId id) => GameKeys.GetOrAdd(id, static key => "Game." + key);
}
