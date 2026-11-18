using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Contacts;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.UI;

// The friends page of the party window: its lists from the server, the recent players (whispers and trades), the friend
// button under a reached player's portrait, and the friends' presence asked again every ContactRefreshMs while it shows.
public sealed partial class GameplayHud
{
    private const double ContactRefreshMs = 10_000;
    private double _contactsAskedMs = double.MinValue;
    private readonly Dictionary<ulong, PlayerAppearanceInfo> _socialPlayers = new();

    // Sends the contact list requests; set by the screen.
    public IContactClient Contacts { get; set; }

    public void ObserveContacts(PresenceUpdate update)
    {
        foreach (var spawn in update.Spawns)
        {
            if (spawn.Player is { } player)
            {
                _socialPlayers[spawn.EntityId] = player;
            }
        }

        foreach (var removed in update.Removed)
        {
            _socialPlayers.Remove(removed);
        }

        if (update.Contacts is { } lists)
        {
            _partyWindow.Friends.Apply(lists);
            _partyWindow.RefreshSocialContacts();
        }
    }

    private void SetUpContacts()
    {
        _sprites.Request(HudContactArt.All);
        var friends = _partyWindow.Friends;
        friends.Changed = (list, add, name) => Contacts?.ChangeContactAsync(list, add, name);
        friends.Whispered = _chat.Window.WhisperTo;
        _partyWindow.InviteNamed = name => _chat.Send?.Invoke("//party " + name, false);
        _partyWindow.ProfileOf = SocialProfileOf;
        _chat.WhisperPartner = friends.Met;
    }

    private HudSocialProfile SocialProfileOf(string name)
    {
        var online = _partyWindow.Friends.OnlineOf(name);
        var level = (int?)null;
        var className = "";
        var clanName = "";
        var partyMember = Party.Members.FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
        if (partyMember is not null)
        {
            online = true;
            level = partyMember.Level;
        }

        var clanMember = Clan.Members.FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
        if (clanMember is not null)
        {
            online = clanMember.Online;
            level = clanMember.Level;
            className = DataText.ClassTitle(clanMember.Class, 0);
            clanName = Clan.Name;
        }

        var nearby = _socialPlayers.Values.FirstOrDefault(player => string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase));
        if (nearby is not null)
        {
            online = true;
            level = nearby.Level;
            className = DataText.ClassTitle(nearby.Class, nearby.Tier);
            clanName = nearby.ClanName;
        }

        return new HudSocialProfile(name, online, className, level, clanName);
    }

    private void UpdateContacts(double nowMs)
    {
        if (_partyWindow.Visible && _partyWindow.Page == HudPartyPage.Friends && nowMs - _contactsAskedMs >= ContactRefreshMs)
        {
            _contactsAskedMs = nowMs;
            Contacts?.RefreshContactsAsync();
        }
    }

    // The friend button: the player goes on the friends list and the friends page opens on it, as the original's does.
    private void BefriendReached()
    {
        if (Combat.PlayerName(ButtonsPlayerId) is not { } name)
        {
            return;
        }

        Contacts?.ChangeContactAsync(ContactList.Friends, true, name);
        _partyWindow.Friends.Met(name);
        _questWindow.Close();
        _contactsAskedMs = double.MinValue;
        _partyWindow.ShowFriends(HudFriendsView.Friends);
    }
}
