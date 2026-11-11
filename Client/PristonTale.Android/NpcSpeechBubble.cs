using Android.Content;
using Android.Graphics;
using Android.Widget;

namespace PristonTale.Mobile;

internal sealed class NpcSpeechBubble : TextView
{
    private readonly Bitmap[] _tiles;
    private readonly Rect _source = new();
    private readonly RectF _destination = new();
    private readonly float _density;

    public NpcSpeechBubble(Context context) : base(context)
    {
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _tiles = Enumerable.Range(0, 10).Select(index =>
        {
            using var stream = context.Assets!.Open($"ChatBox/{index}.png");
            return BitmapFactory.DecodeStream(stream) ?? throw new InvalidDataException("Invalid speech bubble tile.");
        }).ToArray();

        TextSize = 14f;
        SetTextColor(Color.White);
        SetPadding(Dp(9), Dp(7), Dp(9), Dp(13));
        SetMaxWidth(Dp(282));
        SetMaxLines(12);
    }

    protected override void OnDraw(Canvas canvas)
    {
        canvas.Save();
        canvas.Scale(_density, _density);
        var width = Math.Max(18, (int)(Width / _density));
        var bottom = Math.Max(4, (int)(Height / _density) - 10);
        var tail = (width - 12) / 2;

        Tile(4, 0, 0, 16, 16, 4, 4, width - 8, bottom - 4);
        Tile(0, 0, 0, 11, 4, 0, 0, 11, 4);
        Tile(1, 0, 0, 6, 4, 11, 0, width - 18, 4);
        Tile(2, 9, 0, 7, 4, width - 7, 0, 7, 4);
        Tile(3, 0, 0, 4, 16, 0, 4, 4, bottom - 4);
        Tile(5, 12, 0, 4, 16, width - 4, 4, 4, bottom - 4);
        Tile(6, 0, 4, 11, 2, 0, bottom, 11, 2);
        Tile(7, 0, 4, 6, 2, 11, bottom, tail - 11, 2);
        Tile(9, 0, 4, 12, 10, tail, bottom, 12, 10);
        Tile(7, 0, 4, 6, 2, tail + 12, bottom, width - tail - 19, 2);
        Tile(8, 9, 4, 7, 2, width - 7, bottom, 7, 2);
        canvas.Restore();
        base.OnDraw(canvas);

        void Tile(int index, int sx, int sy, int sw, int sh, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0)
            {
                return;
            }
            _source.Set(sx, sy, sx + sw, sy + sh);
            _destination.Set(x, y, x + w, y + h);
            canvas.DrawBitmap(_tiles[index], _source, _destination, null);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var tile in _tiles)
            {
                tile.Dispose();
            }
            _source.Dispose();
            _destination.Dispose();
        }
        base.Dispose(disposing);
    }

    private int Dp(int value) => (int)MathF.Round(value * _density);
}
