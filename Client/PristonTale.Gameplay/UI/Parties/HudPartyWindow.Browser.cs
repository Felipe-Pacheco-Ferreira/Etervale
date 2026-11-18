using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Contacts;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Gameplay.UI.Parties;

public sealed record HudSocialProfile(string Name, bool? Online = null, string ClassName = "", int? Level = null, string ClanName = "");

public sealed partial class HudPartyWindow
{
    private const float SocialTextScale = 11f / HudPainter.BakePixels;
    private static readonly Color SocialGold = new(205, 181, 126);
    private static readonly Color SocialMuted = new(152, 141, 113);
    private static readonly Color SocialBorder = Color.FromNonPremultiplied(134, 111, 62, 170);
    private static readonly string[] SocialPortugueseTabs = ["Grupo", "Amigos", "Clã", "Bloqueados", "Recentes"];
    private static readonly string[] SocialEnglishTabs = ["Party", "Friends", "Clan", "Blocked", "Recent"];
    private static readonly string[][] SocialPortugueseActions =
    [
        ["Sussurrar", "Procurar", "Perfil"],
        ["Sussurrar", "Grupo", "Perfil", "Remover"],
        ["Sussurrar", "Grupo", "Perfil", "Chat clã"],
        ["Desbloquear"],
        ["Sussurrar", "Amigo", "Grupo", "Perfil", "Bloquear", "Remover"]
    ];
    private static readonly string[][] SocialEnglishActions =
    [
        ["Whisper", "Find", "Profile"],
        ["Whisper", "Party", "Profile", "Remove"],
        ["Whisper", "Party", "Profile", "Clan chat"],
        ["Unblock"],
        ["Whisper", "Friend", "Party", "Profile", "Block", "Remove"]
    ];
    private static readonly int[][] SocialActionWidths = [[76, 70, 52], [76, 62, 51, 74], [76, 62, 51, 74], [90], [76, 58, 57, 51, 80, 74]];
    private readonly StringBuilder _socialName = new();
    private string _socialPicked = "";
    private string _socialLastClick = "";
    private double _socialLastClickAt;
    private int _socialFirst;
    private HudSocialProfile _socialProfile;
    private string _socialMessage = "";
    private bool _socialTyping;
    private bool _socialFinding;
    private bool _directoryLoading;
    private ulong _directoryLeader;
    private int _directoryFirst;

    public Action<string> InviteNamed { get; set; }
    public Action SearchPartiesRequested { get; set; }
    public Action CreatePartyRequested { get; set; }
    public Action<bool> PartyOpenChanged { get; set; }
    public bool PartyOpen { get; set; } = true;
    public bool CreatingParty { get; set; }
    public Action<ulong> JoinPartyRequested { get; set; }
    public IReadOnlyList<PartyDirectoryEntry> Directory { get; private set; } = [];
    public Func<int, string> FieldName { get; set; } = code => (ClientText.Language == "pt-BR" ? "Mapa " : "Map ") + code;
    public Func<string, HudSocialProfile> ProfileOf { get; set; }
    public bool KeyboardCaptured => Open && _socialTyping;

    public static Rectangle BrowserBounds(HudLayout layout) =>
        layout.PlaceOverlay(new Point((layout.Width - 384) / 2, (layout.Height - 528) / 2), 384, 528);

    private static Rectangle BrowserClose(Rectangle box) => new(box.Right - 36, box.Top + 7, 24, 24);
    internal static Rectangle BrowserTab(Rectangle box, int index)
    {
        var width = (box.Width - 28) / 5;
        return new Rectangle(box.X + 14 + index * width, box.Y + 46, width - 5, 42);
    }

    private static Rectangle BrowserFooter(Rectangle box) => new(box.X + 12, box.Bottom - 124, box.Width - 24, 112);
    internal static Rectangle BrowserName(Rectangle box) => new(box.X + 24, box.Bottom - 50, Math.Max(40, box.Width - 142), 29);
    internal static Rectangle BrowserSubmit(Rectangle box) => new(box.Right - 112, box.Bottom - 50, 88, 29);
    internal Rectangle BrowserAction(Rectangle box, int index)
    {
        var footer = BrowserFooter(box);
        var widths = SocialActionWidths[BrowserTabIndex];
        var first = index >= 4 ? 4 : 0;
        var scale = Math.Min(1f, Math.Max(0.1f, (box.Width - 48) / 284f));
        var x = footer.X + 12;
        for (var previous = first; previous < index; previous++)
        {
            x += (int)((widths[previous] + 7) * scale);
        }

        return new Rectangle(x, footer.Y + (index >= 4 ? 74 : 40), (int)(widths[index] * scale), 28);
    }

    private static Rectangle BrowserList(Rectangle box) => new(box.X + 22, box.Y + 104, box.Width - 44, Math.Max(28, box.Height - 242));
    private static Rectangle BrowserGroupCard(Rectangle box) => new(box.X + 14, Math.Min(box.Y + 254, BrowserFooter(box).Top - 48), box.Width - 28, 41);
    internal static Rectangle BrowserCreateGroup(Rectangle box)
    {
        var card = BrowserGroupCard(box);
        return new Rectangle(card.X + 12, card.Y + 6, 108, 29);
    }

    internal static Rectangle BrowserOpenGroup(Rectangle box)
    {
        var card = BrowserGroupCard(box);
        return new Rectangle(card.Right - 33, card.Y + 10, 23, 21);
    }

    internal static Rectangle BrowserPartyCommand(Rectangle box, int action)
    {
        var width = (box.Width - 69) / 4;
        return new Rectangle(box.X + 24 + action * (width + 7), BrowserFooter(box).Y - 37, width, 27);
    }

    internal static Rectangle BrowserPartyOpen(Rectangle box) => new(box.Right - 51, box.Y + 101, 23, 21);
    private Rectangle BrowserRow(Rectangle box, int index)
    {
        var list = BrowserList(box);
        return new Rectangle(list.X, list.Y + index * 35 + (Page == HudPartyPage.Party ? 28 : 0), list.Width, 33);
    }

    private int BrowserTabIndex => Page switch
    {
        HudPartyPage.Party => 0,
        HudPartyPage.Clan => 2,
        _ => Friends.View == HudFriendsView.Ignored ? 3 : Friends.View == HudFriendsView.Recent ? 4 : 1
    };

    public void CancelTyping() => _socialTyping = false;

    internal void RefreshSocialContacts()
    {
        if (Page != HudPartyPage.Friends)
        {
            return;
        }

        for (var index = 0; index < Friends.Count; index++)
        {
            if (string.Equals(Friends.NameAt(index), _socialPicked, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        _socialPicked = "";
    }

    private void ResetSocialSelection()
    {
        _socialPicked = "";
        _socialProfile = null;
        _socialMessage = "";
        _socialFirst = 0;
        _socialTyping = false;
        _socialFinding = false;
    }

    public void Type(ReadOnlySpan<char> typed, bool backspace, bool enter)
    {
        if (!KeyboardCaptured)
        {
            return;
        }

        foreach (var character in typed)
        {
            if (character == '\b')
            {
                if (_socialName.Length > 0)
                {
                    _socialName.Length--;
                }
            }
            else if (!char.IsControl(character) && !char.IsWhiteSpace(character) && _socialName.Length < 31)
            {
                _socialName.Append(character);
            }
        }

        if (backspace && !typed.Contains('\b') && _socialName.Length > 0)
        {
            _socialName.Length--;
        }

        if (!typed.IsEmpty || backspace)
        {
            _socialNameText = _socialName.ToString();
        }

        if (enter)
        {
            SubmitSocialName();
        }
    }

    public bool Scroll(HudLayout layout, Point pointer, int wheelDelta)
    {
        if (!layout.Browser || !Visible || wheelDelta == 0 || !BrowserBounds(layout).Contains(pointer))
        {
            return false;
        }

        if (_socialFinding)
        {
            var visible = Math.Max(1, (BrowserBounds(layout).Height - 192) / 52);
            _directoryFirst = Math.Clamp(_directoryFirst - Math.Sign(wheelDelta), 0, Math.Max(0, Directory.Count - visible));
            return true;
        }

        var count = Page == HudPartyPage.Clan ? Clan.Info.Members.Count : Page == HudPartyPage.Friends ? Friends.Count : 0;
        var rows = Math.Max(1, BrowserList(BrowserBounds(layout)).Height / 35);
        _socialFirst = Math.Clamp(_socialFirst - Math.Sign(wheelDelta), 0, Math.Max(0, count - rows));
        return true;
    }

    private void SelectSocialTab(int index)
    {
        Page = index == 0 ? HudPartyPage.Party : index == 2 ? HudPartyPage.Clan : HudPartyPage.Friends;
        if (Page == HudPartyPage.Friends)
        {
            Friends.Show(index == 3 ? HudFriendsView.Ignored : index == 4 ? HudFriendsView.Recent : HudFriendsView.Friends);
        }

        _socialFirst = 0;
        _socialPicked = "";
        _socialProfile = null;
        _socialMessage = "";
        _socialTyping = false;
        _socialFinding = false;
        Picked = 0;
    }

    private bool ClickBrowser(HudLayout layout, Point pointer, PartyInfo party, ulong self, double nowSeconds)
    {
        var box = BrowserBounds(layout);
        if (BrowserClose(box).Contains(pointer))
        {
            Close();
            return true;
        }

        if (_socialProfile is not null)
        {
            if (ProfileBack(box).Contains(pointer))
            {
                _socialProfile = null;
            }

            return true;
        }

        if (_socialFinding)
        {
            ClickPartyDirectory(box, pointer);
            return true;
        }

        for (var tab = 0; tab < 5; tab++)
        {
            if (BrowserTab(box, tab).Contains(pointer))
            {
                SelectSocialTab(tab);
                return true;
            }
        }

        _socialTyping = BrowserName(box).Contains(pointer) && Page != HudPartyPage.Clan && BrowserTabIndex != 4;
        if (_socialTyping)
        {
            return true;
        }

        if (BrowserSubmit(box).Contains(pointer) && Page != HudPartyPage.Clan && BrowserTabIndex != 4)
        {
            SubmitSocialName();
            return true;
        }

        for (var action = 0; action < SocialActionWidths[BrowserTabIndex].Length; action++)
        {
            if (BrowserAction(box, action).Contains(pointer))
            {
                RunSocialAction(action, party, self);
                return true;
            }
        }

        if (Page == HudPartyPage.Party)
        {
            if (party.Members.Count == 0)
            {
                if (BrowserCreateGroup(box).Contains(pointer))
                {
                    if (!CreatingParty)
                    {
                        CreatePartyRequested?.Invoke();
                    }

                    return true;
                }

                if (BrowserOpenGroup(box).Contains(pointer))
                {
                    if (!CreatingParty)
                    {
                        PartyOpen = !PartyOpen;
                    }

                    return true;
                }
            }
            else
            {
                if (party.Leads(self) && BrowserPartyOpen(box).Contains(pointer))
                {
                    PartyOpen = !PartyOpen;
                    PartyOpenChanged?.Invoke(PartyOpen);
                    return true;
                }

                for (var command = 0; command < 4; command++)
                {
                    if (BrowserPartyCommand(box, command).Contains(pointer))
                    {
                        if ((command == 3 || party.Leads(self)) && (command is 1 or 3 || Picked != 0 && Picked != self))
                        {
                            Commanded?.Invoke((PartyCommand)command, Picked);
                        }

                        return true;
                    }
                }
            }
        }

        var count = SocialCount(party);
        var rows = Math.Max(1, BrowserList(box).Height / 35);
        _socialFirst = Math.Clamp(_socialFirst, 0, Math.Max(0, count - rows));
        for (var row = 0; row < rows && _socialFirst + row < count; row++)
        {
            if (!BrowserRow(box, row).Contains(pointer))
            {
                continue;
            }

            var index = _socialFirst + row;
            _socialPicked = SocialName(party, index);
            Picked = Page == HudPartyPage.Party ? party.Members[index].EntityId : 0;
            if (_socialPicked == _socialLastClick && nowSeconds - _socialLastClickAt < DoubleClickSeconds && BrowserTabIndex != 3)
            {
                Whispered?.Invoke(_socialPicked);
                _socialLastClick = "";
            }
            else
            {
                _socialLastClick = _socialPicked;
                _socialLastClickAt = nowSeconds;
            }

            return true;
        }

        return true;
    }

    private void SubmitSocialName()
    {
        var name = _socialName.ToString().Trim();
        if (name.Length == 0)
        {
            _socialTyping = true;
            return;
        }

        if (Page == HudPartyPage.Party)
        {
            InviteNamed?.Invoke(name);
        }
        else
        {
            Friends.Changed?.Invoke(Friends.View == HudFriendsView.Ignored ? ContactList.Ignored : ContactList.Friends, true, name);
        }

        _socialName.Clear();
        _socialNameText = "";
        _socialTyping = false;
        _socialMessage = ClientText.Language == "pt-BR" ? "Solicitação enviada." : "Request sent.";
    }

    private void RunSocialAction(int action, PartyInfo party, ulong self)
    {
        if (Page == HudPartyPage.Party)
        {
            if (action == 1)
            {
                SearchPartyDirectory();
            }
            else if (_socialPicked.Length > 0 && action == 0)
            {
                Whispered?.Invoke(_socialPicked);
            }
            else if (_socialPicked.Length > 0 && action == 2)
            {
                OpenSocialProfile();
            }

            return;
        }

        if (BrowserTabIndex == 3)
        {
            if (_socialPicked.Length > 0)
            {
                Friends.Changed?.Invoke(ContactList.Ignored, false, _socialPicked);
                _socialPicked = "";
            }

            return;
        }

        if (Page == HudPartyPage.Clan && action == 3 && Clan.HasClan)
        {
            Clan.Talk?.Invoke();
            return;
        }

        if (string.IsNullOrEmpty(_socialPicked))
        {
            return;
        }

        if (BrowserTabIndex == 4)
        {
            switch (action)
            {
                case 0:
                    Whispered?.Invoke(_socialPicked);
                    break;
                case 1:
                    Friends.Changed?.Invoke(ContactList.Friends, true, _socialPicked);
                    break;
                case 2:
                    InviteNamed?.Invoke(_socialPicked);
                    break;
                case 3:
                    OpenSocialProfile();
                    break;
                case 4:
                    Friends.Changed?.Invoke(ContactList.Ignored, true, _socialPicked);
                    break;
                case 5:
                    Friends.RemoveRecent(_socialPicked);
                    _socialPicked = "";
                    break;
            }

            return;
        }

        switch (action)
        {
            case 0 when BrowserTabIndex != 3:
                Whispered?.Invoke(_socialPicked);
                break;
            case 1 when BrowserTabIndex != 3:
                InviteNamed?.Invoke(_socialPicked);
                break;
            case 2:
                OpenSocialProfile();
                break;
            case 3 when Page == HudPartyPage.Friends:
                Friends.Changed?.Invoke(Friends.View == HudFriendsView.Ignored ? ContactList.Ignored : ContactList.Friends, false, _socialPicked);
                _socialPicked = "";
                break;
        }
    }

    private void OpenSocialProfile() =>
        _socialProfile = ProfileOf?.Invoke(_socialPicked) ?? new HudSocialProfile(_socialPicked, Friends.OnlineOf(_socialPicked));

    public void ObserveDirectory(IReadOnlyList<PartyDirectoryEntry> entries)
    {
        Directory = entries;
        _directoryLoading = false;
        _directoryFirst = 0;
        if (SelectedDirectoryEntry() is null)
        {
            _directoryLeader = 0;
        }
    }

    private void SearchPartyDirectory()
    {
        _socialFinding = true;
        _directoryLoading = true;
        _socialTyping = false;
        SearchPartiesRequested?.Invoke();
    }

    private int SocialCount(PartyInfo party) => Page switch
    {
        HudPartyPage.Party => party.Members.Count,
        HudPartyPage.Clan => Clan.Info.Members.Count,
        _ => Friends.Count
    };

    private string SocialName(PartyInfo party, int index) => Page switch
    {
        HudPartyPage.Party => party.Members[index].Name,
        HudPartyPage.Clan => Clan.Info.Members[index].Name,
        _ => Friends.NameAt(index)
    };

    private void DrawBrowser(HudPainter painter, HudLayout layout, PartyInfo party, ulong self, Point pointer)
    {
        painter.Flush();
        RefreshSocialTextLanguage();
        var box = BrowserBounds(layout);
        painter.FramedBox(box, SocialBorder, Color.FromNonPremultiplied(31, 25, 13, 247),
            Color.FromNonPremultiplied(9, 8, 5, 239), 4);
        painter.ColorBox(new Rectangle(box.X + 1, box.Y + 36, box.Width - 2, 1), 103, 82, 40, 145);
        painter.Diamond(box.X + 16, box.Y + 19, 6, new Color(215, 187, 111));
        painter.Text("Social", box.X + 29, box.Y + 11, SocialGold, 15f / HudPainter.BakePixels);
        var close = BrowserClose(box);
        painter.Disc(close.Center.X, close.Center.Y, 11, SocialBorder);
        painter.Disc(close.Center.X, close.Center.Y, 10, new Color(24, 21, 13));
        SocialText(painter, "×", close.Center.X - 3, close.Y + 3, SocialGold);

        if (_socialProfile is not null)
        {
            DrawSocialProfile(painter, box, pointer);
            return;
        }

        if (_socialFinding)
        {
            DrawPartyDirectory(painter, box, pointer);
            return;
        }

        var portuguese = ClientText.Language == "pt-BR";
        var labels = portuguese ? SocialPortugueseTabs : SocialEnglishTabs;
        for (var index = 0; index < 5; index++)
        {
            var tab = BrowserTab(box, index);
            var selected = BrowserTabIndex == index;
            painter.FramedBox(tab, selected ? new Color(125, 157, 112) : SocialBorder,
                selected ? Color.FromNonPremultiplied(48, 67, 40, 213) : Color.FromNonPremultiplied(27, 23, 14, 190), 3);
            DrawSocialIcon(painter, index, tab.Center.X, tab.Y + 15, selected);
            var label = FitSocial(painter, labels[index], tab.Width - 3, 10f / HudPainter.BakePixels);
            var textX = tab.Center.X - painter.MeasureAt(label, 10f / HudPainter.BakePixels) / 2;
            painter.Text(label, textX, tab.Bottom - 12, SocialGold, 10f / HudPainter.BakePixels);
        }

        DrawSocialRows(painter, box, party, pointer);
        if (Page == HudPartyPage.Party)
        {
            DrawBrowserPartyControls(painter, box, party, self, pointer);
        }

        DrawSocialFooter(painter, box, party, self, pointer);
    }

    private void DrawBrowserPartyControls(HudPainter painter, Rectangle box, PartyInfo party, ulong self, Point pointer)
    {
        var portuguese = ClientText.Language == "pt-BR";
        if (party.Members.Count == 0)
        {
            var card = BrowserGroupCard(box);
            painter.FramedBox(card, SocialBorder, Color.FromNonPremultiplied(31, 26, 13, 185), 3);
            painter.ColorBox(new Rectangle(card.X + 1, card.Y + 6, 3, card.Height - 12), 102, 163, 92, 220);
            var label = CreatingParty ? portuguese ? "CRIANDO..." : "CREATING..." : portuguese ? "CRIAR GRUPO" : "CREATE PARTY";
            SocialButton(painter, BrowserCreateGroup(box), label, !CreatingParty, pointer, true);
            var hint = FitSocial(painter, portuguese ? "Aberto: outros entram sem convite" : "Open: players can join freely",
                card.Width - 165, 9f / HudPainter.BakePixels);
            SocialText(painter, hint,
                card.X + 125, card.Y + 15, SocialMuted, 9f / HudPainter.BakePixels);
            DrawPartyOpenToggle(painter, BrowserOpenGroup(box), !CreatingParty);
            return;
        }

        SocialText(painter, SocialPartyTitle(party.Members.Count),
            box.X + 24, box.Y + 107, SocialGold);
        SocialText(painter, portuguese ? "Aberto" : "Open", box.Right - 100, box.Y + 107, SocialMuted);
        DrawPartyOpenToggle(painter, BrowserPartyOpen(box), party.Leads(self));
        var labels = portuguese ? PortuguesePartyCommands : EnglishPartyCommands;
        for (var command = 0; command < 4; command++)
        {
            var enabled = (command == 3 || party.Leads(self)) && (command is 1 or 3 || Picked != 0 && Picked != self);
            SocialButton(painter, BrowserPartyCommand(box, command), labels[command], enabled, pointer);
        }
    }

    private void DrawPartyOpenToggle(HudPainter painter, Rectangle bounds, bool enabled)
    {
        painter.FramedBox(bounds, enabled ? SocialBorder : Color.FromNonPremultiplied(88, 78, 55, 130),
            PartyOpen ? new Color(61, 81, 43) : new Color(30, 27, 17), 3);
        if (PartyOpen)
        {
            painter.Disc(bounds.Right - 7, bounds.Center.Y, 4, new Color(204, 190, 132));
        }
        else
        {
            painter.Disc(bounds.Left + 7, bounds.Center.Y, 4, SocialMuted);
        }
    }

    private static Rectangle DirectoryRow(Rectangle box, int row) => new(box.X + 22, box.Y + 104 + row * 52, box.Width - 44, 47);
    internal static Rectangle DirectoryRefresh(Rectangle box) => new(box.X + 24, box.Bottom - 56, 103, 29);
    internal static Rectangle DirectoryJoin(Rectangle box) => new(box.Right - 127, box.Bottom - 56, 103, 29);
    internal static Rectangle DirectoryBack(Rectangle box) => new(box.X + 135, box.Bottom - 56, Math.Max(40, box.Width - 270), 29);

    private void ClickPartyDirectory(Rectangle box, Point pointer)
    {
        if (DirectoryBack(box).Contains(pointer))
        {
            _socialFinding = false;
            return;
        }

        if (DirectoryRefresh(box).Contains(pointer))
        {
            SearchPartyDirectory();
            return;
        }

        var selected = SelectedDirectoryEntry();
        if (DirectoryJoin(box).Contains(pointer) && selected is not null && selected.MemberCount < PartyLimits.MaxMembers)
        {
            JoinPartyRequested?.Invoke(_directoryLeader);
            _socialFinding = false;
            return;
        }

        var visible = Math.Max(1, (box.Height - 192) / 52);
        for (var row = 0; row < visible && row + _directoryFirst < Directory.Count; row++)
        {
            if (DirectoryRow(box, row).Contains(pointer))
            {
                _directoryLeader = Directory[row + _directoryFirst].LeaderId;
                return;
            }
        }
    }

    private void DrawPartyDirectory(HudPainter painter, Rectangle box, Point pointer)
    {
        var portuguese = ClientText.Language == "pt-BR";
        SocialText(painter, portuguese ? "Procurar grupo" : "Find party", box.X + 24, box.Y + 59, SocialGold);
        var visible = Math.Max(1, (box.Height - 192) / 52);
        _directoryFirst = Math.Clamp(_directoryFirst, 0, Math.Max(0, Directory.Count - visible));
        for (var row = 0; row < visible && row + _directoryFirst < Directory.Count; row++)
        {
            var entry = Directory[row + _directoryFirst];
            var text = DirectoryLabel(entry);
            var bounds = DirectoryRow(box, row);
            painter.FramedBox(bounds, entry.LeaderId == _directoryLeader ? SocialBorder : Color.FromNonPremultiplied(74, 65, 43, 120),
                Color.FromNonPremultiplied(30, 27, 17, entry.LeaderId == _directoryLeader ? 220 : 130), 2);
            SocialText(painter, FitSocial(painter, entry.LeaderName, bounds.Width - 70, SocialTextScale), bounds.X + 10, bounds.Y + 7, SocialGold);
            SocialText(painter, text.Count, bounds.Right - 40, bounds.Y + 7, SocialMuted);
            SocialText(painter, FitSocial(painter, text.Detail, bounds.Width - 85, SocialTextScale),
                bounds.X + 10, bounds.Y + 25, SocialMuted);
            SocialText(painter, entry.IsOpen ? portuguese ? "Aberto" : "Open" : portuguese ? "Fechado" : "Closed",
                bounds.Right - 55, bounds.Y + 25, entry.IsOpen ? new Color(120, 181, 107) : SocialMuted);
        }

        if (Directory.Count == 0)
        {
            SocialCentered(painter, _directoryLoading ? portuguese ? "Buscando grupos..." : "Searching for parties..."
                : portuguese ? "Nenhum grupo aberto no momento." : "No open parties right now.",
                BrowserList(box), box.Y + 180, SocialMuted);
        }

        if (Directory.Count > visible)
        {
            painter.ScrollBar(box.Right - 15, box.Y + 104, visible * 52, _directoryFirst, Directory.Count - visible);
        }

        SocialButton(painter, DirectoryRefresh(box), portuguese ? "ATUALIZAR" : "REFRESH", true, pointer);
        SocialButton(painter, DirectoryBack(box), portuguese ? "VOLTAR" : "BACK", true, pointer);
        var selected = SelectedDirectoryEntry();
        var canJoin = selected is not null && selected.MemberCount < PartyLimits.MaxMembers;
        var action = selected?.IsOpen == false ? portuguese ? "PEDIR" : "REQUEST" : portuguese ? "ENTRAR" : "JOIN";
        SocialButton(painter, DirectoryJoin(box), action, canJoin, pointer, true);
    }

    private void DrawSocialRows(HudPainter painter, Rectangle box, PartyInfo party, Point pointer)
    {
        var count = SocialCount(party);
        var list = BrowserList(box);
        if (count == 0)
        {
            var portuguese = ClientText.Language == "pt-BR";
            var title = BrowserTabIndex switch
            {
                0 => portuguese ? "Você não está em um grupo." : "You are not in a party.",
                1 => portuguese ? "Nenhum amigo ainda." : "No friends yet.",
                2 => portuguese ? "Você ainda não faz parte de um clã." : "You are not a clan member.",
                3 => portuguese ? "Ninguém bloqueado." : "Nobody blocked.",
                _ => portuguese ? "Nenhuma conversa recente." : "No recent conversations."
            };
            var hint = BrowserTabIndex == 1
                ? portuguese ? "Adicione pelo nome abaixo ou pelo botão Amigo do alvo." : "Add a name below or use the target's Friend button."
                : BrowserTabIndex == 0
                    ? portuguese ? "Use Procurar para achar um, ou convide alguém pelo nome" : "Use Find to join one, or invite a player by name."
                    : BrowserTabIndex == 3
                        ? portuguese ? "Sussurros de quem você bloquear não chegam até você." : "Blocked players cannot send you whispers."
                        : BrowserTabIndex == 4 ? portuguese ? "Quem trocar sussurros com você aparece aqui." : "Your recent whisper contacts appear here." : "";
            var middle = list.Y + Math.Min(50, list.Height / 3);
            DrawSocialIcon(painter, BrowserTabIndex, box.Center.X, middle, true, 16);
            SocialCentered(painter, title, list, middle + 26, SocialGold);
            SocialCentered(painter, hint, list, middle + 43, SocialMuted, 10f / HudPainter.BakePixels);
            if (BrowserTabIndex == 0)
            {
                SocialCentered(painter, portuguese ? "abaixo." : "", list, middle + 57, SocialMuted, 10f / HudPainter.BakePixels);
            }

            return;
        }

        var visible = Math.Max(1, list.Height / 35);
        _socialFirst = Math.Clamp(_socialFirst, 0, Math.Max(0, count - visible));
        for (var row = 0; row < visible && _socialFirst + row < count; row++)
        {
            var index = _socialFirst + row;
            var bounds = BrowserRow(box, row);
            var name = SocialName(party, index);
            var selected = string.Equals(_socialPicked, name, StringComparison.OrdinalIgnoreCase);
            if (selected || bounds.Contains(pointer))
            {
                painter.FramedBox(bounds, selected ? SocialBorder : Color.FromNonPremultiplied(85, 80, 58, 160),
                    Color.FromNonPremultiplied(57, 46, 23, selected ? 190 : 90), 2);
            }

            var online = Page == HudPartyPage.Clan ? Clan.Info.Members[index].Online : Page == HudPartyPage.Party ? true : Friends.OnlineAt(index);
            if (online.HasValue)
            {
                painter.Disc(bounds.X + 9, bounds.Y + 11, 3, online.Value ? new Color(102, 197, 112) : new Color(105, 101, 91));
            }

            SocialText(painter, FitSocial(painter, name, bounds.Width - 80, HudPainter.TextScale), bounds.X + 20, bounds.Y + 3,
                online == false ? SocialMuted : selected ? Color.Wheat : SocialGold);
            if (Page == HudPartyPage.Party)
            {
                var member = party.Members[index];
                SocialText(painter, SocialLevelLabel(member.Level), bounds.Right - 51, bounds.Y + 3, SocialMuted);
                var bar = new Rectangle(bounds.X + 20, bounds.Bottom - 9, bounds.Width - 43, 3);
                painter.ColorBox(bar, 47, 39, 23, 220);
                var life = member.MaxLife <= 0 ? 0 : (int)Math.Clamp((long)member.Life * bar.Width / member.MaxLife, 0, bar.Width);
                painter.ColorBox(new Rectangle(bar.X, bar.Y, life, bar.Height), 93, 155, 89, 235);
            }
            else if (Page == HudPartyPage.Clan)
            {
                var member = Clan.Info.Members[index];
                SocialText(painter, SocialClanLabel(member.Class, member.Level), bounds.X + 20, bounds.Y + 18, SocialMuted);
            }
        }

        if (count > visible)
        {
            painter.ScrollBar(list.Right + 4, list.Y, list.Height, _socialFirst, count - visible);
        }
    }

    private void DrawSocialFooter(HudPainter painter, Rectangle box, PartyInfo party, ulong self, Point pointer)
    {
        var footer = BrowserFooter(box);
        painter.FramedBox(footer, Color.FromNonPremultiplied(112, 93, 53, 120), Color.FromNonPremultiplied(24, 20, 11, 145), 4);
        var portuguese = ClientText.Language == "pt-BR";
        var hint = _socialMessage.Length > 0 ? _socialMessage : portuguese ? "Clique em um nome da lista para ver as ações." : "Select a name in the list to see actions.";
        SocialText(painter, hint, footer.X + 12, footer.Y + 15, SocialMuted, 10f / HudPainter.BakePixels);
        var actions = portuguese ? SocialPortugueseActions[BrowserTabIndex] : SocialEnglishActions[BrowserTabIndex];
        for (var action = 0; action < actions.Length; action++)
        {
            var enabled = Page == HudPartyPage.Party
                ? action == 1 || _socialPicked.Length > 0
                : Page == HudPartyPage.Clan && action == 3 ? Clan.HasClan
                : _socialPicked.Length > 0;
            SocialButton(painter, BrowserAction(box, action), actions[action], enabled, pointer);
        }

        if (BrowserTabIndex == 4)
        {
            return;
        }

        if (Page == HudPartyPage.Clan)
        {
            SocialText(painter, Clan.HasClan ? Clan.Info.Name : portuguese ? "Encontre o Mestre de Clãs na cidade." : "Visit the Clan Master in town.",
                footer.X + 12, footer.Bottom - 30, SocialGold);
            return;
        }

        var name = BrowserName(box);
        painter.FramedBox(name, _socialTyping ? new Color(182, 158, 89) : SocialBorder, new Color(14, 13, 8), 2);
        var text = _socialName.Length > 0 ? _socialNameText : portuguese ? "Nome do personagem" : "Character name";
        text = FitSocial(painter, text, name.Width - 12, SocialTextScale);
        SocialText(painter, text, name.X + 8, name.Y + 7,
            _socialName.Length > 0 ? SocialGold : SocialMuted);
        if (_socialTyping && Environment.TickCount64 / 500 % 2 == 0)
        {
            SocialText(painter, "|", name.X + 8 + painter.MeasureAt(text, SocialTextScale), name.Y + 7,
                _socialName.Length > 0 ? SocialGold : SocialMuted);
        }
        var submit = Page == HudPartyPage.Party ? portuguese ? "CONVIDAR" : "INVITE"
            : BrowserTabIndex == 3 ? portuguese ? "BLOQUEAR" : "BLOCK" : portuguese ? "ADICIONAR" : "ADD";
        SocialButton(painter, BrowserSubmit(box), submit, true, pointer, BrowserTabIndex != 3, BrowserTabIndex == 3);
    }

    private static Rectangle ProfileBack(Rectangle box) => new(box.X + 24, box.Bottom - 56, box.Width - 48, 32);

    private void DrawSocialProfile(HudPainter painter, Rectangle box, Point pointer)
    {
        RefreshProfileText();
        var portuguese = ClientText.Language == "pt-BR";
        SocialText(painter, portuguese ? "Perfil do personagem" : "Character profile", box.X + 24, box.Y + 59, SocialGold);
        SocialText(painter, _socialProfile.Name, box.X + 24, box.Y + 99, Color.Wheat, 17f / HudPainter.BakePixels);
        var y = box.Y + 140;
        if (_socialProfile.Online is { } online)
        {
            SocialText(painter, online ? "Online" : "Offline", box.X + 24, y, online ? new Color(120, 192, 123) : SocialMuted);
            y += 30;
        }

        if (_socialProfile.ClassName.Length > 0)
        {
            SocialText(painter, _profileClassLine, box.X + 24, y, SocialGold);
            y += 30;
        }

        if (_socialProfile.Level.HasValue)
        {
            SocialText(painter, _profileLevelLine, box.X + 24, y, SocialGold);
            y += 30;
        }

        if (_socialProfile.ClanName.Length > 0)
        {
            SocialText(painter, _profileClanLine, box.X + 24, y, SocialGold);
        }

        SocialButton(painter, ProfileBack(box), portuguese ? "VOLTAR" : "BACK", true, pointer);
    }

    private static void DrawSocialIcon(HudPainter painter, int index, int x, int y, bool active, int radius = 9)
    {
        painter.Disc(x, y, radius, Color.FromNonPremultiplied(123, 110, 72, 180));
        painter.Disc(x, y, radius - 1, new Color(27, 30, 23));
        var color = index == 1 ? new Color(231, 190, 78) : active ? SocialGold : SocialMuted;
        var half = Math.Max(4, radius / 2);
        switch (index)
        {
            case 0:
                for (var step = -half; step <= half; step++)
                {
                    painter.Fill(new Rectangle(x + step, y + step, 2, 2), color);
                    painter.Fill(new Rectangle(x - step, y + step, 2, 2), color);
                }

                break;
            case 1:
                painter.Disc(x - half / 2, y - half / 2, half / 2 + 1, color);
                painter.Disc(x + half / 2, y - half / 2, half / 2 + 1, color);
                painter.DownTriangle(x, y + half + 1, half, half + 2, color);
                break;
            case 2:
                painter.Fill(new Rectangle(x - half + 1, y - half, half * 2 - 1, half + 1), color);
                painter.DownTriangle(x, y + half + 1, half - 1, half + 1, color);
                painter.Fill(new Rectangle(x, y - half + 1, 1, half + 2), new Color(83, 61, 45));
                break;
            case 3:
                painter.Disc(x, y, half + 1, new Color(200, 155, 148));
                painter.Disc(x, y, half, color);
                for (var step = -half; step <= half; step++)
                {
                    painter.Fill(new Rectangle(x + step, y + step, 2, 2), new Color(100, 52, 44));
                }

                break;
            default:
                painter.RoundedFill(new Rectangle(x - half - 2, y - half, half * 2 + 4, half * 2), color, 1);
                painter.DownTriangle(x - half + 1, y + half + 2, 2, 3, color);
                for (var dot = -1; dot <= 1; dot++)
                {
                    painter.Fill(new Rectangle(x + dot * 3, y - 1, 1, 2), new Color(32, 32, 26));
                }

                break;
        }
    }

    private void SocialButton(HudPainter painter, Rectangle bounds, string text, bool enabled, Point pointer, bool green = false, bool danger = false)
    {
        var hover = enabled && bounds.Contains(pointer);
        var edge = enabled ? danger ? new Color(174, 109, 89) : green ? new Color(144, 174, 103) : SocialBorder : Color.FromNonPremultiplied(87, 76, 52, 120);
        var top = danger && enabled ? new Color(128, 60, 48) : green && enabled ? new Color(67, 125, 58) : hover ? new Color(73, 59, 30) : Color.FromNonPremultiplied(33, 28, 17, 220);
        var bottom = danger && enabled ? new Color(81, 32, 25) : green && enabled ? new Color(30, 73, 34) : Color.FromNonPremultiplied(16, 14, 9, 225);
        painter.FramedBox(bounds, edge, top, bottom, 3);
        SocialCentered(painter, text, bounds, bounds.Y + 8, enabled ? green ? Color.Wheat : SocialGold : new Color(96, 88, 69), 11f / HudPainter.BakePixels);
    }

    private static void SocialText(HudPainter painter, string text, int x, int y, Color color, float scale = SocialTextScale) =>
        painter.Text(text, x, y, color, scale);

    private void SocialCentered(HudPainter painter, string text, Rectangle bounds, int y, Color color, float scale = SocialTextScale)
    {
        var shown = FitSocial(painter, text, bounds.Width - 8, scale);
        painter.Text(shown, bounds.Center.X - painter.MeasureAt(shown, scale) / 2, y, color, scale);
    }

}
