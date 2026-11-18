using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.Localization;
using PristonTale.Contracts.Social;

namespace PristonTale.Gameplay.UI;

// The party: its page of the party window (D or the bar's party button; it shares its place with the quest page, so one
// closes the other), the request box, the buttons under a reached player's portrait, and the members for the minimap,
// the name boxes and the chat. The window opens when a party forms and closes when it is released, as in the original.
public sealed partial class GameplayHud
{
    private readonly HudPartyWindow _partyWindow = new();
    private readonly HudRequestBox _partyRequest = new(HudPartyArt.Request, Simulation.Parties.PartyRules.InviteMs);
    private readonly List<Vector3> _minimapParty = new(Contracts.Social.PartyLimits.MaxMembers);

    // Sends the party requests; set by the screen.
    public IPartyClient Parties { get; set; }

    public PartyInfo Party { get; private set; } = PartyInfo.None;

    // The player whose portrait shows the trade, party and friend buttons (the scene reached it); 0 for none.
    public ulong ButtonsPlayerId { get; set; }

    // A button under the portrait was pressed: the row closes until the player is reached again.
    public event Action PlayerButtonsUsed;

    // The party part of each presence update. As in the original, a request is not shown while the warehouse is open.
    public void ObserveParty(PresenceUpdate update)
    {
        if (update.Party is { } party)
        {
            var formed = Party.Members.Count == 0 && party.Members.Count > 0;
            var released = Party.Members.Count > 0 && party.Members.Count == 0;
            Party = party;
            if (party.Members.Count > 0)
            {
                _partyWindow.PartyOpen = party.IsOpen;
                _partyWindow.CreatingParty = false;
            }
            _partyWindow.Refresh(party);
            if (formed)
            {
                _questWindow.Close();
                _partyWindow.Show();
            }
            else if (released && _partyWindow.Page == HudPartyPage.Party)
            {
                _partyWindow.Close();
            }
        }

        if (update.PartyDirectory is { } directory)
        {
            _partyWindow.ObserveDirectory(directory);
        }

        if (update.PartyRequest is { } request && !_warehouse.IsOpen)
        {
            var line = request.Recommended.Length > 0
                ? ClientText.Format(Simulation.Parties.PartyMessages.Recommends(request.Recommended))
                : ClientText.Format(Simulation.Parties.PartyMessages.Request);
            _partyRequest.Show(request.FromId, request.FromName, line, _clock.Elapsed.TotalMilliseconds);
        }
    }

    public bool InParty(ulong entityId) => entityId != SelfId && Party.Contains(entityId);

    private void ToggleQuestWindow()
    {
        _partyWindow.Close();
        _questWindow.Toggle();
    }

    private void TogglePartyWindow()
    {
        _questWindow.Close();
        _partyWindow.Toggle();
    }

    private void SetUpParty()
    {
        _sprites.Request(HudPartyArt.All);
        _partyWindow.FieldName = new Simulation.World.WorldCatalog().FieldName;
        _partyWindow.Commanded = (command, member) => Parties?.CommandPartyAsync(command, member);
        _partyWindow.CreatePartyRequested = () => _ = CreatePartyAsync();
        _partyWindow.PartyOpenChanged = open => Parties?.CommandPartyAsync(open ? PartyCommand.Open : PartyCommand.Close, 0);
        _partyWindow.SearchPartiesRequested = () => Parties?.CommandPartyAsync(PartyCommand.Search, 0);
        _partyWindow.JoinPartyRequested = leader => Parties?.CommandPartyAsync(PartyCommand.Join, leader);
        _partyWindow.Whispered = _chat.Window.WhisperTo;
        _chat.InParty = InParty;
        Combat.InParty = InParty;
    }

    private async Task CreatePartyAsync()
    {
        if (Parties is null || _partyWindow.CreatingParty || Party.Members.Count > 0)
        {
            return;
        }

        _partyWindow.CreatingParty = true;
        try
        {
            // Creation and the chosen admission policy are one authoritative command. Keep the pending state until
            // its PartyInfo arrives, so repeated clicks cannot enqueue duplicate groups.
            await Parties.CommandPartyAsync(PartyCommand.Create, _partyWindow.PartyOpen ? 1UL : 0UL);
        }
        catch (Exception ex)
        {
            _partyWindow.CreatingParty = false;
            _context.Log.Error("The group creation request could not be sent", ex);
            _notice.Show(ClientText.Format(Simulation.Parties.PartyMessages.CannotEnter));
        }
    }

    private bool ShowsPlayerButtons => ButtonsPlayerId != 0 && TargetId == ButtonsPlayerId;

    private bool ClickParty(HudLayout layout, Point pointer)
    {
        var from = _partyRequest.FromId;
        if (_partyRequest.Click(pointer) is { } answer)
        {
            Parties?.AnswerPartyAsync(from, answer);
            return true;
        }

        if (ShowsPlayerButtons && HudPlayerButtons.At(HudCombat.TargetFrame(layout), pointer, ClanButton) is { } button)
        {
            if (button == HudPlayerButton.Clan)
            {
                InviteToClan(ButtonsPlayerId);
            }
            else if (button == HudPlayerButton.Party)
            {
                Parties?.InviteToPartyAsync(ButtonsPlayerId);
            }
            else if (button == HudPlayerButton.Trade)
            {
                Trades?.RequestTradeAsync(ButtonsPlayerId);
            }
            else
            {
                BefriendReached();
            }

            PlayerButtonsUsed?.Invoke();
            return true;
        }

        return _partyWindow.Click(layout, pointer, Party, SelfId, _clock.Elapsed.TotalSeconds);
    }

    private bool CapturesParty(HudLayout layout, Point pointer) =>
        _partyRequest.Captures(pointer) || _partyWindow.Contains(layout, pointer)
        || ShowsPlayerButtons && HudPlayerButtons.At(HudCombat.TargetFrame(layout), pointer, ClanButton) is not null;

    // The other members' last known places, for the minimap.
    private List<Vector3> MinimapParty()
    {
        _minimapParty.Clear();
        var members = Party.Members;
        for (var index = 0; index < members.Count; index++)
        {
            if (members[index].EntityId != SelfId)
            {
                _minimapParty.Add(new Vector3(members[index].Position.X, 0f, members[index].Position.Y));
            }
        }

        return _minimapParty;
    }
}
