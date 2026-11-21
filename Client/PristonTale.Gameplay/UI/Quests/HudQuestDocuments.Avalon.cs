using PristonTale.Assets;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Quests;

public sealed partial class HudQuestDocuments
{
    // The shown lines of a document by language and path; null when the language has the document as a file.
    private readonly Dictionary<(string Language, string Path), string[]> _avalonFallbacks = new();

    // Each document is one phrase whose lines are separated by line breaks.
    private static readonly Dictionary<string, string> AvalonDocuments = new(StringComparer.OrdinalIgnoreCase)
    {
        [BoardPath("Avalon/MeetSkadi.msg")] = "QuestDocument.Avalon.MeetSkadi",
        [BoardPath("Avalon/MeetJin.msg")] = "QuestDocument.Avalon.MeetJin",
        [BoardPath("Avalon/Preparation.msg")] = "QuestDocument.Avalon.Preparation",
        [BoardPath("Avalon/SkadiHunt.msg")] = "QuestDocument.Avalon.SkadiHunt",
        [BoardPath("Avalon/JinHunt.msg")] = "QuestDocument.Avalon.JinHunt",
        [BoardPath("Avalon/Finished.msg")] = "QuestDocument.Avalon.Finished",
        [DialogPath("avalon_journey_start")] = "QuestDocument.Avalon.JourneyStart",
        [DialogPath("avalon_journey_bag")] = "QuestDocument.Avalon.JourneyBag",
        [DialogPath("avalon_journey_hunt_skadi")] = "QuestDocument.Avalon.JourneyHuntSkadi",
        [DialogPath("avalon_journey_hunt_jin")] = "QuestDocument.Avalon.JourneyHuntJin",
        [DialogPath("avalon_journey_collect")] = "QuestDocument.Avalon.JourneyCollect",
        [DialogPath("avalon_journey_finish")] = "QuestDocument.Avalon.JourneyFinish"
    };

    private string[] AvalonFallback(string relativePath)
    {
        var key = relativePath.Replace('\\', '/');
        if (!AvalonDocuments.TryGetValue(key, out var phrase))
        {
            return null;
        }

        var languageKey = (ClientText.Language, key);
        if (!_avalonFallbacks.TryGetValue(languageKey, out var lines))
        {
            var exists = AssetFiles.Exists(Path.Combine(assetRoot, key))
                || File.Exists(Path.Combine(HudTextFiles.DefaultTranslationRoot, ClientText.Language, key));
            lines = exists ? null : ClientText.Get(phrase).Split('\n');
            _avalonFallbacks[languageKey] = lines;
        }

        return lines;
    }

    internal static string AvalonObjective(string path)
    {
        if (path.EndsWith("/MeetSkadi.msg", StringComparison.OrdinalIgnoreCase))
        {
            return ClientText.Get("QuestDocument.Avalon.ObjectiveSkadi");
        }
        if (path.EndsWith("/MeetJin.msg", StringComparison.OrdinalIgnoreCase))
        {
            return ClientText.Get("QuestDocument.Avalon.ObjectiveJin");
        }
        if (path.EndsWith("/Preparation.msg", StringComparison.OrdinalIgnoreCase))
        {
            return ClientText.Get("QuestDocument.Avalon.ObjectivePreparation");
        }
        return ClientText.Get("QuestDocument.Avalon.ObjectiveOther");
    }

    internal static string AvalonReward(string path)
    {
        if (path.EndsWith("/Preparation.msg", StringComparison.OrdinalIgnoreCase))
        {
            return ClientText.Get("QuestDocument.Avalon.RewardPreparation");
        }
        if (path.EndsWith("/SkadiHunt.msg", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/JinHunt.msg", StringComparison.OrdinalIgnoreCase))
        {
            return "EXP + 300 Gold";
        }
        if (path.EndsWith("/MeetSkadi.msg", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/MeetJin.msg", StringComparison.OrdinalIgnoreCase))
        {
            return ClientText.Get("QuestDocument.Avalon.RewardMeet");
        }
        return ClientText.Get("QuestDocument.Avalon.RewardOther");
    }
}
