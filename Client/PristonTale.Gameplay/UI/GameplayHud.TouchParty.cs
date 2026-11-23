using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.UI;

internal enum TouchPartyAction
{
    Player,
    Command,
    Yes,
    No
}

internal enum TouchRequestKind
{
    Party,
    Clan,
    Trade,
    Duel
}

// The touch HUD's party pieces, drawn by the page as the Android client's (touch-party.js): the row of buttons under a
// reached player's target frame (trade, party, friend, clan for a recruiter, duel), the other members' plates under the
// player frame with the party's commands on a tap, and the plate of a party, clan, trade or duel request. While the touch
// HUD is in use the canvas leaves the buttons and the request boxes out, drawn and taking taps.
public sealed partial class GameplayHud
{
    private const string TouchPartyPrefix = "touch-party:";
    private const string TouchDuelButton = "Duel";
    private static readonly PartyCommand[] TouchMemberCommands = [PartyCommand.Delegate, PartyCommand.Kick, PartyCommand.Disband, PartyCommand.Leave];

    internal string TouchParty()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartArray("buttons");
            if (ShowsPlayerButtons)
            {
                json.WriteStringValue(nameof(HudPlayerButton.Trade));
                json.WriteStringValue(nameof(HudPlayerButton.Party));
                json.WriteStringValue(nameof(HudPlayerButton.Friend));
                if (ClanButton)
                {
                    json.WriteStringValue(nameof(HudPlayerButton.Clan));
                }

                json.WriteStringValue(TouchDuelButton);
            }

            json.WriteEndArray();
            json.WriteBoolean("leads", Party.Leads(SelfId));
            json.WriteStartArray("members");
            foreach (var member in Party.Members)
            {
                if (member.EntityId == SelfId)
                {
                    continue;
                }

                json.WriteStartObject();
                json.WriteString("id", member.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                json.WriteString("name", member.Name ?? "");
                json.WriteNumber("level", member.Level);
                json.WriteNumber("life", member.Life);
                json.WriteNumber("maxLife", member.MaxLife);
                json.WriteBoolean("leader", Party.Leads(member.EntityId));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (TouchRequest() is { } shown)
            {
                json.WriteStartObject("request");
                json.WriteString("kind", shown.Kind.ToString());
                json.WriteString("name", shown.Box.FromName);
                json.WriteString("line", shown.Box.Line ?? "");
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The request the canvas would show on top: the boxes share one place and draw party, clan, trade, then duel.
    private (TouchRequestKind Kind, HudRequestBox Box)? TouchRequest() =>
        _duelRequest.IsOpen ? (TouchRequestKind.Duel, _duelRequest)
        : _tradeRequest.IsOpen ? (TouchRequestKind.Trade, _tradeRequest)
        : _clanRequest.IsOpen ? (TouchRequestKind.Clan, _clanRequest)
        : _partyRequest.IsOpen ? (TouchRequestKind.Party, _partyRequest)
        : null;

    // "touch-party:player:<Trade|Party|Friend|Clan|Duel>", "touch-party:command:<Delegate|Kick|Disband|Leave>:<member id>"
    // and "touch-party:yes|no:<Party|Clan|Trade|Duel>". Value is the button, command or request kind; member the id.
    internal static bool TryReadPartyAction(string action, out TouchPartyAction kind, out string value, out ulong member)
    {
        kind = default;
        value = "";
        member = 0;
        if (action is null || !action.StartsWith(TouchPartyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = action[TouchPartyPrefix.Length..].Split(':');
        if (parts.Length < 2 || !TryParseName(parts[0], out kind))
        {
            return false;
        }

        value = parts[1];
        return kind switch
        {
            TouchPartyAction.Player => parts.Length == 2 && (value == TouchDuelButton || TryParseName<HudPlayerButton>(value, out _)),
            TouchPartyAction.Command => parts.Length == 3 && TryParseName<PartyCommand>(value, out var command) && Array.IndexOf(TouchMemberCommands, command) >= 0
                && ulong.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out member),
            _ => parts.Length == 2 && TryParseName<TouchRequestKind>(value, out _)
        };
    }

    // A name of the enum, never a number.
    private static bool TryParseName<T>(string text, out T value) where T : struct, Enum =>
        Enum.TryParse(text, ignoreCase: true, out value) && text.Length > 0 && char.IsLetter(text[0]) && Enum.IsDefined(value);

    private bool RunTouchParty(string action)
    {
        if (!action.StartsWith(TouchPartyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryReadPartyAction(action, out var kind, out var value, out var member))
        {
            return true;
        }

        switch (kind)
        {
            case TouchPartyAction.Player when ShowsPlayerButtons && value == TouchDuelButton:
                ChallengeReached();
                break;
            case TouchPartyAction.Player when ShowsPlayerButtons && TryParseName<HudPlayerButton>(value, out var button)
                && (button != HudPlayerButton.Clan || ClanButton):
                UsePlayerButton(button);
                break;
            case TouchPartyAction.Command when TryParseName<PartyCommand>(value, out var command) && TouchCommandAllowed(command, member):
                Parties?.CommandPartyAsync(command, member);
                break;
            case TouchPartyAction.Yes or TouchPartyAction.No when TryParseName<TouchRequestKind>(value, out var request):
                AnswerTouchRequest(request, kind == TouchPartyAction.Yes);
                break;
        }

        return true;
    }

    // As the Android client offers them: leaving for anyone, the lead, removing a member and ending the party for its leader.
    private bool TouchCommandAllowed(PartyCommand command, ulong member) => command switch
    {
        PartyCommand.Leave => Party.Members.Count > 0,
        PartyCommand.Disband => Party.Leads(SelfId),
        _ => Party.Leads(SelfId) && member != SelfId && Party.Contains(member)
    };

    private void AnswerTouchRequest(TouchRequestKind kind, bool accept)
    {
        var box = kind switch
        {
            TouchRequestKind.Party => _partyRequest,
            TouchRequestKind.Clan => _clanRequest,
            TouchRequestKind.Trade => _tradeRequest,
            _ => _duelRequest
        };
        if (!box.IsOpen)
        {
            return;
        }

        var from = box.FromId;
        box.Close();
        switch (kind)
        {
            case TouchRequestKind.Party:
                Parties?.AnswerPartyAsync(from, accept);
                break;
            case TouchRequestKind.Clan:
                AnswerClan(accept);
                break;
            case TouchRequestKind.Trade:
                Trades?.AnswerTradeAsync(from, accept);
                break;
            default:
                Duels?.AnswerDuelAsync(from, accept);
                break;
        }
    }
}
