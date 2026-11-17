using System.Text.Json;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

internal sealed partial class HudNews
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Rectangle Box = new(130, 88, 540, 390);
    private static readonly Rectangle Close = new(624, 100, 32, 24);
    private static readonly Rectangle Previous = new(150, 434, 120, 26);
    private static readonly Rectangle Next = new(530, 434, 120, 26);
    private Task<NewsCard[]> _request;
    private bool _open;
    private int _newsOffset;
    private const int NewsVisibleLines = 17;
    private const int NewsLineHeight = 16;
    private sealed record NewsCard(string Title, string Date, string[] Lines)
    {
        public int Height => Lines.Length + 4;
    }

    public bool Captures(Point pointer) => _open && Box.Contains(pointer);

    private readonly Uri[] _social = new Uri[3];
    private static readonly string[] SocialLabels = ["Discord", "Instagram", "X"];

    private static Rectangle SocialBounds(int index) => new(408 + index * 68, 100, 64, 24);

    public void Open(PristonTale.Engine3D.Rendering.ClientSettings settings)
    {
        _attachmentSite = DesktopCommunityLinks.Parse(settings.NewsEndpoint);
        _social[0] = DesktopCommunityLinks.Parse(settings.DiscordUrl);
        _social[1] = DesktopCommunityLinks.Parse(settings.InstagramUrl);
        _social[2] = DesktopCommunityLinks.Parse(settings.XUrl);
        _newsOffset = 0;
        _open = true;
        _request = Uri.TryCreate(settings.NewsEndpoint, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps
            ? Read(address)
            : null;
        _tab = 0;
    }

    public bool Dismiss()
    {
        var wasOpen = _open;
        _open = false;
        return wasOpen;
    }

    public bool Click(Point pointer)
    {
        if (!Captures(pointer))
        {
            return false;
        }

        if (Close.Contains(pointer))
        {
            _open = false;
        }
        else if (ClickSocial(pointer) || ClickTickets(pointer))
        {
            return true;
        }
        return true;
    }

    public bool Scroll(Point pointer, int wheel)
    {
        if (!Captures(pointer) || _tab != 0)
        {
            return false;
        }

        if (wheel != 0 && _request is { IsCompletedSuccessfully: true } && _request.Result is { } cards)
        {
            var maximum = Math.Max(0, cards.Sum(card => card.Height) - NewsVisibleLines);
            _newsOffset = Math.Clamp(_newsOffset - Math.Sign(wheel) * 3, 0, maximum);
        }

        return true;
    }

    public void Draw(HudPainter painter)
    {
        if (!_open)
        {
            return;
        }

        var portuguese = ClientText.Language == "pt-BR";
        painter.FramedBox(Box, new Color(155, 115, 50), new Color(39, 32, 21), new Color(16, 13, 9), 4);
        painter.RoundedGradient(new Rectangle(132, 90, 536, 36), new Color(56, 45, 28), new Color(30, 24, 15), 3);
        painter.Fill(new Rectangle(132, 125, 536, 1), new Color(112, 85, 40));
        painter.Diamond(144, 108, 5, Color.Gold);
        painter.Text(portuguese ? "Novidades & Suporte" : "News & Support", 155, 101, Color.Wheat, HudPainter.TextScale * 1.18f);
        DrawSupportButton(painter, Close, "X");
        for (var index = 0; index < _social.Length; index++)
        {
            if (_social[index] is not null)
            {
                DrawSupportButton(painter, SocialBounds(index), SocialLabels[index]);
            }
        }
        if (DrawTickets(painter, portuguese))
        {
            return;
        }

        if (_request is not { IsCompletedSuccessfully: true } || _request.Result is null)
        {
            var status = _request is { IsCompleted: false }
                ? (portuguese ? "Carregando…" : "Loading…")
                : (portuguese ? "Feed de notícias indisponível." : "News feed unavailable.");
            painter.Text(status, 150, 175, Color.Wheat);
            return;
        }

        var cards = _request.Result;
        if (cards.Length == 0)
        {
            painter.Text(portuguese ? "Sem notícias." : "No news.", 150, 175, Color.Wheat);
            return;
        }

        DrawNewsFeed(painter, cards, portuguese);
    }

    private void DrawNewsFeed(HudPainter painter, NewsCard[] cards, bool portuguese)
    {
        const int left = 145;
        const int top = 171;
        const int width = 498;
        var end = _newsOffset + NewsVisibleLines;
        var start = 0;
        foreach (var card in cards)
        {
            var next = start + card.Height;
            if (next > _newsOffset && start < end)
            {
                var first = Math.Max(start, _newsOffset);
                var last = Math.Min(next, end);
                var box = new Rectangle(left, top + (first - _newsOffset) * NewsLineHeight, width, (last - first) * NewsLineHeight - 2);
                painter.FramedBox(box, new Color(102, 77, 39), new Color(42, 34, 22), new Color(24, 19, 13), 4);
                for (var row = first; row < last; row++)
                {
                    var local = row - start;
                    var y = top + (row - _newsOffset) * NewsLineHeight + 3;
                    if (local == 0)
                    {
                        var badge = new Rectangle(left + 10, y, 61, 14);
                        painter.RoundedFill(badge, new Color(114, 88, 43), 4);
                        painter.TextCentered(portuguese ? "Novidade" : "News", badge.Left, badge.Right, badge.Y, Color.White);
                        painter.Text(painter.Fit(card.Title, width - 92), left + 79, y, Color.Gold);
                    }
                    else if (local == 1)
                    {
                        painter.Text(card.Date, left + 10, y, new Color(186, 163, 120));
                    }
                    else if (local >= 3 && local - 3 < card.Lines.Length)
                    {
                        painter.Text(painter.Fit(card.Lines[local - 3], width - 20), left + 10, y, Color.Wheat);
                    }
                }
            }
            start = next;
            if (start >= end)
            {
                break;
            }
        }

        var total = cards.Sum(card => card.Height);
        if (total > NewsVisibleLines)
        {
            var track = new Rectangle(650, top, 4, NewsVisibleLines * NewsLineHeight);
            var thumb = Math.Max(20, track.Height * NewsVisibleLines / total);
            painter.RoundedFill(track, new Color(51, 39, 23));
            painter.RoundedFill(new Rectangle(track.X, track.Y + (track.Height - thumb) * _newsOffset / (total - NewsVisibleLines),
                track.Width, thumb), new Color(153, 115, 59));
        }
    }

    private bool ClickSocial(Point pointer)
    {
        for (var index = 0; index < _social.Length; index++)
        {
            if (_social[index] is { } address && SocialBounds(index).Contains(pointer))
            {
                DesktopCommunityLinks.Open(address);
                return true;
            }
        }

        return false;
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

    private static async Task<NewsCard[]> Read(Uri address)
    {
        try
        {
            using var stream = await Http.GetStreamAsync(address);
            using var document = await JsonDocument.ParseAsync(stream);
            return document.RootElement.EnumerateArray().Take(20)
                .Select(post => new NewsCard(post.GetProperty("title").GetString() ?? "", post.GetProperty("date").GetString() ?? "",
                    post.GetProperty("body").EnumerateArray().Select(line => line.GetString() ?? "")
                        .Prepend(post.GetProperty("summary").GetString() ?? "")
                        .SelectMany(line => line.Length == 0 ? new[] { "" } : Enumerable.Range(0, (line.Length + 59) / 60)
                            .Select(index => line.Substring(index * 60, Math.Min(60, line.Length - index * 60))))
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
