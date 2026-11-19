using System.Buffers;
using System.Text;
using System.Text.Json;

namespace PristonTale.Gameplay.UI.Widgets;

// The browser page draws this window as HTML, like its Settings (news.js): the game keeps the feed's read state and the
// tickets' conversation with the server, the page reads it (PageState) and acts on it (PageAction). While PageOpen is
// set the window draws nothing on the canvas and takes no input there.
internal sealed partial class HudNews
{
    internal static Action PageOpen { get; set; }

    private static bool PageDrawn => PageOpen is not null;

    // The page closed its window.
    internal void PageClosed()
    {
        _open = false;
        _askedLink = NoLink;
    }

    // What the page shows: the chosen tab, the request's state, the community links, the ticket list and the open ticket.
    internal string PageState()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("tab", _tab);
            json.WriteString("state", _state);
            json.WriteString("discord", _social[0]?.AbsoluteUri ?? "");
            json.WriteString("instagram", _social[1]?.AbsoluteUri ?? "");
            json.WriteString("x", _social[2]?.AbsoluteUri ?? "");
            json.WriteString("open", _openTicket == Guid.Empty ? "" : _openTicket.ToString("N"));
            json.WriteStartArray("list");
            foreach (var item in _ticketList)
            {
                json.WriteStartObject();
                json.WriteString("id", item.Id.ToString("N"));
                json.WriteString("number", item.Number);
                json.WriteString("status", item.Status);
                json.WriteString("category", item.Category);
                json.WriteString("subject", item.Subject);
                json.WriteString("details", item.Details);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (_detail is { } detail && detail.Id == _openTicket)
            {
                json.WriteStartObject("detail");
                json.WriteString("id", detail.Id.ToString("N"));
                json.WriteString("number", detail.Number);
                json.WriteString("status", detail.Status);
                json.WriteString("category", detail.Category);
                json.WriteString("subject", detail.Subject);
                json.WriteString("opened", detail.Opened);
                json.WriteBoolean("editable", detail.Editable);
                json.WriteStartArray("messages");
                foreach (var message in detail.Messages)
                {
                    json.WriteStartObject();
                    json.WriteBoolean("staff", message.Staff);
                    json.WriteString("header", message.Header);
                    json.WriteString("text", message.Text);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The page's actions on the tickets, as the canvas window's buttons do them; false when the game refuses one.
    internal bool PageAction(string action, string id, string subject, int category, string text)
    {
        var ticket = Guid.TryParseExact(id ?? "", "N", out var parsed) ? parsed : Guid.Empty;
        switch (action)
        {
            case "news":
                _tab = 0;
                return true;
            case "list":
                OpenTicket(Guid.Empty);
                return true;
            case "new":
                _tab = 2;
                _editingTicket = Guid.Empty;
                _state = "";
                return true;
            case "open":
                OpenTicket(ticket);
                return true;
            case "edit":
                _tab = 2;
                _editingTicket = ticket;
                _state = "";
                return true;
            case "cancel":
                if (ticket == Guid.Empty)
                {
                    return false;
                }

                Request("/ticketclose " + ticket.ToString("N"));
                return true;
            case "reply":
                if (ticket == Guid.Empty || string.IsNullOrWhiteSpace(text) || _state == "waiting")
                {
                    return false;
                }

                _ = SendTextAsync("/ticketreply " + ticket.ToString("N"), text.Trim());
                return true;
            case "send":
                return PageSend(subject, category, text);
            default:
                return false;
        }
    }

    private bool PageSend(string subject, int category, string text)
    {
        if (_state == "waiting" || category < 0 || category >= CategoryKeys.Length)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(text))
        {
            _state = "invalid";
            return false;
        }

        var trimmed = subject.Trim();
        var title = "[" + CategoryKeys[category] + "] " + (trimmed.Length > SubjectLimit ? trimmed[..SubjectLimit] : trimmed);
        var body = text.Length > DescriptionLimit ? text[..DescriptionLimit] : text;
        _ = _editingTicket == Guid.Empty
            ? SendTextAsync("/ticketstart " + title, body)
            : SendTextAsync("/ticketedit " + _editingTicket.ToString("N") + " " + title, body);
        return true;
    }
}
