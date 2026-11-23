using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI;

internal enum TouchNpcAction
{
    Service,
    Destination,
    Close,
    Ok,
    Choose,
    Browse,
    Yes,
    No
}

// The touch HUD's NPC plate, drawn by the page as the Android client's (touch-npc.js): an NPC's dialog with its service
// or destination buttons, a quest's dialog with its choices or browsed rewards, and the yes/no boxes that follow a quest
// dialog or offer a skill reset. While the touch HUD is in use the canvas leaves these out, drawn and taking taps.
public sealed partial class GameplayHud
{
    private const string TouchNpcPrefix = "touch-npc:";

    // What the plate shows, in the canvas's order: a yes/no box over a quest dialog over the NPC's own dialog (which the
    // clan, castle and Bellatra windows also replace). Kind is "" when nothing shows.
    internal string TouchNpc()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteBoolean("pending", _quests.Pending);
            if (_quests.Confirming && _quests.Dialog is { } confirmed)
            {
                WriteTouchQuestion(json, ClientText.Format(confirmed.Title), _quests.ConfirmDetail,
                    HudQuestDialog.Question(ClientText.Format(confirmed.ConfirmLine1), ClientText.Format(confirmed.ConfirmLine2)));
            }
            else if (_quests.SkillResetBox)
            {
                WriteTouchQuestion(json, "", ClientText.Format(QuestTexts.SkillPointName),
                    HudQuestDialog.Question(ClientText.Format(QuestTexts.Redistribute.Item1), ClientText.Format(QuestTexts.Redistribute.Item2)));
            }
            else if (_quests.Dialog is { } dialog)
            {
                WriteTouchQuestDialog(json, dialog);
            }
            else if (_npcOpen && _npc.Current is { } current && !_clanWindow.IsOpen && !_castleWindow.IsOpen && !_bellatraWindow.IsOpen)
            {
                json.WriteString("kind", "npc");
                json.WriteString("name", current.Name ?? "");
                json.WriteStartArray("lines");
                if (_npc.Destinations.Count > 0)
                {
                    json.WriteStringValue(ClientText.Get(HudNpcPanel.WhereTo));
                }
                else
                {
                    foreach (var line in HudNpcPanel.Said(current))
                    {
                        json.WriteStringValue(line);
                    }
                }

                json.WriteEndArray();
                json.WriteBoolean("destinations", _npc.Destinations.Count > 0);
                json.WriteStartArray("buttons");
                if (_npc.Destinations.Count > 0)
                {
                    foreach (var destination in _npc.Destinations)
                    {
                        WriteTouchNpcButton(json, HudNpcPanel.DestinationLabel(destination), true);
                    }
                }
                else
                {
                    foreach (var service in _npc.Services)
                    {
                        WriteTouchNpcButton(json, HudNpcArt.Label(service.Kind, service.Value), service.Available);
                    }
                }

                json.WriteEndArray();
            }
            else
            {
                json.WriteString("kind", "");
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The quest dialog: its title, the NPC it answers to, the document's paragraphs (null while the file loads), what OK
    // leads to, the choices of a reward list and the browsed reward of a reward view.
    private void WriteTouchQuestDialog(Utf8JsonWriter json, QuestPrompt dialog)
    {
        json.WriteString("kind", "quest");
        json.WriteString("title", ClientText.Format(dialog.Title));
        json.WriteString("name", _npc.Current is { } npc && npc.EntityId == _quests.DialogNpc ? npc.Name ?? "" : "");
        json.WriteString("followup", dialog.Followup.ToString());
        var lines = _questDocuments.Lines(HudQuestDocuments.DialogPath(_quests.DialogDocument));
        if (lines is null)
        {
            json.WriteNull("lines");
        }
        else
        {
            var subject = ClientText.Format(dialog.Subject);
            json.WriteStartArray("lines");
            foreach (var paragraph in HudQuestDocuments.Paragraphs(lines))
            {
                json.WriteStringValue(HudQuestDocuments.WithSubject(paragraph, subject));
            }

            json.WriteEndArray();
        }

        json.WriteStartArray("choices");
        if (dialog.Followup == QuestFollowup.ChooseFromList)
        {
            foreach (var choice in dialog.Choices)
            {
                json.WriteStringValue(choice);
            }
        }

        json.WriteEndArray();
        var browsed = dialog.Followup == QuestFollowup.ConfirmChoice && dialog.Choices.Count > 1;
        json.WriteNumber("browse", browsed ? _quests.Browse : 0);
        json.WriteNumber("browseCount", browsed ? dialog.Choices.Count : 0);
        json.WriteString("reward", browsed ? _quests.ConfirmDetail : "");
    }

    private static void WriteTouchQuestion(Utf8JsonWriter json, string title, string detail, string question)
    {
        json.WriteString("kind", "confirm");
        json.WriteString("title", title);
        json.WriteString("detail", detail);
        json.WriteString("question", question);
    }

    private static void WriteTouchNpcButton(Utf8JsonWriter json, string label, bool available)
    {
        json.WriteStartObject();
        json.WriteString("label", label);
        json.WriteBoolean("available", available);
        json.WriteEndObject();
    }

    // "touch-npc:<action>[:<value>]": the value is a button's index, or the reward browse's step.
    internal static bool TryReadNpcAction(string action, out TouchNpcAction kind, out int value)
    {
        kind = default;
        value = 0;
        if (action is null || !action.StartsWith(TouchNpcPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = action[TouchNpcPrefix.Length..].Split(':');
        if (parts[0].Length == 0 || !char.IsLetter(parts[0][0]) || !Enum.TryParse(parts[0], ignoreCase: true, out kind) || !Enum.IsDefined(kind))
        {
            return false;
        }

        var valued = kind is TouchNpcAction.Service or TouchNpcAction.Destination or TouchNpcAction.Choose or TouchNpcAction.Browse;
        if (!valued)
        {
            return parts.Length == 1;
        }

        return parts.Length == 2 && int.TryParse(parts[1], out value) && (kind == TouchNpcAction.Browse ? value is -1 or 1 : value >= 0);
    }

    private bool RunTouchNpc(string action)
    {
        if (!action.StartsWith(TouchNpcPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadNpcAction(action, out var kind, out var value))
        {
            return true;
        }

        switch (kind)
        {
            case TouchNpcAction.Service when _npcOpen && _npc.Destinations.Count == 0 && value < _npc.Services.Count && _npc.Services[value].Available:
                OpenNpcService(_npc.Services[value]);
                break;
            case TouchNpcAction.Destination when _npcOpen && _state is { } state && value < _npc.Destinations.Count:
                ChooseNpcDestination(state, _npc.Destinations[value]);
                break;
            case TouchNpcAction.Close when _quests.Dialog is not null:
                _quests.CloseDialog();
                break;
            case TouchNpcAction.Close when _npcOpen:
                CloseNpc();
                break;
            case TouchNpcAction.Ok:
                _quests.Ok();
                break;
            case TouchNpcAction.Choose:
                _quests.Choose(value);
                break;
            case TouchNpcAction.Browse:
                _quests.BrowseBy(value);
                break;
            case TouchNpcAction.Yes or TouchNpcAction.No when _quests.Confirming:
                _quests.Answer(kind == TouchNpcAction.Yes);
                break;
            case TouchNpcAction.Yes or TouchNpcAction.No:
                _quests.AnswerSkillReset(kind == TouchNpcAction.Yes);
                break;
        }

        return true;
    }
}
