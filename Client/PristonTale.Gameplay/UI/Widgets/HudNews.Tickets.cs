using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The support tabs: the player's tickets as cards, one ticket as a conversation with its answer box, and the form that
// opens or edits a ticket. Everything travels as chat commands answered by "Ticket> ..." lines (TicketChat on the server).
internal sealed partial class HudNews
{
    private static readonly string[][] SupportIcons =
    [
        ["111110", "100010", "101110", "100010", "101110", "100010", "111110"],
        ["000000", "111111", "100001", "101101", "100001", "111111", "000000"],
        ["111111", "110011", "101101", "100001", "111111", "000000", "000000"],
        ["010010", "001100", "111111", "011110", "111111", "011110", "100001"],
        ["011110", "110011", "110011", "011110", "001100", "001100", "000000"],
        ["111111", "111111", "101011", "111111", "011110", "010000", "000000"]
    ];

    // The categories of the form, as the server stores them, and their icons in SupportIcons.
    private static readonly string[] CategoryKeys = ["Bug", "Melhoria", "Outro"];
    private const int CategoryIcon = 3;
    private const int DescriptionLimit = 1500;
    private const int SubjectLimit = 55;
    private const int PartCharacters = 48;
    private const int CardHeight = 50;
    private const int CardStep = 56;
    private const int LineHeight = 16;
    private const int BubbleWidth = 380;
    private const long ListRefreshMs = 15000;
    private const long ViewRefreshMs = 10000;
    private const long AnswerTimeoutMs = 8000;
    private const long SendTimeoutMs = 20000;

    private static readonly Rectangle Content = new(102, 120, 600, 452);
    private static readonly Rectangle RefreshButton = new(500, 122, 84, 24);
    private static readonly Rectangle NewTicketButton = new(590, 122, 112, 24);
    private static readonly Rectangle BackButton = new(102, 122, 64, 24);
    private static readonly Rectangle EditButton = new(114, 210, 56, 22);
    private static readonly Rectangle CancelButton = new(176, 210, 116, 22);
    private static readonly Rectangle ReplyBox = new(102, 418, 600, 154);
    private static readonly Rectangle ReplyField = new(114, 428, 576, 62);
    private static readonly Rectangle ReplySend = new(608, 532, 84, 30);
    private static readonly Rectangle Subject = new(102, 178, 600, 27);
    private static readonly Rectangle Description = new(102, 214, 600, 200);
    private static readonly Rectangle FormSend = new(570, 470, 132, 30);
    private static readonly Rectangle FormCancel = new(430, 470, 132, 30);

    internal sealed record TicketItem(Guid Id, string Number, string Status, string Category, string Subject, string Details, long Updated = 0)
    {
        // An answered ticket the player has not read yet marks the plaque until its seen key is kept.
        public string SeenKey { get; } = Id.ToString("N") + ":" + Updated.ToString(CultureInfo.InvariantCulture);
    }

    internal sealed record TicketMessageView(bool Staff, string Header, string Text);

    internal sealed class TicketDetail
    {
        public Guid Id;
        public string Number = "";
        public string Status = "";
        public string Category = "";
        public string Subject = "";
        public string Opened = "";
        public bool Editable;
        public List<TicketMessageView> Messages { get; } = [];
        public List<(bool Staff, string Header, List<string> Lines)> Layout;
    }

    private readonly List<TicketItem> _ticketList = [];
    private List<TicketItem> _incomingList;
    private TicketDetail _detail;
    private TicketDetail _incomingDetail;
    private StringBuilder _incomingText;
    private bool _incomingStaff;
    private string _incomingHeader = "";
    private Guid _openTicket;
    private Guid _editingTicket;
    private int _tab;
    private int _listScroll;
    private int _detailScroll;
    private int _category;
    private Guid _savedTicket;
    private bool _editingDescription;
    private bool _confirmCancel;
    private string _subject = "";
    private string _description = "";
    private string _reply = "";
    private string _state = "";
    private long _deadline;
    private long _nextTicketRefresh;

    public Func<string, Task> Send { get; set; }

    public bool TicketTyping => _open && !PageDrawn && _tab != 0;

    private static Rectangle Tab(int index) => new(102 + index * 202, 64, 194, 44);

    private static Rectangle Category(int index) => new(102 + index * 202, 124, 194, 46);

    private static Rectangle Card(int index) => new(Content.X, 152 + index * CardStep, Content.Width, CardHeight);

    private bool Viewing => _tab == 1 && _openTicket != Guid.Empty;

    public bool ReceiveTicket(string line)
    {
        if (!line.StartsWith("Ticket> ", StringComparison.Ordinal))
        {
            return false;
        }

        var value = line[8..];
        var space = value.IndexOf(' ', StringComparison.Ordinal);
        var word = space < 0 ? value : value[..space];
        var rest = space < 0 ? "" : value[(space + 1)..];
        switch (word)
        {
            case "list":
                _incomingList = [];
                break;
            case "item":
                if (_incomingList is not null && ParseItem(rest) is { } item)
                {
                    _incomingList.Add(item);
                }

                break;
            case "listend":
                if (_incomingList is not null)
                {
                    _ticketList.Clear();
                    _ticketList.AddRange(_incomingList);
                    _listScroll = Math.Clamp(_listScroll, 0, MaxListScroll);
                    _incomingList = null;
                }

                _backgroundList = false;
                Answered("");
                break;
            case "view":
                _incomingDetail = ParseView(rest);
                _incomingText = null;
                break;
            case "subject":
                if (_incomingDetail is not null)
                {
                    _incomingDetail.Subject = rest;
                }

                break;
            case "msg":
                EndIncomingMessage();
                var fields = rest.Split(' ', 3);
                if (fields.Length == 3 && long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var at))
                {
                    _incomingStaff = fields[0] == "S";
                    _incomingHeader = fields[2] + " · " + Date(at);
                    _incomingText = new StringBuilder();
                }

                break;
            case "text":
                if (_incomingText is not null)
                {
                    if (_incomingText.Length > 0)
                    {
                        _incomingText.Append('\n');
                    }

                    _incomingText.Append(rest);
                }

                break;
            case "more":
                _incomingText?.Append(rest);
                break;
            case "viewend":
                EndIncomingMessage();
                if (_incomingDetail is { } detail && detail.Id == _openTicket)
                {
                    var newMessages = _detail is null || _detail.Id != detail.Id || _detail.Messages.Count != detail.Messages.Count;
                    _detail = detail;
                    if (newMessages)
                    {
                        _detailScroll = int.MaxValue;
                    }
                }

                _incomingDetail = null;
                Answered("");
                break;
            case "saved":
                _subject = "";
                _description = "";
                _editingTicket = Guid.Empty;
                _ = Guid.TryParseExact(rest, "N", out _savedTicket);
                Answered("saved");
                OpenTicket(_savedTicket);
                break;
            case "replied":
                _reply = "";
                Answered("replied");
                Request("/ticketview " + rest);
                break;
            case "edited":
                _subject = "";
                _description = "";
                _editingTicket = Guid.Empty;
                Answered("edited");
                OpenTicket(Guid.TryParseExact(rest, "N", out var edited) ? edited : Guid.Empty);
                break;
            case "closed":
                Answered("closed");
                Request("/ticketview " + rest);
                break;
            default:
                // "error", and the plain list of the earlier window ("begin", "line", "end").
                _incomingList = null;
                _incomingDetail = null;
                Answered(word == "error" && !_backgroundList ? "error" : "");
                _backgroundList = false;
                break;
        }

        return true;
    }

    private void Answered(string state)
    {
        _state = state;
        _deadline = 0;
    }

    private void EndIncomingMessage()
    {
        if (_incomingDetail is not null && _incomingText is not null)
        {
            _incomingDetail.Messages.Add(new TicketMessageView(_incomingStaff, _incomingHeader, _incomingText.ToString()));
        }

        _incomingText = null;
    }

    // The fields after "item" and "view" (TicketChat).
    internal static TicketItem ParseItem(string text)
    {
        var fields = text.Split(' ', 9);
        if (fields.Length != 9 || !Guid.TryParseExact(fields[0], "N", out var id)
            || !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var created)
            || !long.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out var updated)
            || !int.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return null;
        }

        var portuguese = Portuguese;
        var messages = count == 1 ? (portuguese ? "1 mensagem" : "1 message") : count + (portuguese ? " mensagens" : " messages");
        var details = (portuguese ? "aberto " : "opened ") + Date(created) + " · " + messages + " · " + (portuguese ? "atualizado " : "updated ") + Date(updated);
        return new TicketItem(id, "#" + fields[1], fields[2], fields[3] == "-" ? "" : fields[3], fields[8], details, updated);
    }

    internal static TicketDetail ParseView(string text)
    {
        var fields = text.Split(' ', 7);
        if (fields.Length != 7 || !Guid.TryParseExact(fields[0], "N", out var id)
            || !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var created))
        {
            return null;
        }

        var opened = (Portuguese ? "Aberto em " : "Opened on ") + Date(created) + (Portuguese ? " por " : " by ") + fields[6];
        return new TicketDetail
        {
            Id = id,
            Number = "#" + fields[1],
            Status = fields[2],
            Category = fields[3] == "-" ? "" : fields[3],
            Opened = opened,
            Editable = fields[5] == "1"
        };
    }

    private static bool Portuguese => Localization.ClientText.Language == "pt-BR";

    private static string Date(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("dd/MM/yyyy, HH:mm", CultureInfo.InvariantCulture);

    public void UpdateTickets()
    {
        var now = Environment.TickCount64;
        if (_deadline != 0 && now >= _deadline)
        {
            _deadline = 0;
            _incomingList = null;
            _incomingDetail = null;
            _state = "error";
        }

        if (!_open || _tab != 1 || now < _nextTicketRefresh || _state == "waiting")
        {
            return;
        }

        if (!Viewing)
        {
            Request("/ticketlist", ListRefreshMs);
        }
        else if (_reply.Length == 0)
        {
            Request("/ticketview " + _openTicket.ToString("N"), ViewRefreshMs);
        }
    }

    private void Request(string command, long refreshMs = ListRefreshMs)
    {
        _state = "waiting";
        _deadline = Environment.TickCount64 + AnswerTimeoutMs;
        _nextTicketRefresh = Environment.TickCount64 + refreshMs;
        _ = Send?.Invoke(command);
    }

    private void OpenTicket(Guid id)
    {
        _tab = 1;
        _confirmCancel = false;
        _reply = "";
        _openTicket = id;
        if (id == Guid.Empty)
        {
            _detail = null;
            Request("/ticketlist");
            return;
        }

        if (_detail?.Id != id)
        {
            _detail = null;
        }

        Request("/ticketview " + id.ToString("N"), ViewRefreshMs);
    }

    // A long text goes in parts after the command that starts it, then "/ticketsave".
    private async Task SendTextAsync(string start, string text)
    {
        _state = "waiting";
        _deadline = Environment.TickCount64 + SendTimeoutMs;
        if (Send is not { } send)
        {
            return;
        }

        try
        {
            await send(start);
            for (var offset = 0; offset < text.Length; offset += PartCharacters)
            {
                var part = text.Substring(offset, Math.Min(PartCharacters, text.Length - offset));
                await send("/ticketpart " + Convert.ToBase64String(Encoding.UTF8.GetBytes(part)));
            }

            await send("/ticketsave");
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or OperationCanceledException)
        {
            // The window shows a timeout when the server does not acknowledge the text.
        }
    }

    private bool ClickTickets(Point point)
    {
        for (var index = 0; index < 3; index++)
        {
            if (Tab(index).Contains(point))
            {
                _tab = index;
                if (_tab == 1)
                {
                    OpenTicket(Guid.Empty);
                }

                return true;
            }
        }

        if (_tab == 1)
        {
            if (Viewing)
            {
                ClickDetail(point);
            }
            else
            {
                ClickList(point);
            }
        }
        else if (_tab == 2)
        {
            ClickForm(point);
        }

        return _tab != 0;
    }

    private void ClickList(Point point)
    {
        if (RefreshButton.Contains(point) && _state != "waiting")
        {
            Request("/ticketlist");
            return;
        }

        if (NewTicketButton.Contains(point))
        {
            StartForm(Guid.Empty);
            return;
        }

        for (var index = 0; index < _ticketList.Count; index++)
        {
            var card = Card(index - _listScroll);
            if (card.Top >= 152 && card.Bottom <= Content.Bottom && card.Contains(point))
            {
                OpenTicket(_ticketList[index].Id);
                return;
            }
        }
    }

    private void ClickDetail(Point point)
    {
        if (BackButton.Contains(point))
        {
            OpenTicket(Guid.Empty);
            return;
        }

        if (_detail is not { } detail || _state == "waiting")
        {
            return;
        }

        if (detail.Editable && EditButton.Contains(point))
        {
            StartForm(detail.Id);
            return;
        }

        if (detail.Editable && CancelButton.Contains(point))
        {
            if (_confirmCancel)
            {
                _confirmCancel = false;
                Request("/ticketclose " + detail.Id.ToString("N"));
            }
            else
            {
                _confirmCancel = true;
            }

            return;
        }

        _confirmCancel = false;
        if (!Writable(detail))
        {
            return;
        }

        if (ReplySend.Contains(point) && !string.IsNullOrWhiteSpace(_reply))
        {
            _ = SendTextAsync("/ticketreply " + detail.Id.ToString("N"), _reply.Trim());
        }
    }

    private static bool Writable(TicketDetail detail) => detail.Status is "Open" or "Answered";

    // The form for a new ticket, or for editing one the staff has not answered yet.
    private void StartForm(Guid editing)
    {
        _tab = 2;
        _editingTicket = editing;
        _state = "";
        _editingDescription = false;
        if (editing != Guid.Empty && _detail is { } detail && detail.Id == editing)
        {
            _category = Math.Max(0, Array.IndexOf(CategoryKeys, detail.Category));
            _subject = detail.Subject;
            _description = detail.Messages.Count > 0 ? detail.Messages[0].Text : "";
        }
        else
        {
            _subject = "";
            _description = "";
        }
    }

    private void ClickForm(Point point)
    {
        for (var index = 0; index < CategoryKeys.Length; index++)
        {
            if (Category(index).Contains(point))
            {
                _category = index;
                return;
            }
        }


        if (Subject.Contains(point) || Description.Contains(point))
        {
            _editingDescription = Description.Contains(point);
            return;
        }

        if (_editingTicket != Guid.Empty && FormCancel.Contains(point))
        {
            var ticket = _editingTicket;
            _editingTicket = Guid.Empty;
            OpenTicket(ticket);
            return;
        }

        if (FormSend.Contains(point) && _state != "waiting")
        {
            if (string.IsNullOrWhiteSpace(_subject) || string.IsNullOrWhiteSpace(_description))
            {
                _state = "invalid";
                return;
            }

            var subject = "[" + CategoryKeys[_category] + "] " + _subject.Trim();
            _ = _editingTicket == Guid.Empty
                ? SendTextAsync("/ticketstart " + subject, _description)
                : SendTextAsync("/ticketedit " + _editingTicket.ToString("N") + " " + subject, _description);
        }
    }

    public void TypeTicket(char[] input, bool backspace)
    {
        if (_state == "waiting")
        {
            return;
        }

        if (_tab == 2)
        {
            if (_editingDescription)
            {
                Type(ref _description, input, backspace, DescriptionLimit);
            }
            else
            {
                Type(ref _subject, input, backspace, SubjectLimit);
            }
        }
        else if (Viewing && _detail is { } detail && Writable(detail))
        {
            Type(ref _reply, input, backspace, DescriptionLimit);
        }
    }

    private static void Type(ref string field, char[] input, bool backspace, int limit)
    {
        if (backspace && field.Length > 0)
        {
            field = field[..^1];
        }

        foreach (var character in input)
        {
            if (field.Length < limit && !char.IsControl(character) && !char.IsSurrogate(character) && character != '|')
            {
                field += character;
            }
        }
    }

    private bool ScrollTickets(int wheel)
    {
        if (_tab != 1 || wheel == 0)
        {
            return _tab != 0;
        }

        if (Viewing)
        {
            _detailScroll = Math.Max(0, _detailScroll - Math.Sign(wheel) * 3 * LineHeight);
        }
        else
        {
            _listScroll = Math.Clamp(_listScroll - Math.Sign(wheel), 0, MaxListScroll);
        }

        return true;
    }

    private static int VisibleCards => (Content.Bottom - 152) / CardStep;

    private int MaxListScroll => Math.Max(0, _ticketList.Count - VisibleCards);

    private bool DrawTickets(HudPainter painter, bool portuguese)
    {
        string[] tabs = portuguese ? ["Novidades", "Meus tickets", "Novo ticket"] : ["News", "My tickets", "New ticket"];
        for (var index = 0; index < tabs.Length; index++)
        {
            IconButton(painter, Tab(index), tabs[index], index, _tab == index, false);
        }

        if (_tab == 0)
        {
            return false;
        }

        if (_tab == 1 && Viewing)
        {
            DrawDetail(painter, portuguese);
        }
        else if (_tab == 1)
        {
            DrawList(painter, portuguese);
        }
        else
        {
            DrawForm(painter, portuguese);
        }

        var status = _state switch
        {
            "saved" => portuguese ? "Ticket enviado." : "Ticket sent.",
            "replied" => portuguese ? "Resposta enviada." : "Reply sent.",
            "edited" => portuguese ? "Ticket alterado." : "Ticket changed.",
            "closed" => portuguese ? "Ticket cancelado." : "Ticket cancelled.",
            "waiting" => portuguese ? "Aguardando servidor..." : "Waiting for server...",
            "invalid" => portuguese ? "Preencha título e descrição." : "Fill in subject and description.",
            "error" => portuguese ? "Suporte indisponível." : "Support unavailable.",
            _ => ""
        };
        painter.Text(status, Content.X, 576, Color.Wheat);
        return true;
    }

    private void DrawList(HudPainter painter, bool portuguese)
    {
        DrawSupportButton(painter, RefreshButton, portuguese ? "Atualizar" : "Refresh");
        DrawSupportButton(painter, NewTicketButton, portuguese ? "+ NOVO TICKET" : "+ NEW TICKET", primary: true);
        if (_ticketList.Count == 0)
        {
            if (_state != "waiting")
            {
                painter.Text(portuguese ? "Nenhum ticket." : "No tickets.", Content.X, 160, Color.Wheat);
            }

            return;
        }

        for (var index = _listScroll; index < _ticketList.Count && index - _listScroll < VisibleCards; index++)
        {
            var item = _ticketList[index];
            var card = Card(index - _listScroll);
            painter.FramedBox(card, new Color(102, 77, 39), new Color(42, 34, 22), new Color(24, 19, 13), 4);
            painter.Fill(new Rectangle(card.X, card.Y, 3, card.Height), new Color(207, 160, 44));
            TicketTitle(painter, card.X + 12, card.Y + 8, item.Number, item.Category, card.Right - 110, item.Subject, portuguese);
            StatusChip(painter, card.Right - 12, card.Y + 8, item.Status, portuguese);
            painter.Text(painter.Fit(item.Details, card.Width - 24), card.X + 12, card.Y + 28, new Color(186, 163, 120));
        }

        if (_ticketList.Count > VisibleCards)
        {
            painter.ScrollBar(Content.Right + 2, 152, VisibleCards * CardStep, _listScroll, MaxListScroll);
        }
    }

    private void DrawDetail(HudPainter painter, bool portuguese)
    {
        DrawSupportButton(painter, BackButton, portuguese ? "‹ Voltar" : "‹ Back");
        if (_detail is not { } detail)
        {
            return;
        }

        var header = new Rectangle(Content.X, 152, Content.Width, detail.Editable ? 112 : 64);
        painter.FramedBox(header, new Color(102, 77, 39), new Color(42, 34, 22), new Color(24, 19, 13), 4);
        painter.Fill(new Rectangle(header.X, header.Y, 3, header.Height), new Color(207, 160, 44));
        TicketTitle(painter, header.X + 12, header.Y + 10, detail.Number, detail.Category, header.Right - 110, detail.Subject, portuguese);
        StatusChip(painter, header.Right - 12, header.Y + 10, detail.Status, portuguese);
        painter.Text(painter.Fit(detail.Opened, header.Width - 24), header.X + 12, header.Y + 34, new Color(186, 163, 120));
        if (detail.Editable)
        {
            DrawSupportButton(painter, EditButton, portuguese ? "Editar" : "Edit");
            DrawDangerButton(painter, CancelButton, _confirmCancel ? (portuguese ? "Confirmar" : "Confirm") : (portuguese ? "Cancelar ticket" : "Cancel ticket"));
            painter.Text(portuguese ? "Enquanto a equipe não responder, você pode editar ou cancelar este ticket."
                : "Until the staff answers, you may edit or cancel this ticket.", header.X + 12, header.Y + 88, new Color(150, 130, 100));
        }

        var writable = Writable(detail);
        var area = new Rectangle(Content.X, header.Bottom + 10, Content.Width, (writable ? ReplyBox.Top : Content.Bottom) - header.Bottom - 16);
        DrawConversation(painter, detail, area);
        if (writable)
        {
            DrawReplyBox(painter, portuguese);
        }
        else
        {
            var closed = detail.Status == "Cancelled" ? (portuguese ? "Ticket cancelado por você." : "Ticket cancelled by you.")
                : (portuguese ? "Ticket fechado pela equipe." : "Ticket closed by the staff.");
            painter.Text(closed, Content.X, Content.Bottom - 14, Color.Wheat);
        }
    }

    private void DrawConversation(HudPainter painter, TicketDetail detail, Rectangle area)
    {
        detail.Layout ??= LayoutMessages(painter, detail);
        var total = 0;
        foreach (var message in detail.Layout)
        {
            total += (message.Lines.Count + 1) * LineHeight + 18;
        }

        var maximum = Math.Max(0, total - area.Height);
        _detailScroll = Math.Clamp(_detailScroll, 0, maximum);
        var y = area.Y - _detailScroll;
        foreach (var message in detail.Layout)
        {
            var width = BubbleWidth;
            foreach (var line in message.Lines)
            {
                width = Math.Max(width, Math.Min(Content.Width - 40, painter.Measure(line) + 24));
            }

            var height = (message.Lines.Count + 1) * LineHeight + 10;
            var x = message.Staff ? area.X : area.Right - width;
            var top = Math.Max(y, area.Y);
            var bottom = Math.Min(y + height, area.Bottom);
            if (bottom > top)
            {
                painter.FramedBox(new Rectangle(x, top, width, bottom - top), new Color(102, 77, 39),
                    message.Staff ? new Color(30, 44, 30) : new Color(42, 34, 22), message.Staff ? new Color(18, 28, 18) : new Color(24, 19, 13), 4);
                for (var row = 0; row <= message.Lines.Count; row++)
                {
                    var lineY = y + 5 + row * LineHeight;
                    if (lineY >= area.Y && lineY + LineHeight <= area.Bottom)
                    {
                        painter.Text(row == 0 ? message.Header : message.Lines[row - 1], x + 12, lineY,
                            row == 0 ? new Color(186, 163, 120) : Color.White);
                    }
                }
            }

            y += height + 8;
        }

        if (maximum > 0)
        {
            painter.ScrollBar(Content.Right + 2, area.Y, area.Height, _detailScroll, maximum);
        }
    }

    private static List<(bool Staff, string Header, List<string> Lines)> LayoutMessages(HudPainter painter, TicketDetail detail)
    {
        var layout = new List<(bool Staff, string Header, List<string> Lines)>();
        foreach (var message in detail.Messages)
        {
            var lines = new List<string>();
            foreach (var paragraph in message.Text.Split('\n'))
            {
                var wrapped = HudTextLayout.Wrap(paragraph, Content.Width - 64, candidate => painter.Measure(candidate));
                lines.AddRange(wrapped.Count == 0 ? [""] : wrapped);
            }

            layout.Add((message.Staff, message.Header, lines));
        }

        return layout;
    }

    private void DrawReplyBox(HudPainter painter, bool portuguese)
    {
        painter.FramedBox(ReplyBox, new Color(102, 77, 39), new Color(36, 29, 19), new Color(22, 17, 11), 4);
        painter.ColorBox(ReplyField, 107, 79, 39, 255);
        var inner = ReplyField;
        inner.Inflate(-1, -1);
        painter.ColorBox(inner, 16, 13, 9, 255);
        if (_reply.Length == 0)
        {
            painter.Text(portuguese ? "Escreva uma resposta..." : "Write a reply...", inner.X + 8, inner.Y + 6, new Color(130, 115, 90));
        }
        else
        {
            DrawWrappedTail(painter, _reply, inner.X + 8, inner.Y + 6, inner.Width - 16, 3);
        }

        painter.TextCentered($"{_reply.Length}/{DescriptionLimit}", ReplyField.Right - 80, ReplyField.Right, ReplyField.Bottom + 4, new Color(150, 130, 100));
        DrawSupportButton(painter, ReplySend, portuguese ? "ENVIAR" : "SEND", primary: true);
    }

    private void DrawForm(HudPainter painter, bool portuguese)
    {
        string[] categories = portuguese ? ["Reportar bug", "Pedir melhoria", "Outro assunto"] : ["Report bug", "Suggest improvement", "Other"];
        for (var index = 0; index < categories.Length; index++)
        {
            IconButton(painter, Category(index), categories[index], index + CategoryIcon, index == _category, true);
        }

        DrawSupportButton(painter, Subject, _subject.Length == 0 ? (portuguese ? "Título" : "Subject") : _subject, !_editingDescription);
        painter.ColorBox(Description, 107, 79, 39, 255);
        var inner = Description;
        inner.Inflate(-1, -1);
        painter.ColorBox(inner, 16, 13, 9, 255);
        if (_description.Length == 0)
        {
            painter.Text(portuguese ? "Descrição" : "Description", inner.X + 8, inner.Y + 6, new Color(130, 115, 90));
        }
        else
        {
            DrawWrappedTail(painter, _description, inner.X + 8, inner.Y + 6, inner.Width - 16, 11);
        }

        painter.TextCentered($"{_description.Length}/{DescriptionLimit}", Description.Right - 80, Description.Right, Description.Bottom + 4,
            new Color(150, 130, 100));
        if (_editingTicket == Guid.Empty)
        {
            DrawSupportButton(painter, FormSend, portuguese ? "Enviar ticket" : "Send ticket", primary: true);
        }
        else
        {
            DrawSupportButton(painter, FormCancel, portuguese ? "Voltar" : "Back");
            DrawSupportButton(painter, FormSend, portuguese ? "Salvar alterações" : "Save changes", primary: true);
        }
    }

    // The last rows of a text being typed, wrapped by character to the width.
    private static void DrawWrappedTail(HudPainter painter, string text, int x, int y, int width, int rows)
    {
        var perRow = Math.Max(1, width / Math.Max(1, painter.Measure("m")));
        var first = Math.Max(0, (text.Length - 1) / perRow - (rows - 1)) * perRow;
        for (var offset = first; offset < text.Length; offset += perRow)
        {
            painter.Text(text.Substring(offset, Math.Min(perRow, text.Length - offset)), x, y + (offset - first) / perRow * LineHeight, Color.Wheat);
        }
    }

    // The category icon, "#1234", the category chip and the subject, from the left.
    private static void TicketTitle(HudPainter painter, int x, int y, string number, string category, int right, string subject, bool portuguese)
    {
        var icon = Array.IndexOf(CategoryKeys, category);
        DrawIcon(painter, x, y + 1, (icon < 0 ? 2 : icon) + CategoryIcon);
        x += 18;
        painter.Text(number, x, y, new Color(150, 130, 100));
        x += painter.Measure(number) + 8;
        if (category.Length > 0)
        {
            var label = portuguese || category != "Melhoria" ? category : "Improvement";
            var chip = new Rectangle(x, y - 1, painter.Measure(label) + 14, 17);
            painter.RoundedFill(chip, category switch
            {
                "Bug" => new Color(150, 52, 40),
                "Melhoria" => new Color(52, 108, 160),
                _ => new Color(90, 80, 70)
            }, 7);
            painter.TextCentered(label, chip.Left, chip.Right, y, Color.White);
            x = chip.Right + 8;
        }

        painter.Text(painter.Fit(subject, right - x), x, y, Color.Gold);
    }

    // The status chip, right-aligned at right.
    private static void StatusChip(HudPainter painter, int right, int y, string status, bool portuguese)
    {
        var (label, color) = status switch
        {
            "Answered" => (portuguese ? "Respondido" : "Answered", new Color(50, 120, 60)),
            "Closed" => (portuguese ? "Fechado" : "Closed", new Color(90, 90, 90)),
            "Cancelled" => (portuguese ? "Cancelado" : "Cancelled", new Color(120, 50, 40)),
            _ => (portuguese ? "Aberto" : "Open", new Color(150, 110, 30))
        };
        var chip = new Rectangle(right - painter.Measure(label) - 16, y - 1, painter.Measure(label) + 16, 17);
        painter.RoundedFill(chip, color, 7);
        painter.TextCentered(label, chip.Left, chip.Right, y, Color.White);
    }

    private static void DrawDangerButton(HudPainter painter, Rectangle bounds, string label)
    {
        painter.ColorBox(bounds, 190, 80, 60, 255);
        bounds.Inflate(-1, -1);
        painter.ColorBox(bounds, 120, 36, 28, 255);
        painter.TextCentered(label, bounds.Left, bounds.Right, bounds.Y + 4, Color.White);
    }

    private static void IconButton(HudPainter painter, Rectangle bounds, string text, int icon, bool active, bool stacked)
    {
        if (stacked)
        {
            DrawSupportButton(painter, bounds, "", active);
            DrawIcon(painter, bounds.Center.X - 6, bounds.Top + 6, icon);
            painter.TextCentered(text, bounds.Left, bounds.Right, bounds.Top + 25, active ? Color.White : Color.Wheat);
            return;
        }

        DrawTab(painter, bounds, text, icon, active);
    }

    // A tab of the window: a framed plate, gold and lighter while chosen with a diamond under it; its icon in a tile on
    // the left and its name in bold beside it.
    private static void DrawTab(HudPainter painter, Rectangle bounds, string text, int icon, bool active)
    {
        painter.FramedBox(bounds, active ? new Color(206, 166, 82) : new Color(76, 60, 39),
            active ? new Color(58, 45, 29) : new Color(36, 28, 20), active ? new Color(36, 28, 19) : new Color(26, 20, 15), 6);
        var tile = new Rectangle(bounds.X + 9, bounds.Center.Y - 14, 30, 28);
        painter.FramedBox(tile, new Color(96, 78, 52), new Color(58, 47, 34), new Color(40, 32, 23), 4);
        DrawIcon(painter, tile.Center.X - 6, tile.Center.Y - 7, icon);
        var scale = HudPainter.TextScale * 1.1f;
        var ink = active ? Color.White : new Color(196, 180, 152);
        painter.Text(text, tile.Right + 11, bounds.Center.Y - 7, ink, scale);
        painter.Text(text, tile.Right + 12, bounds.Center.Y - 7, ink, scale);
        if (active)
        {
            painter.Diamond(bounds.Center.X, bounds.Bottom, 4, new Color(222, 180, 90));
        }
    }

    private static void DrawIcon(HudPainter painter, int x, int y, int icon)
    {
        var glyph = SupportIcons[icon];
        var tint = icon switch
        {
            1 => (R: 143, G: 209, B: 229),
            3 => (R: 228, G: 113, B: 73),
            4 => (R: 247, G: 207, B: 107),
            _ => (R: 233, G: 210, B: 156)
        };
        for (var row = 0; row < glyph.Length; row++)
        {
            for (var column = 0; column < glyph[row].Length; column++)
            {
                if (glyph[row][column] == '1')
                {
                    painter.ColorBox(new Rectangle(x + column * 2, y + row * 2, 2, 2), tint.R, tint.G, tint.B, 255);
                }
            }
        }
    }
}
