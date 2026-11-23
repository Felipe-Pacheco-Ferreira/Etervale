using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI;

// The touch HUD's quest card and journal, drawn by the page as the Android client's (touch-quests.js). The canvas quest
// tracker and quest window stay out while the touch HUD is in use; opening the quest window asks the page for its
// journal instead.
public sealed partial class GameplayHud
{
    private const string TouchQuestGoAction = "touch-quest-go";

    // The card's space under the minimap, or from the top when there is none, in layout pixels (the Android card's 5 dp).
    private const int TouchQuestCardGap = 5;

    // Grows each time the quest window was asked for while the page draws the journal.
    private int _touchJournalRequests;

    // Whether the quest window opening goes to the page's journal (true) or stays the canvas window.
    private bool TouchJournalRequested()
    {
        if (!TouchHudShown)
        {
            return false;
        }

        _partyWindow.Close();
        _questWindow.Close();
        _touchJournalRequests++;
        return true;
    }

    // The quest card: the step, the hunt's count, the timer, whether the automatic quest (the journey's guide or the quest
    // hunt) runs or could start, where the card sits under the minimap; with journal, every running step and finished
    // quest with its description's paragraphs (null while the file loads) and reward.
    internal string TouchQuest(bool journal)
    {
        if (TouchHudShown && _questWindow.Open)
        {
            TouchJournalRequested();
        }

        var view = _questView;
        var layout = Layout();
        var top = MinimapShown ? Widgets.HudMinimap.Box(layout).Bottom + TouchQuestCardGap : TouchQuestCardGap;
        var step = view?.Progress.Count > 0 ? view.Progress[^1] : null;
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteBoolean("shown", QuestTrackingEnabled);
            json.WriteNumber("top", layout.Height > 0 ? (float)top / layout.Height : 0f);
            json.WriteNumber("requests", _touchJournalRequests);
            json.WriteNumber("revision", view?.Revision ?? -1);
            json.WriteString("language", ClientText.Language);
            WriteTouchQuestCount(json, view);
            json.WriteBoolean("journey", view is not null && step is not null && (view.Code == AdventurerJourneyRoute.Code
                || step.Description is AdventurerJourneyRoute.MeetSkadiDescription or AdventurerJourneyRoute.MeetJinDescription));
            json.WriteNumber("timerMs", QuestTimeLeftMs);
            // Without a hunt the step's own line; the page words a hunt from its monster.
            json.WriteString("objective", step is null || view.KillsNeeded > 0 ? "" : HudQuestDocuments.AvalonObjective(HudQuestDocuments.BoardPath(step.Description)));
            json.WriteBoolean("hunting", AutoHuntEnabled);
            json.WriteBoolean("guiding", QuestGuideEnabled);
            json.WriteBoolean("canHunt", !InTown && AutoHuntQuestCode != 0);
            json.WriteBoolean("canGuide", CanNavigateTrackedQuest);
            if (journal)
            {
                WriteTouchEntries(json, "running", view?.Progress);
                WriteTouchEntries(json, "finished", view?.Finished);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The tracked step's title and the hunt's monster and count (held to what it needs), for the quest card and the map.
    private static void WriteTouchQuestCount(Utf8JsonWriter json, QuestView view)
    {
        var step = view?.Progress.Count > 0 ? view.Progress[^1] : null;
        json.WriteString("step", step is null ? "" : DataText.QuestTitle(step.Title));
        json.WriteString("target", view?.TargetName ?? "");
        json.WriteNumber("kills", view is null ? 0 : Math.Min(view.Kills, view.KillsNeeded));
        json.WriteNumber("killsNeeded", view?.KillsNeeded ?? 0);
    }

    private void WriteTouchEntries(Utf8JsonWriter json, string name, IReadOnlyList<QuestBoardEntry> entries)
    {
        json.WriteStartArray(name);
        foreach (var entry in entries ?? [])
        {
            var path = HudQuestDocuments.BoardPath(entry.Description);
            var lines = _questDocuments.Lines(path);
            json.WriteStartObject();
            json.WriteString("title", DataText.QuestTitle(entry.Title));
            json.WriteString("reward", HudQuestDocuments.AvalonReward(path));
            if (lines is null)
            {
                json.WriteNull("paragraphs");
            }
            else
            {
                json.WriteStartArray("paragraphs");
                foreach (var paragraph in HudQuestDocuments.Paragraphs(lines))
                {
                    json.WriteStringValue(paragraph);
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private void RunTouchQuestGo()
    {
        switch (TouchQuestGo.Next(QuestGuideEnabled, AutoHuntEnabled, CanNavigateTrackedQuest))
        {
            case TouchQuestStep.Pause:
                if (QuestGuideEnabled)
                {
                    ToggleQuestGuide();
                }

                AutoHuntEnabled = false;
                break;
            case TouchQuestStep.Guide:
                ToggleQuestGuide();
                break;
            default:
                ToggleQuickHunt();
                break;
        }
    }
}
