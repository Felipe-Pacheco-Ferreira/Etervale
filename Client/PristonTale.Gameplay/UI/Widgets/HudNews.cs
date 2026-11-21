using System.Text.Json;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

internal sealed partial class HudNews
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Rectangle Box = new(90, 10, 620, 580);
    private static readonly Rectangle Close = new(678, 21, 24, 24);
    // The title bar: dragging it moves the window.
    private static readonly Rectangle Header = new(92, 12, 616, 44);
    // The news panel and, inside it, the posts' area: the kind's line on the left, then the text, the scroll bar right.
    private static readonly Rectangle Panel = new(102, 118, 600, 464);
    private static readonly Rectangle Feed = new(112, 126, 572, 448);
    private const int FeedTextX = 14;
    private const int WheelStep = 57;
    private const string NewSystemTitle = "Novo sistema";
    private static readonly Color WindowBorder = new(118, 92, 52);
    private static readonly Color WindowTop = new(37, 28, 20);
    private static readonly Color WindowBottom = new(19, 14, 10);
    private static readonly Color Rule = new(72, 56, 36);
    private static readonly Color TitleCream = new(243, 228, 196);
    private static readonly Color PanelBorder = new(78, 61, 40);
    private static readonly Color PanelTop = new(33, 25, 19);
    private static readonly Color PanelBottom = new(24, 18, 14);
    private static readonly Color PostTitle = new(240, 222, 186);
    private static readonly Color StampInk = new(160, 148, 128);
    private static readonly Color BodyInk = new(232, 222, 204);
    private static readonly Color BoldInk = Color.White;
    private static readonly Color BulletInk = new(214, 160, 62);
    // Each kind's badge and the line beside its post: Novidade gold, Evento purple, Melhoria green.
    private static readonly Color[] KindColors = [new(168, 120, 42), new(112, 58, 160), new(52, 132, 78)];
    private Task<NewsCard[]> _request;
    private bool _open;
    private Point _offset;
    private Point? _dragAnchor;
    private int _newsOffset;

    // A post as it arrives (its lines may hold **bold** spans and "- " bullets), its day as players read it and who signed
    // it, and, once the painter has measured it, its lines.
    private sealed record NewsCard(string Title, string Day, string Author, NewsKind Kind, string[] Lines)
    {
        public NewsLayout Layout { get; set; }

        public int Height => Layout?.Step ?? Lines.Length * BodyLine + 80;

        // "18/11/2026 · DEV Strong": the day and who signed the post.
        public string Stamp => Author.Length > 0 ? Day + " · " + Author : Day;

        // A development note of a new system, marked with the tools icon as on Discord.
        public bool NewSystem => Title.StartsWith(NewSystemTitle, StringComparison.OrdinalIgnoreCase);

        // What the player has seen of the feed: its newest post.
        public string Key => Stamp + "\n" + Title;
    }

    // The site's post kinds (the feed's "kind"): a new system or other news, an event, an improvement.
    internal enum NewsKind
    {
        News,
        Event,
        Improvement
    }

    private enum NewsRowKind
    {
        Title,
        Stamp,
        Body,
        Bullet
    }

    private readonly record struct NewsSpan(string Text, int X, bool Bold);

    private sealed record NewsRow(NewsRowKind Kind, NewsSpan[] Spans, int Y, float Scale);

    // A post's lines, the height its kind's line covers and the step to the next post.
    private sealed record NewsLayout(NewsRow[] Rows, int Height, int Step);

    public bool Captures(Point pointer) => _open && !PageDrawn && (_dragAnchor.HasValue || Box.Contains(pointer - _offset));

    public void UpdateDrag(HudLayout layout, Point pointer, bool pressed)
    {
        if (!pressed)
        {
            _dragAnchor = null;
        }

        var desired = _dragAnchor is { } anchor ? pointer - anchor : _offset;
        _offset = new Point(
            Math.Clamp(desired.X, -Box.Left, Math.Max(-Box.Left, layout.Width - Box.Right)),
            Math.Clamp(desired.Y, -Box.Top, Math.Max(-Box.Top, layout.Height - Box.Bottom)));
    }

    internal static string KindLabel(NewsKind kind) => ClientText.Get(KindKey(kind));

    internal static string KindLabel(NewsKind kind, string language) => ClientText.Get(KindKey(kind), language);

    private static string KindKey(NewsKind kind) => kind switch
    {
        NewsKind.Event => "News.Kind.Event",
        NewsKind.Improvement => "News.Kind.Improvement",
        _ => "News.Kind.News"
    };

    private readonly Uri[] _social = new Uri[3];
    private static readonly string[] SocialLabels = ["Discord", "Instagram", "X"];

    // Each network's own colors and mark (1 a pixel): Discord's blurple, Instagram's pink, X's grey on black.
    private static readonly Color[] SocialBorders = [new(88, 101, 242), new(225, 48, 108), new(120, 120, 120)];
    private static readonly Color[] SocialFills = [new(36, 38, 78), new(52, 18, 32), new(24, 24, 24)];
    private static readonly Color[] SocialInks = [new(196, 202, 255), new(255, 168, 200), Color.White];
    private static readonly string[][] SocialMarks =
    [
        ["..1111..", ".111111.", "11111111", "11.11.11", "11111111", "111..111", ".1....1."],
        ["111111.", "1.....1", "1.111.1", "1.1.1.1", "1.111.1", "1.....1", ".11111."],
        ["11...11", ".11.11.", "..111..", "...1...", "..111..", ".11.11.", "11...11"]
    ];

    private static readonly int[] SocialWidths = [66, 80, 40];

    private static Rectangle SocialBounds(int index)
    {
        var right = Close.Left - 10;
        for (var other = SocialWidths.Length - 1; other > index; other--)
        {
            right -= SocialWidths[other] + 8;
        }

        return new Rectangle(right - SocialWidths[index], 23, SocialWidths[index], 20);
    }

    public void Open(PristonTale.Engine3D.Rendering.ClientSettings settings)
    {
        _social[0] = DesktopCommunityLinks.Parse(settings.DiscordUrl);
        _social[1] = DesktopCommunityLinks.Parse(settings.InstagramUrl);
        _social[2] = DesktopCommunityLinks.Parse(settings.XUrl);
        _newsOffset = 0;
        _askedLink = NoLink;
        _open = true;
        _request = Uri.TryCreate(settings.NewsEndpoint, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps
            ? Read(address)
            : null;
        _tab = 0;
        PageOpen?.Invoke();
    }

    public bool Dismiss()
    {
        if (PageDrawn)
        {
            return false;
        }

        if (LinkPrompt)
        {
            _askedLink = NoLink;
            return true;
        }

        var wasOpen = _open;
        _open = false;
        return wasOpen;
    }

    public bool Click(Point pointer)
    {
        pointer -= _offset;
        if (!_open || PageDrawn || !Box.Contains(pointer))
        {
            return false;
        }

        if (LinkPrompt)
        {
            ClickLinkPrompt(pointer);
        }
        else if (Close.Contains(pointer))
        {
            _open = false;
        }
        else if (ClickSocial(pointer) || ClickTickets(pointer))
        {
            return true;
        }
        else if (Header.Contains(pointer))
        {
            _dragAnchor = pointer;
        }

        return true;
    }

    public bool Scroll(Point pointer, int wheel)
    {
        if (!_open || PageDrawn || !Box.Contains(pointer - _offset))
        {
            return false;
        }

        if (_tab != 0)
        {
            return ScrollTickets(wheel);
        }

        if (wheel != 0 && _request is { IsCompletedSuccessfully: true } && _request.Result is { } cards)
        {
            var maximum = Math.Max(0, cards.Sum(card => card.Height) - Feed.Height);
            _newsOffset = Math.Clamp(_newsOffset - Math.Sign(wheel) * WheelStep, 0, maximum);
        }

        return true;
    }

    public void Draw(HudPainter painter)
    {
        if (!_open || PageDrawn)
        {
            return;
        }

        painter.Shift(_offset);
        DrawWindow(painter);
        DrawLinkPrompt(painter);
        painter.Shift(Point.Zero);
    }

    private void DrawWindow(HudPainter painter)
    {
        painter.FramedBox(Box, WindowBorder, WindowTop, WindowBottom, 6);
        painter.Fill(new Rectangle(Box.X + 1, Header.Bottom, Box.Width - 2, 1), Rule);
        painter.Diamond(107, 33, 6, new Color(232, 186, 92));
        painter.Diamond(107, 33, 3, new Color(255, 232, 170));
        painter.Text(ClientText.Get("QuickMenu.News"), 122, 22, TitleCream, HudPainter.TextScale * 1.35f);
        painter.Text(ClientText.Get("QuickMenu.News"), 123, 22, TitleCream, HudPainter.TextScale * 1.35f);
        DrawCloseButton(painter);
        for (var index = 0; index < _social.Length; index++)
        {
            if (_social[index] is not null)
            {
                DrawSocialButton(painter, index);
            }
        }

        if (DrawTickets(painter))
        {
            return;
        }

        painter.FramedBox(Panel, PanelBorder, PanelTop, PanelBottom, 4);
        if (_request is not { IsCompletedSuccessfully: true } || _request.Result is null)
        {
            var status = _request is { IsCompleted: false }
                ? ClientText.Get("News.Loading")
                : ClientText.Get("News.Unavailable");
            painter.Text(status, Feed.X + FeedTextX, Feed.Y + 4, BodyInk);
            return;
        }

        var cards = _request.Result;
        if (cards.Length == 0)
        {
            painter.Text(ClientText.Get("News.None"), Feed.X + FeedTextX, Feed.Y + 4, BodyInk);
            return;
        }

        DrawNewsFeed(painter, cards);
    }

    private static void DrawCloseButton(HudPainter painter)
    {
        var center = Close.Center;
        painter.Disc(center.X, center.Y, Close.Width / 2, new Color(112, 96, 74));
        painter.Disc(center.X, center.Y, Close.Width / 2 - 1, new Color(30, 24, 18));
        painter.TextCentered("×", Close.Left, Close.Right, Close.Y + 4, new Color(226, 214, 192), false);
    }

    private void DrawSocialButton(HudPainter painter, int index)
    {
        var bounds = SocialBounds(index);
        painter.FramedBox(bounds, SocialBorders[index], SocialFills[index], 3);
        var mark = SocialMarks[index];
        var ink = SocialInks[index];
        var text = SocialLabels[index];
        var width = mark[0].Length + 5 + painter.MeasureAt(text, HudPainter.TextScale * 0.85f);
        var x = bounds.Center.X - width / 2;
        var y = bounds.Center.Y - mark.Length / 2;
        for (var row = 0; row < mark.Length; row++)
        {
            for (var column = 0; column < mark[row].Length; column++)
            {
                if (mark[row][column] == '1')
                {
                    painter.Fill(new Rectangle(x + column, y + row, 1, 1), ink);
                }
            }
        }

        var textX = x + mark[0].Length + 5;
        painter.Text(text, textX, bounds.Y + 4, ink, HudPainter.TextScale * 0.85f);
        painter.Text(text, textX + 1, bounds.Y + 4, ink, HudPainter.TextScale * 0.85f);
    }

    private void DrawNewsFeed(HudPainter painter, NewsCard[] cards)
    {
        var width = Feed.Width - FeedTextX - 14;
        var top = Feed.Y - _newsOffset;
        var total = 0;
        foreach (var card in cards)
        {
            card.Layout ??= LayoutCard(painter, card, width);
            var layout = card.Layout;
            if (top + layout.Height > Feed.Top && top < Feed.Bottom)
            {
                DrawPost(painter, card, layout, top);
            }

            top += layout.Step;
            total += layout.Step;
        }

        if (total > Feed.Height)
        {
            DrawFeedScrollBar(painter, total);
        }
    }

    private static void DrawPost(HudPainter painter, NewsCard card, NewsLayout layout, int top)
    {
        var accent = KindColors[(int)card.Kind];
        var lineTop = Math.Max(Feed.Top, top);
        var lineBottom = Math.Min(Feed.Bottom, top + layout.Height);
        painter.Fill(new Rectangle(Feed.X, lineTop, 2, lineBottom - lineTop), accent);
        var left = Feed.X + FeedTextX;
        var titleY = top + TitleTop;
        if (titleY >= Feed.Top && titleY + BadgeHeight <= Feed.Bottom)
        {
            var badge = new Rectangle(left, titleY + 1, BadgeWidth(painter, card.Kind), BadgeHeight);
            painter.RoundedFill(badge, accent, 8);
            var label = KindLabel(card.Kind);
            var labelX = badge.Center.X - painter.MeasureAt(label, BadgeScale) / 2;
            painter.Text(label, labelX, badge.Y + 3, Color.White, BadgeScale);
            painter.Text(label, labelX + 1, badge.Y + 3, Color.White, BadgeScale);
            if (card.NewSystem)
            {
                DrawToolsIcon(painter, badge.Right + BadgeGap, titleY + 2);
            }
        }

        foreach (var row in layout.Rows)
        {
            var y = top + row.Y;
            if (y < Feed.Top || y + BodyLine > Feed.Bottom)
            {
                continue;
            }

            if (row.Kind == NewsRowKind.Bullet)
            {
                painter.Fill(new Rectangle(left + 3, y + 7, 4, 4), BulletInk);
            }

            foreach (var span in row.Spans)
            {
                var ink = row.Kind switch
                {
                    NewsRowKind.Title => PostTitle,
                    NewsRowKind.Stamp => StampInk,
                    _ => span.Bold ? BoldInk : BodyInk
                };
                painter.Text(span.Text, left + span.X, y, ink, row.Scale);
                if (row.Kind != NewsRowKind.Stamp)
                {
                    painter.Text(span.Text, left + span.X + 1, y, ink, row.Scale);
                }
            }
        }
    }

    private void DrawFeedScrollBar(HudPainter painter, int total)
    {
        var x = Feed.Right + 4;
        var track = new Rectangle(x, Feed.Y + 12, 5, Feed.Height - 24);
        var arrow = new Color(150, 132, 104);
        for (var row = 0; row < 4; row++)
        {
            painter.Fill(new Rectangle(x + 2 - row, Feed.Y + 2 + row, row * 2 + 1, 1), arrow);
        }

        painter.DownTriangle(x + 2, Feed.Bottom - 2, 3, 4, arrow);
        painter.RoundedFill(track, new Color(44, 34, 24), 2);
        var thumb = Math.Max(24, track.Height * Feed.Height / total);
        var position = track.Y + (track.Height - thumb) * _newsOffset / Math.Max(1, total - Feed.Height);
        painter.RoundedFill(new Rectangle(track.X, position, track.Width, thumb), new Color(170, 136, 78), 2);
    }

    private static void DrawSupportButton(HudPainter painter, Rectangle bounds, string label, bool active = false, bool primary = false)
    {
        painter.ColorBox(bounds, active || primary ? 207 : 113, active || primary ? 160 : 84, 44, 255);
        bounds.Inflate(-1, -1);
        painter.ColorBox(bounds, primary ? 37 : active ? 64 : 40, primary ? 96 : active ? 49 : 32, primary ? 43 : 20, 255);
        painter.ColorBox(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), primary ? 106 : 105, primary ? 152 : 79, 48, 255);
        if (active)
        {
            painter.ColorBox(new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2), 222, 177, 83, 255);
        }

        painter.TextCentered(label, bounds.Left, bounds.Right, bounds.Y + 5, active || primary ? Color.White : Color.Wheat);
    }

    // "18/11/2026": the post's day as players read it.
    private static string NewsDay(JsonElement post)
    {
        var date = post.GetProperty("date").GetString() ?? "";
        return DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
            ? day.ToString("dd'/'MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : date;
    }

    // The feed's kind ("news", "event", "improvement"); a post without one, or with another, is news.
    internal static NewsKind ParseKind(string kind) => kind?.ToLowerInvariant() switch
    {
        "event" => NewsKind.Event,
        "improvement" => NewsKind.Improvement,
        _ => NewsKind.News
    };

    private static string Optional(JsonElement post, string name) =>
        post.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static async Task<NewsCard[]> Read(Uri address)
    {
        try
        {
            using var stream = await Http.GetStreamAsync(address);
            using var document = await JsonDocument.ParseAsync(stream);
            return document.RootElement.EnumerateArray().Take(20)
                .Select(post => new NewsCard(post.GetProperty("title").GetString() ?? "", NewsDay(post), Optional(post, "author"), ParseKind(Optional(post, "kind")),
                    post.GetProperty("body").EnumerateArray().Select(line => line.GetString() ?? "")
                        .Prepend(post.GetProperty("summary").GetString() ?? "")
                        .ToArray()))
                .ToArray();
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
            or InvalidOperationException or KeyNotFoundException or IOException)
        {
            return null;
        }
    }
}
