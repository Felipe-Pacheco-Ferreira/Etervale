using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

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
    private static readonly Rectangle Subject = new(150, 225, 500, 27);
    private static readonly Rectangle Description = new(150, 260, 500, 122);
    private readonly List<string> _tickets = [];
    private int _tab;
    private int _ticketPage;
    private int _category;
    private static readonly Rectangle AttachmentButton = new(150, 393, 210, 22);
    private Uri _attachmentSite;
    private Guid _savedTicket;
    private bool _attachOnSave;
    private bool _editingDescription;
    private string _subject = "";
    private string _description = "";
    private string _state = "";
    private long _deadline;
    private long _nextTicketRefresh;
    private List<string> _incomingTickets;

    public Func<string, Task> Send { get; set; }
    public bool TicketTyping => _open && _tab != 0;

    private static Rectangle Tab(int index) => new(150 + index * 168, 133, 164, 28);
    private static Rectangle Category(int index) => new(150 + index * 168, 175, 164, 43);

    public bool ReceiveTicket(string line)
    {
        if (!line.StartsWith("Ticket> ", StringComparison.Ordinal))
        {
            return false;
        }

        var value = line[8..];
        if (value == "begin")
        {
            _incomingTickets = [];
        }
        else if (value.StartsWith("line ", StringComparison.Ordinal))
        {
            _incomingTickets?.Add(value[5..]);
        }
        else
        {
            if (value == "end" && _incomingTickets is { } incoming)
            {
                _tickets.Clear();
                _tickets.AddRange(incoming);
                _ticketPage = Math.Clamp(_ticketPage, 0, Math.Max(0, (_tickets.Count - 1) / 15));
            }

            _incomingTickets = null;
            var saved = value.StartsWith("saved ", StringComparison.Ordinal)
                && Guid.TryParseExact(value[6..], "N", out _savedTicket);
            _state = saved ? "saved" : value;
            _deadline = 0;
            if (_state == "saved")
            {
                _subject = "";
                _description = "";
                if (_attachOnSave && _savedTicket != Guid.Empty && _attachmentSite is { } site)
                {
                    DesktopCommunityLinks.Open(new Uri(site, $"/suporte/anexo/{_savedTicket:D}"));
                    _attachOnSave = false;
                }
            }
        }

        return true;
    }

    private const int DescriptionLimit = 1500;

    private async Task SubmitTicketAsync(string subject, string description)
    {
        _state = "waiting";
        _deadline = Environment.TickCount64 + 20000;
        if (Send is not { } send)
        {
            return;
        }

        try
        {
            await send("/ticketstart " + subject);
            for (var offset = 0; offset < description.Length; offset += 48)
            {
                var part = description.Substring(offset, Math.Min(48, description.Length - offset));
                await send("/ticketpart " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(part)));
            }
            await send("/ticketsave");
        }
        catch
        {
            // The HUD displays a timeout if the server cannot acknowledge the submission.
        }
    }

    public void UpdateTickets()
    {
        var now = Environment.TickCount64;
        if (_deadline != 0 && now >= _deadline)
        {
            _deadline = 0;
            _incomingTickets = null;
            _state = "error";
        }

        if (_open && _tab == 1 && now >= _nextTicketRefresh && _state != "waiting")
        {
            Request("/tickets");
        }
    }

    private void Request(string command)
    {
        _state = "waiting";
        _deadline = Environment.TickCount64 + 8000;
        _nextTicketRefresh = _deadline + 2000;
        _ = Send?.Invoke(command);
    }

    private bool ClickTickets(Point point)
    {
        for (var index = 0; index < 3; index++)
        {
            if (Tab(index).Contains(point))
            {
                _tab = index;
                if (_tab == 1 && _state != "waiting")
                {
                    Request("/tickets");
                }

                return true;
            }
        }

        if (_tab == 1)
        {
            var delta = Previous.Contains(point) ? -1 : Next.Contains(point) ? 1 : 0;
            _ticketPage = Math.Clamp(_ticketPage + delta, 0, Math.Max(0, (_tickets.Count - 1) / 15));
        }
        else if (_tab == 2)
        {
            for (var index = 0; index < 3; index++)
            {
                if (Category(index).Contains(point))
                {
                    _category = index;
                }
            }

            if (_attachmentSite is { } site && AttachmentButton.Contains(point) && _state != "waiting")
            {
                if (_savedTicket != Guid.Empty && _subject.Length == 0 && _description.Length == 0)
                {
                    DesktopCommunityLinks.Open(new Uri(site, $"/suporte/anexo/{_savedTicket:D}"));
                }
                else
                {
                    _attachOnSave = !_attachOnSave;
                }
                return true;
            }

            if (Subject.Contains(point) || Description.Contains(point))
            {
                _editingDescription = Description.Contains(point);
            }
            else if (Next.Contains(point) && _state != "waiting")
            {
                if (string.IsNullOrWhiteSpace(_subject) || string.IsNullOrWhiteSpace(_description))
                {
                    _state = "invalid";
                }
                else
                {
                    var category = _category == 0 ? "Bug" : _category == 1 ? "Melhoria" : "Outro";
                    _ = SubmitTicketAsync($"[{category}] {_subject}", _description);
                }
            }
        }

        return _tab != 0;
    }

    public void TypeTicket(char[] input, bool backspace)
    {
        if (_tab != 2 || _state == "waiting")
        {
            return;
        }

        ref var field = ref (_editingDescription ? ref _description : ref _subject);
        if (backspace && field.Length > 0)
        {
            field = field[..^1];
        }

        var limit = _editingDescription ? DescriptionLimit : 55;
        foreach (var character in input)
        {
            if (field.Length < limit && !char.IsControl(character) && !char.IsSurrogate(character) && character != '|')
            {
                field += character;
            }
        }
    }

    private bool DrawTickets(HudPainter painter, bool portuguese)
    {
        var tabs = portuguese ? new[] { "Novidades", "Meus tickets", "Novo ticket" } : new[] { "News", "My tickets", "New ticket" };
        for (var index = 0; index < tabs.Length; index++)
        {
            IconButton(Tab(index), tabs[index], index, _tab == index, false);
        }

        if (_tab == 0)
        {
            return false;
        }

        if (_tab == 1)
        {
            var y = 175;
            foreach (var line in _tickets.Skip(_ticketPage * 15).Take(15))
            {
                painter.Text(line, 150, y, Color.Wheat);
                y += 16;
            }

            if (_tickets.Count == 0 && _state == "end")
            {
                painter.Text(portuguese ? "Nenhum ticket." : "No tickets.", 150, 175, Color.Wheat);
            }

            Button(Previous, portuguese ? "Anterior" : "Previous", false);
            Button(Next, portuguese ? "Próxima" : "Next", false);
        }
        else
        {
            var categories = portuguese ? new[] { "Reportar bug", "Pedir melhoria", "Outro assunto" } : new[] { "Report bug", "Suggest improvement", "Other" };
            for (var index = 0; index < categories.Length; index++)
            {
                IconButton(Category(index), categories[index], index + 3, index == _category, true);
            }

            Button(Subject, _subject.Length == 0 ? (portuguese ? "Título" : "Subject") : _subject, !_editingDescription);
            painter.ColorBox(Description, 107, 79, 39, 255);
            var descriptionInterior = Description;
            descriptionInterior.Inflate(-1, -1);
            painter.ColorBox(descriptionInterior, 16, 13, 9, 255);
            painter.Text(portuguese ? "Descrição" : "Description", 158, 266, Color.Wheat);
            var first = Math.Max(0, (_description.Length - 1) / 48 - 4) * 48;
            for (var offset = first; offset < _description.Length; offset += 48)
            {
                painter.Text(_description.Substring(offset, Math.Min(48, _description.Length - offset)),
                    158, 286 + (offset - first) / 48 * 16, Color.Wheat);
            }

            DrawSupportButton(painter, Next, portuguese ? "Enviar ticket" : "Send ticket", primary: true);
        }

        if (_tab == 2)
        {
            if (_attachmentSite is not null)
            {
                var attachmentLabel = _attachOnSave
                    ? (portuguese ? "Escolher arquivo após enviar" : "Choose file after sending")
                    : (portuguese ? "Anexar imagem ou vídeo" : "Attach image or video");
                Button(AttachmentButton, attachmentLabel, _attachOnSave);
            }

            painter.TextCentered($"{_description.Length}/{DescriptionLimit}", 540, 650, 387, Color.Wheat);
        }

        var status = _state switch
        {
            "saved" => portuguese ? "Ticket enviado." : "Ticket sent.",
            "waiting" => portuguese ? "Aguardando servidor..." : "Waiting for server...",
            "invalid" => portuguese ? "Preencha título e descrição." : "Fill in subject and description.",
            "error" => portuguese ? "Suporte indisponível." : "Support unavailable.",
            _ => ""
        };
        painter.Text(status, 280, 438, Color.Wheat);
        return true;

        void IconButton(Rectangle bounds, string text, int icon, bool active, bool stacked)
        {
            DrawSupportButton(painter, bounds, "", active);
            var x = stacked ? bounds.Center.X - 6 : bounds.Left + 12;
            var y = stacked ? bounds.Top + 5 : bounds.Top + 7;
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

            painter.TextCentered(text, stacked ? bounds.Left : bounds.Left + 30, bounds.Right,
                stacked ? bounds.Top + 23 : bounds.Top + 7, active ? Color.White : Color.Wheat);
        }

        void Button(Rectangle bounds, string text, bool active) => DrawSupportButton(painter, bounds, text, active);
    }
}
