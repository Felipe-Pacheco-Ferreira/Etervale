using Android.Graphics;
using Android.Graphics.Drawables;

namespace PristonTale.Mobile;

internal enum HudGlyph { Attack, Potion, Skill, Journal, Chat, Run, Auto, Exit, Menu, Bag }

internal sealed class HudIcon(HudGlyph glyph, int size) : Drawable
{
    private readonly Paint _paint = new() { AntiAlias = true, StrokeWidth = 1.6f, StrokeCap = Paint.Cap.Round, StrokeJoin = Paint.Join.Round };
    private readonly global::Android.Graphics.Path _path = new();
    private int _alpha = 255;

    public override int IntrinsicWidth => size;
    public override int IntrinsicHeight => size;

    public override void Draw(Canvas canvas)
    {
        var saved = canvas.Save();
        canvas.Translate(Bounds.Left, Bounds.Top);
        canvas.Scale(Bounds.Width() / 24f, Bounds.Height() / 24f);
        _paint.Color = MobileHudTheme.Gold;
        _paint.Alpha = _alpha;
        _paint.SetStyle(Paint.Style.Stroke);
        switch (glyph)
        {
            case HudGlyph.Attack:
                Line(5, 19, 19, 5, 21, 3, 21, 9, 11, 19);
                Line(5, 14, 10, 19, 7, 22);
                Line(6, 17, 3, 20);
                break;
            case HudGlyph.Potion:
                Line(9, 4, 15, 4, 15, 9, 19, 14, 18, 20, 6, 20, 5, 14, 9, 9, 9, 4);
                Line(6, 14, 18, 14);
                break;
            case HudGlyph.Skill:
                Line(12, 2, 14, 9, 22, 12, 14, 14, 12, 22, 10, 14, 2, 12, 10, 9, 12, 2);
                break;
            case HudGlyph.Journal:
                Line(4, 4, 18, 4, 18, 19, 5, 19, 4, 17, 4, 4);
                Line(8, 8, 15, 8);
                Line(8, 12, 15, 12);
                break;
            case HudGlyph.Chat:
                Line(3, 5, 21, 5, 21, 16, 12, 16, 7, 20, 7, 16, 3, 16, 3, 5);
                Line(7, 10, 17, 10);
                break;
            case HudGlyph.Run:
                canvas.DrawCircle(15, 4, 2, _paint);
                Line(5, 9, 10, 7, 14, 8, 17, 12, 21, 10);
                Line(14, 8, 10, 14, 15, 16, 13, 21);
                Line(10, 14, 7, 18, 3, 19);
                break;
            case HudGlyph.Auto:
                canvas.DrawCircle(12, 12, 8, _paint);
                Line(12, 3, 12, 8);
                Line(12, 16, 12, 21);
                Line(3, 12, 8, 12);
                Line(16, 12, 21, 12);
                break;
            case HudGlyph.Menu:
                Line(4, 6, 20, 6);
                Line(4, 12, 20, 12);
                Line(4, 18, 20, 18);
                break;
            case HudGlyph.Bag:
                // A drawstring pouch: the gathered neck, the tie, the round body and its clasp.
                Line(9, 8, 8, 4, 16, 4, 15, 8);
                Line(7, 8, 17, 8);
                _path.Reset();
                _path.MoveTo(9, 8);
                _path.CubicTo(3, 11, 3, 21, 12, 21);
                _path.CubicTo(21, 21, 21, 11, 15, 8);
                canvas.DrawPath(_path, _paint);
                canvas.DrawCircle(12, 14.5f, 2f, _paint);
                break;
            case HudGlyph.Exit:
                Line(11, 4, 5, 4, 5, 20, 11, 20);
                Line(10, 12, 21, 12, 17, 8);
                Line(21, 12, 17, 16);
                break;
        }
        canvas.RestoreToCount(saved);

        void Line(params float[] coordinates)
        {
            _path.Reset();
            _path.MoveTo(coordinates[0], coordinates[1]);
            for (var index = 2; index < coordinates.Length; index += 2)
            {
                _path.LineTo(coordinates[index], coordinates[index + 1]);
            }
            canvas.DrawPath(_path, _paint);
        }
    }

    public override void SetAlpha(int alpha)
    {
        _alpha = alpha;
        InvalidateSelf();
    }

    public override void SetColorFilter(ColorFilter? colorFilter) => _paint.SetColorFilter(colorFilter);
    public override int Opacity => (int)Format.Translucent;
}
