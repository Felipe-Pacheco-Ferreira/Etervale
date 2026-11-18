using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Parties;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Contacts;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Tests;

// The party in the HUD: the party page of the party window, the request box and the buttons under a player's portrait.
internal static class PartyHudTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the party page lists members with levels in tens, gives the leader its buttons, picks a member and whispers on a double click", WindowAsync);
        yield return ("the party request box answers once from its buttons and closes itself after twenty seconds, the trade's after ten", RequestAsync);
        yield return ("the trade, party and friend buttons sit under the portrait from 36 px left of its centre", PlayerButtonsAsync);
        yield return ("browser Social keeps five tabs and contact controls inside resized panels while desktop retains its native window", BrowserSocialLayoutAsync);
        yield return ("browser Social name fields add and block contacts, release typing and dispatch selected contact actions", BrowserSocialContactsAsync);
        yield return ("browser Social recent contacts offer whisper, friend, group, profile, block and removal without a hidden name field", BrowserSocialRecentAsync);
        yield return ("browser Social creates groups, changes openness and dispatches finder requests with full groups disabled", BrowserSocialGroupsAsync);
    }

    private static readonly PartyInfo Party = new(
    [
        new PartyMemberInfo(1, "Ann", 45, 100, 200, System.Numerics.Vector2.Zero),
        new PartyMemberInfo(2, "Bob", 9, 50, 50, System.Numerics.Vector2.Zero)
    ]);

    private static Task WindowAsync()
    {
        Assert.True(HudPartyWindow.LevelText(45) == "L4x" && HudPartyWindow.LevelText(10) == "L10" && HudPartyWindow.LevelText(9) == "L9",
            "levels past 10 show in tens");
        Assert.True(Enumerable.Range(0, 5).All(button => HudPartyWindow.Enabled(button, Party, 1)), "the leader has every button");
        Assert.True(!HudPartyWindow.Enabled(0, Party, 2) && !HudPartyWindow.Enabled(2, Party, 2) && HudPartyWindow.Enabled(3, Party, 2)
            && HudPartyWindow.Enabled(HudPartyWindow.CloseButton, Party, 2), "a member may only leave and close");
        Assert.True(!HudPartyWindow.Enabled(3, PartyInfo.None, 1) && HudPartyWindow.Enabled(HudPartyWindow.CloseButton, PartyInfo.None, 1),
            "without a party only close works");

        var layout = HudLayout.Create(800, 600);
        var window = new HudPartyWindow();
        var commands = new List<(PartyCommand, ulong)>();
        var whispers = new List<string>();
        window.Commanded = (command, member) => commands.Add((command, member));
        window.Whispered = whispers.Add;
        window.Show();
        window.Advance(1);
        var origin = window.CurrentOrigin(layout);
        Assert.True(origin == new Point(539, 155), $"the window stands at {origin}");

        var bob = HudPartyWindow.Row(origin, 1).Center;
        Assert.True(window.Click(layout, bob, Party, 1, 10) && window.Picked == 2, "a click picks the member");
        Assert.True(window.Click(layout, HudSidePanel.Button(origin, (int)PartyCommand.Kick).Center, Party, 1, 11)
            && commands is [(PartyCommand.Kick, 2)] && window.Picked == 0, "kick acts on the picked member and clears the pick");
        window.Click(layout, bob, Party, 1, 20);
        window.Click(layout, bob, Party, 1, 20.2);
        Assert.True(whispers is ["Bob"], "a double click whispers to the member");
        window.Click(layout, HudPartyWindow.Row(origin, 0).Center, Party, 1, 30);
        window.Click(layout, HudPartyWindow.Row(origin, 0).Center, Party, 1, 30.1);
        Assert.True(whispers.Count == 1, "the player whispered to itself");

        window.Click(layout, HudSidePanel.Button(origin, (int)PartyCommand.Delegate).Center, Party, 2, 40);
        Assert.True(commands.Count == 1, "a member used a leader's button");
        window.Click(layout, bob, Party, 1, 45);
        window.Refresh(new PartyInfo([Party.Members[0]]));
        Assert.True(window.Picked == 0, "a member who left stayed picked");
        Assert.True(window.Click(layout, HudSidePanel.Button(origin, HudPartyWindow.CloseButton).Center, Party, 1, 50) && !window.Open,
            "close did not close");
        return Task.CompletedTask;
    }

    private static Task RequestAsync()
    {
        var box = new HudRequestBox(HudPartyArt.Request, PartyRules.InviteMs);
        box.Show(7, "Ann", ClientText.Format(PartyMessages.Request), 1000);
        Assert.True(box.IsOpen && box.Click(new Point(0, 300)) is null && box.IsOpen, "a click elsewhere answered");
        Assert.True(box.Click(box.Ok.Center) == true && !box.IsOpen, "OK accepts and closes");
        box.Show(7, "Ann", ClientText.Format(PartyMessages.Request), 1000);
        Assert.True(box.Click(box.Cancel.Center) == false && !box.IsOpen && box.FromId == 7, "cancel refuses and closes");
        box.Show(7, "Ann", ClientText.Format(PartyMessages.Request), 1000);
        box.Update(1000 + PartyRules.InviteMs - 1);
        Assert.True(box.IsOpen, "the box closed early");
        box.Update(1000 + PartyRules.InviteMs);
        Assert.True(!box.IsOpen && box.Ok.Location == new Point(653, 14), "the box outlived its request or moved");

        var trade = new HudRequestBox(PristonTale.Gameplay.UI.Trades.HudTradeArt.Request, PristonTale.Contracts.Social.TradeLimits.RequestMs);
        trade.Show(9, "Bob", null, 0);
        trade.Update(PristonTale.Contracts.Social.TradeLimits.RequestMs);
        Assert.True(!trade.IsOpen && trade.Cancel.Location == new Point(657, 40), "the trade box keeps the original's ten seconds and place");
        return Task.CompletedTask;
    }

    private static Task PlayerButtonsAsync()
    {
        var frame = HudCombat.TargetFrame(HudLayout.Create(800, 600));
        var center = frame.Box.X + frame.Box.Width / 2;
        var party = HudPlayerButtons.Button(frame, HudPlayerButton.Party);
        Assert.True(HudPlayerButtons.Button(frame, HudPlayerButton.Trade) == new Rectangle(center - 36, frame.Box.Bottom, 20, 20)
            && party.X == center - 11 && HudPlayerButtons.Menu(frame) == new Rectangle(center - 50, frame.Box.Bottom - 6, 100, 50), "button places");
        Assert.True(HudPlayerButtons.At(frame, party.Center) == HudPlayerButton.Party && HudPlayerButtons.At(frame, new Point(center, frame.Box.Y)) is null,
            "the pointer finds the party button only over it");
        return Task.CompletedTask;
    }

    private static Task BrowserSocialLayoutAsync()
    {
        var scenario = new BrowserSocialScenario();
        Assert.True(scenario.Box == new Rectangle(448, 96, 384, 528), "browser Social lost its centered reference dimensions");
        foreach (var size in new[] { new Point(320, 480), new Point(800, 600), new Point(1280, 720), new Point(2560, 1440) })
        {
            var layout = HudLayout.Create(size.X, size.Y) with { Browser = true };
            var box = HudPartyWindow.BrowserBounds(layout);
            Assert.True(new Rectangle(0, 0, layout.Width, layout.Height).Contains(box), "Social escaped the logical viewport");
            for (var tab = 0; tab < 5; tab++)
            {
                scenario.Tab(tab);
                Assert.True(box.Contains(HudPartyWindow.BrowserTab(box, tab)), "a Social tab escaped its panel");
                var actions = tab == 0 ? 3 : tab == 3 ? 1 : tab == 4 ? 6 : 4;
                for (var action = 0; action < actions; action++)
                {
                    var bounds = scenario.Window.BrowserAction(box, action);
                    Assert.True(box.Contains(bounds), "a Social action escaped its resized panel");
                    for (var previous = 0; previous < action; previous++)
                    {
                        Assert.True(!bounds.Intersects(scenario.Window.BrowserAction(box, previous)), "Social action hit targets overlap");
                    }
                }
            }
        }

        var desktop = HudLayout.Create(800, 600);
        var legacy = new HudPartyWindow();
        legacy.Show();
        legacy.Advance(1);
        var origin = legacy.CurrentOrigin(desktop);
        Assert.True(origin == new Point(539, 155) && !legacy.Contains(desktop, HudPartyWindow.BrowserBounds(desktop).Center),
            "the desktop window used the browser overlay bounds");
        legacy.Click(desktop, HudPartyWindow.FriendsTab(origin).Center, PartyInfo.None, 1, 1);
        Assert.True(legacy.Page == HudPartyPage.Friends, "desktop friends tab stopped responding");
        legacy.Click(desktop, HudFriendsPage.Button(origin, 6).Center, PartyInfo.None, 1, 2);
        Assert.True(!legacy.Open, "desktop close button stopped responding");
        return Task.CompletedTask;
    }

    private static Task BrowserSocialContactsAsync()
    {
        var scenario = new BrowserSocialScenario();
        scenario.Click(HudPartyWindow.BrowserName(scenario.Box).Center);
        Assert.True(scenario.Window.KeyboardCaptured, "the name field did not capture typing");
        scenario.Window.Type("Alicee", false, false);
        scenario.Window.Type([], true, true);
        Assert.True(scenario.Changes is [(ContactList.Friends, true, "Alice")] && !scenario.Window.KeyboardCaptured,
            "friend submission lost editing, the selected list or keyboard release");
        scenario.Window.Friends.Apply(new ContactListInfo(ContactList.Friends, [("Alice", true)]));
        scenario.Pick(0);
        scenario.Action(0);
        scenario.Action(1);
        scenario.Action(2);
        Assert.True(scenario.Whispers is ["Alice"] && scenario.Invites is ["Alice"] && scenario.Profiles is ["Alice"],
            "selected friend actions did not use the picked name");
        scenario.BackFromProfile();
        scenario.Action(3);
        Assert.True(scenario.Changes[^1] == (ContactList.Friends, false, "Alice"), "remove acted on the wrong contact list");

        scenario.Tab(3);
        scenario.TypeName("Blocked");
        Assert.True(scenario.Changes[^1] == (ContactList.Ignored, true, "Blocked"), "block did not use the ignored list");
        scenario.Window.Friends.Apply(new ContactListInfo(ContactList.Ignored, [("Blocked", true)]));
        scenario.Pick(0);
        scenario.Action(0);
        Assert.True(scenario.Changes[^1] == (ContactList.Ignored, false, "Blocked"), "unblock did not remove the selected blocked name");
        scenario.Click(HudPartyWindow.BrowserName(scenario.Box).Center);
        scenario.Window.CancelTyping();
        Assert.True(!scenario.Window.KeyboardCaptured, "cancel left the name field capturing gameplay keys");

        scenario.Window.ObserveClan(new ClanInfo(1, "Avalon", ClanState.Registering, 0, ClanRank.Chief, 20, "", 0,
            [new ClanMemberInfo("Clanmate", PlayerClass.Fighter, 50, ClanRank.Member, true)]));
        var talked = false;
        scenario.Window.Clan.Talk = () => talked = true;
        scenario.Tab(2);
        scenario.Action(3);
        scenario.Pick(0);
        scenario.Action(0);
        Assert.True(talked && scenario.Whispers[^1] == "Clanmate", "browser clan actions lost chat or member selection");
        return Task.CompletedTask;
    }

    private static Task BrowserSocialRecentAsync()
    {
        var scenario = new BrowserSocialScenario();
        scenario.Window.Friends.Met("Recent");
        scenario.Tab(4);
        scenario.Pick(0);
        scenario.Action(0);
        scenario.Action(1);
        scenario.Action(2);
        scenario.Action(3);
        Assert.True(scenario.Whispers is ["Recent"] && scenario.Invites is ["Recent"] && scenario.Profiles is ["Recent"] &&
            scenario.Changes is [(ContactList.Friends, true, "Recent")], "the first four recent actions were not dispatched");
        scenario.BackFromProfile();
        scenario.Action(4);
        Assert.True(scenario.Changes[^1] == (ContactList.Ignored, true, "Recent") && !scenario.Window.KeyboardCaptured,
            "block recent clicked a hidden textbox or changed the wrong list");
        scenario.Action(5);
        Assert.True(scenario.Window.Friends.Recent.Count == 0, "remove kept the selected recent contact");
        scenario.Window.Close();
        Assert.True(!scenario.Window.KeyboardCaptured, "closing Social retained keyboard capture");
        return Task.CompletedTask;
    }

    private static Task BrowserSocialGroupsAsync()
    {
        var scenario = new BrowserSocialScenario();
        scenario.Tab(0);
        var creates = 0;
        scenario.Window.CreatePartyRequested = () => creates++;
        scenario.Window.CreatingParty = true;
        scenario.Click(HudPartyWindow.BrowserCreateGroup(scenario.Box).Center);
        Assert.True(creates == 0, "a pending create accepted a duplicate click");
        scenario.Window.CreatingParty = false;
        scenario.Click(HudPartyWindow.BrowserOpenGroup(scenario.Box).Center);
        Assert.True(!scenario.Window.PartyOpen, "pre-create openness could not be changed");
        scenario.Click(HudPartyWindow.BrowserCreateGroup(scenario.Box).Center);
        Assert.True(creates == 1, "create did not dispatch");
        scenario.TypeName("Invitee");
        Assert.True(scenario.Invites is ["Invitee"], "the group field did not invite by name");

        var searches = 0;
        var joined = new List<ulong>();
        scenario.Window.SearchPartiesRequested = () => searches++;
        scenario.Window.JoinPartyRequested = joined.Add;
        scenario.Action(1);
        scenario.Window.ObserveDirectory([
            new PartyDirectoryEntry(10, "Open", 3, 50, 2, true),
            new PartyDirectoryEntry(20, "Closed", 3, 50, 2, false),
            new PartyDirectoryEntry(30, "Full", 3, 50, PartyLimits.MaxMembers, true)
        ]);
        scenario.PickDirectory(0);
        scenario.Click(HudPartyWindow.DirectoryJoin(scenario.Box).Center);
        scenario.Action(1);
        scenario.PickDirectory(1);
        scenario.Click(HudPartyWindow.DirectoryJoin(scenario.Box).Center);
        scenario.Action(1);
        scenario.PickDirectory(2);
        scenario.Click(HudPartyWindow.DirectoryJoin(scenario.Box).Center);
        Assert.True(joined is [10, 20], "finder lost the selected leader, refused a closed-group request or joined a full group");
        scenario.Click(HudPartyWindow.DirectoryRefresh(scenario.Box).Center);
        Assert.True(searches == 4, "finder refresh did not request current directory data");
        scenario.Click(HudPartyWindow.DirectoryBack(scenario.Box).Center);

        scenario.Party = Party;
        scenario.Pick(1);
        scenario.Click(HudPartyWindow.BrowserPartyCommand(scenario.Box, (int)PartyCommand.Kick).Center);
        Assert.True(scenario.Commands is [(PartyCommand.Kick, 2)], "browser group management lost the selected member");
        bool? open = null;
        scenario.Window.PartyOpenChanged = value => open = value;
        scenario.Click(HudPartyWindow.BrowserPartyOpen(scenario.Box).Center);
        Assert.True(open == scenario.Window.PartyOpen, "the current group's openness was not dispatched");
        return Task.CompletedTask;
    }

    private sealed class BrowserSocialScenario
    {
        private readonly HudLayout _layout = HudLayout.Create(1280, 720) with { Browser = true };
        private double _now;

        public HudPartyWindow Window { get; } = new();
        public Rectangle Box => HudPartyWindow.BrowserBounds(_layout);
        public PartyInfo Party { get; set; } = PartyInfo.None;
        public List<(ContactList, bool, string)> Changes { get; } = [];
        public List<(PartyCommand, ulong)> Commands { get; } = [];
        public List<string> Whispers { get; } = [];
        public List<string> Invites { get; } = [];
        public List<string> Profiles { get; } = [];

        public BrowserSocialScenario()
        {
            Window.Friends.Changed = (list, add, name) => Changes.Add((list, add, name));
            Window.Commanded = (command, member) => Commands.Add((command, member));
            Window.Whispered = Whispers.Add;
            Window.InviteNamed = Invites.Add;
            Window.ProfileOf = name =>
            {
                Profiles.Add(name);
                return new HudSocialProfile(name, true);
            };
            Window.ShowFriends(HudFriendsView.Friends);
            Window.Advance(1);
        }

        public void Click(Point point)
        {
            _now++;
            Assert.True(Window.Click(_layout, point, Party, 1, _now), "the visible Social control did not take its click");
        }

        public void Tab(int index) => Click(HudPartyWindow.BrowserTab(Box, index).Center);
        public void Action(int index) => Click(Window.BrowserAction(Box, index).Center);
        public void Pick(int index) => Click(new Point(Box.X + 40, Box.Y + 116 + index * 35 + (Window.Page == HudPartyPage.Party ? 28 : 0)));
        public void PickDirectory(int index) => Click(new Point(Box.X + 40, Box.Y + 120 + index * 52));
        public void BackFromProfile() => Click(new Point(Box.Center.X, Box.Bottom - 40));

        public void TypeName(string name)
        {
            Click(HudPartyWindow.BrowserName(Box).Center);
            Window.Type(name, false, true);
        }
    }
}
