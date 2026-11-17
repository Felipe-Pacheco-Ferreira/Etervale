using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

internal sealed class HudMail
{
    private static readonly Rectangle Panel = new(180, 105, 440, 345);
    private static readonly Rectangle Title = new(180, 105, 440, 43);
    private static readonly Rectangle Close = new(574, 116, 30, 24);
    private static readonly Rectangle Back = new(198, 411, 106, 25);
    private static readonly Rectangle Previous = new(393, 411, 101, 25);
    private static readonly Rectangle Next = new(500, 411, 104, 25);
    private static readonly Rectangle Claim = new(307, 411, 82, 25);
    private readonly List<string> _lines = [];
    private readonly List<(string Key, int Quantity)> _items = [];
    private long _gold;
    private long _id;
    private int _page;
    private int _pages;
    private Point _offset;
    private Point? _dragAnchor;
    private long _requestedAt;
    private long _nextInboxRefresh;
    private bool _distributor;
    private string InboxCommand => _distributor ? "/distribuidor" : "/correio";
    private bool _claimed;
    private bool _claiming;
    private long _claimAt;
    private string _claimStatus = "";

    private bool CanClaim => _id != 0 && !_claimed && (_items.Count > 0 || _gold > 0);

    public bool IsOpen { get; private set; }
    public bool Captures(Point pointer) => IsOpen && (_dragAnchor.HasValue || Panel.Contains(pointer - _offset));

    public void RefreshInbox(HudChat chat)
    {
        var now = Environment.TickCount64;
        if (_claiming && now - _claimAt >= 6000)
        {
            _claiming = false;
            _claimStatus = "Sem resposta. Tente novamente.";
        }
        if (IsOpen && _id == 0 && now >= _nextInboxRefresh && !_dragAnchor.HasValue)
        {
            _nextInboxRefresh = now + 5000;
            _ = chat.Send?.Invoke(InboxCommand, false);
        }
    }

    public void UpdateDrag(HudLayout layout, Point pointer, bool pressed)
    {
        if (!pressed)
        {
            _dragAnchor = null;
        }

        var desired = _dragAnchor is { } anchor ? pointer - anchor : _offset;
        _offset = new Point(
            Math.Clamp(desired.X, -Panel.Left, Math.Max(-Panel.Left, layout.Width - Panel.Right)),
            Math.Clamp(desired.Y, -Panel.Top, Math.Max(-Panel.Top, layout.Height - Panel.Bottom)));
    }

    public void Open(HudChat chat, bool distributor = false)
    {
        if (!IsOpen)
        {
            chat.ServerLine += Receive;
        }

        IsOpen = true;
        _id = 0;
        _distributor = distributor;
        Request(chat, InboxCommand);
        _nextInboxRefresh = _requestedAt + 5000;
    }

    private void Request(HudChat chat, string command)
    {
        _lines.Clear();
        _items.Clear();
        _gold = 0;
        _pages = 0;
        _claimed = false;
        _claiming = false;
        _claimStatus = "";
        _requestedAt = Environment.TickCount64;
        _ = chat.Send?.Invoke(command, false);
    }

    private bool Receive(string text)
    {
        if (text.StartsWith("Correio resgate> ", StringComparison.Ordinal))
        {
            var parts = text[17..].Split(' ', 2);
            if (parts.Length == 2 && long.TryParse(parts[0], out var id) && id == _id)
            {
                _claiming = false;
                _claimed = parts[1] is "ok" or "already";
                _claimStatus = parts[1] switch
                {
                    "ok" => "Recompensa recebida.",
                    "already" => "Esta recompensa já foi resgatada.",
                    "space" => "A recompensa não cabe no inventário.",
                    "unavailable" => "Recompensa indisponível.",
                    _ => "Tente novamente em instantes."
                };
            }

            return true;
        }

        if (text.StartsWith("Correio estado> ", StringComparison.Ordinal))
        {
            if (_id != 0)
            {
                _claimed = text.EndsWith("claimed", StringComparison.Ordinal);
            }
            return true;
        }

        if (text.StartsWith("Correio item> ", StringComparison.Ordinal))
        {
            var parts = text[14..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (_id != 0 && _items.Count < 10 && parts.Length == 2 && int.TryParse(parts[1], out var quantity))
            {
                _items.Add((parts[0], quantity));
            }

            return true;
        }

        if (text.StartsWith("Correio ouro> ", StringComparison.Ordinal))
        {
            if (_id != 0)
            {
                long.TryParse(text.AsSpan(14), out _gold);
            }

            return true;
        }

        if (text.StartsWith("Correio> ", StringComparison.Ordinal))
        {
            if (_id != 0)
            {
                _lines.Add(text[9..]);
            }
            return true;
        }

        if (!text.StartsWith("Correio", StringComparison.Ordinal))
        {
            return false;
        }

        if (_id != 0 && text.StartsWith($"Correio #{_id} (", StringComparison.Ordinal))
        {
            var end = text.IndexOf(')');
            var slash = text.IndexOf('/');
            if (slash > 0 && end > slash)
            {
                int.TryParse(text.AsSpan(slash + 1, end - slash - 1), out _pages);
            }
        }

        if (_id == 0)
        {
            if (InboxId(text) is { } id)
            {
                _lines.RemoveAll(line => !line.StartsWith("Correio #", StringComparison.Ordinal));
                if (!_lines.Contains(text))
                {
                    var index = _lines.FindIndex(line => InboxId(line) is { } other && other < id);
                    _lines.Insert(index < 0 ? _lines.Count : index, text);
                    if (_lines.Count > 10)
                    {
                        _lines.RemoveAt(10);
                    }
                }
            }
            else if (_lines.Count == 0)
            {
                _lines.Add(text);
            }
        }
        else
        {
            _lines.Add(text);
        }
        return true;
    }

    private static long? InboxId(string text)
    {
        var end = text.IndexOf(':');
        return end > 9 && text.StartsWith("Correio #", StringComparison.Ordinal) &&
            long.TryParse(text.AsSpan(9, end - 9), out var id) ? id : null;
    }

    public bool Click(Point pointer, HudChat chat)
    {
        pointer -= _offset;
        if (!IsOpen || !Panel.Contains(pointer))
        {
            return false;
        }

        if (Close.Contains(pointer))
        {
            IsOpen = false;
            chat.ServerLine -= Receive;
        }
        else if (Title.Contains(pointer))
        {
            _dragAnchor = pointer;
        }
        else if (CanClaim && !_claiming && Claim.Contains(pointer))
        {
            _claiming = true;
            _claimAt = Environment.TickCount64;
            _claimStatus = "";
            _ = chat.Send?.Invoke($"/resgatarcorreio {_id}", false);
        }
        else if (_id != 0 && Back.Contains(pointer))
        {
            Open(chat, _distributor);
        }
        else if (_id != 0 && _page > 1 && Previous.Contains(pointer))
        {
            Request(chat, $"/correio {_id} {--_page}");
        }
        else if (_id != 0 && _page < _pages && Next.Contains(pointer))
        {
            Request(chat, $"/correio {_id} {++_page}");
        }
        else if (_id == 0 && pointer.Y >= 157)
        {
            var row = (pointer.Y - 157) / 23;
            if (row < _lines.Count && _lines[row].StartsWith("Correio #", StringComparison.Ordinal))
            {
                var end = _lines[row].IndexOf(':');
                if (end > 9 && long.TryParse(_lines[row].AsSpan(9, end - 9), out var selected))
                {
                    _id = selected;
                    _page = 1;
                    Request(chat, $"/correio {_id}");
                }
            }
        }

        return true;
    }

    public void Draw(HudPainter painter, HudItemIcons icons, Point pointer)
    {
        if (!IsOpen)
        {
            return;
        }

        pointer -= _offset;
        RoundBox(Panel, 12, 165, 125, 60);
        var inside = Panel;
        inside.Inflate(-1, -1);
        RoundBox(inside, 11, 25, 27, 32);
        Box(new Rectangle(198, 146, 404, 1), 165, 125, 60, 255);
        Text(_distributor ? "Distribuidor de Itens" : _id == 0 ? "Correios · Caixa de entrada" : "Correios · Mensagem", 198, 121);
        RoundBox(Close, 11, 158, 116, 56);
        var closeFace = Close;
        closeFace.Inflate(-1, -1);
        RoundBox(closeFace, 10, 43, 37, 31);
        Text("X", 584, 121);
        if (_lines.Count == 0)
        {
            Text(Environment.TickCount64 - _requestedAt < 6000 ? "Carregando…" : "Sem resposta do servidor.", 199, 160);
        }

        var y = 157;
        foreach (var line in _lines)
        {
            if (_id == 0)
            {
                var row = new Rectangle(196, y, 406, 22);
                Box(row, 50, 42, 32, row.Contains(pointer) ? 255 : 130);
                var separator = line.IndexOf(": ", StringComparison.Ordinal);
                var subject = line.StartsWith("Correio #", StringComparison.Ordinal) && separator >= 0 ? line[(separator + 2)..] : line;
                Text(painter.Fit(subject, 396), 202, y + 3);
                y += 23;
                continue;
            }

            foreach (var wrapped in HudTextLayout.Wrap(painter.Font, line, 400, HudPainter.TextScale))
            {
                if (y >= (_items.Count == 0 && _gold == 0 ? 401 : 338))
                {
                    break;
                }

                Text(wrapped, 200, y);
                y += Math.Max(12, HudPainter.LineHeight);
            }
        }

        if (_id == 0)
        {
            Text("Atualização automática", 198, 417);
            Text($"{_lines.Count(line => line.StartsWith("Correio #", StringComparison.Ordinal))} mensagens", 490, 417);
        }
        else
        {
            if (_items.Count > 0 || _gold > 0)
            {
                Text(_gold > 0 ? $"Anexos · {_gold} ouro" : "Anexos", 198, 344);
                var status = _claimStatus.Length > 0 ? _claimStatus : _claimed ? "Recompensa resgatada" : "";
                Text(painter.Fit(status, 255), 345, 344);
                icons.Request(_items.Select(item => item.Key));
                for (var index = 0; index < _items.Count; index++)
                {
                    var item = _items[index];
                    var slot = new Rectangle(198 + 40 * index, 366, 36, 36);
                    Box(slot, 66, 52, 34, 255);
                    var destination = slot;
                    destination.Inflate(-6, -6);
                    destination.Offset(_offset.X, _offset.Y);
                    painter.Image(icons.Find(item.Key), destination);
                    Text(item.Quantity.ToString(), slot.Right - 13, slot.Bottom - 12);
                }
            }

            Button(Back, "Caixa de entrada");
            if (CanClaim)
            {
                Button(Claim, _claiming ? "Aguarde…" : "Resgatar");
            }
            if (_page > 1)
            {
                Button(Previous, "Anterior");
            }

            if (_page < _pages)
            {
                Button(Next, "Próxima");
            }
        }

        void Box(Rectangle bounds, int red, int green, int blue, int alpha)
        {
            bounds.Offset(_offset.X, _offset.Y);
            painter.ColorBox(bounds, red, green, blue, alpha);
        }

        void RoundBox(Rectangle bounds, int radius, int red, int green, int blue)
        {
            Box(new Rectangle(bounds.X, bounds.Y + radius, bounds.Width, bounds.Height - 2 * radius), red, green, blue, 255);
            for (var row = 0; row < radius; row++)
            {
                var distance = radius - row - .5;
                var inset = radius - (int)Math.Sqrt(radius * radius - distance * distance);
                Box(new Rectangle(bounds.X + inset, bounds.Y + row, bounds.Width - 2 * inset, 1), red, green, blue, 255);
                Box(new Rectangle(bounds.X + inset, bounds.Bottom - row - 1, bounds.Width - 2 * inset, 1), red, green, blue, 255);
            }
        }

        void Text(string text, int x, int y) => painter.Text(text, x + _offset.X, y + _offset.Y, Color.Wheat);

        void Button(Rectangle bounds, string label)
        {
            Box(bounds, 158, 116, 56, 255);
            bounds.Inflate(-1, -1);
            Box(bounds, bounds.Contains(pointer) ? 83 : 53, 44, 30, 255);
            Text(label, bounds.X + 7, bounds.Y + 4);
        }
    }
}
