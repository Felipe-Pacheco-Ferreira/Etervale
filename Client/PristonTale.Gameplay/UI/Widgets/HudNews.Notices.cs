using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The mark on the News & Support plaque: a news post newer than the one the player saw, and tickets the staff answered
// that the player has not read. While the window is closed the feed and the ticket list are read again every two minutes;
// what the player sees is kept in the client settings (NewsSeen, TicketsSeen), so the mark survives a restart.
internal sealed partial class HudNews
{
    private const long NoticeCheckMs = 120_000;
    private long _nextNoticeCheck;
    private Task<NewsCard[]> _newsCheck;
    private string _newestNews = "";
    private bool _backgroundList;

    // How many things wait for the player: 1 for news, plus each answered ticket not read.
    public int Unread { get; private set; }

    // Returns the settings to save when what the player has seen changed, else null.
    public ClientSettings UpdateNotices(ClientSettings settings)
    {
        var now = Environment.TickCount64;
        if (now >= _nextNoticeCheck)
        {
            _nextNoticeCheck = now + NoticeCheckMs;
            if (!_open && _newsCheck is null && Uri.TryCreate(settings.NewsEndpoint, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps)
            {
                _newsCheck = Read(address);
            }

            if (!(_open && _tab == 1) && _state != "waiting" && Send is { } send)
            {
                _backgroundList = true;
                _ = send("/ticketlist");
            }
        }

        if (_newsCheck is { IsCompleted: true })
        {
            if (_newsCheck.IsCompletedSuccessfully && _newsCheck.Result is { Length: > 0 } fresh)
            {
                _newestNews = fresh[0].Key;
            }

            _newsCheck = null;
        }

        var changed = settings;
        if (_open && _tab == 0 && _request is { IsCompletedSuccessfully: true, Result: { Length: > 0 } shown })
        {
            _newestNews = shown[0].Key;
            if (settings.NewsSeen != _newestNews)
            {
                changed = changed with { NewsSeen = _newestNews };
            }
        }

        if (Viewing && _detail is { } detail && detail.Id == _openTicket && Answer(detail.Id) is { } read && Array.IndexOf(changed.TicketsSeen, read.SeenKey) < 0)
        {
            changed = changed with { TicketsSeen = AnsweredKeys(changed.TicketsSeen, read.SeenKey) };
        }

        var unread = _newestNews.Length > 0 && _newestNews != changed.NewsSeen ? 1 : 0;
        for (var index = 0; index < _ticketList.Count; index++)
        {
            if (_ticketList[index].Status == "Answered" && Array.IndexOf(changed.TicketsSeen, _ticketList[index].SeenKey) < 0)
            {
                unread++;
            }
        }

        Unread = unread;
        return ReferenceEquals(changed, settings) ? null : changed;
    }

    private TicketItem Answer(Guid id)
    {
        for (var index = 0; index < _ticketList.Count; index++)
        {
            if (_ticketList[index].Id == id && _ticketList[index].Status == "Answered")
            {
                return _ticketList[index];
            }
        }

        return null;
    }

    // The read keys of the tickets still answered in the list, and the one just read.
    private string[] AnsweredKeys(string[] seen, string read) =>
        seen.Where(key => _ticketList.Any(item => item.Status == "Answered" && item.SeenKey == key)).Append(read).ToArray();
}
