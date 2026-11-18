using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

// Original pinned-post art for Priston Avalon: a dusk panorama drawn from scratch (sky, sun, mountain ranges, mist,
// embers), with the shield logo, the title set in type and the call to play. Three 1080x1350 posts plus a grid preview.
public static class AvalonPinned
{
    const int TileW = 1080, TileH = 1350, W = TileW * 3, H = TileH;
    static readonly Random Rng = new Random(7);

    public static void Run(string emblemPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        using var emblem = new Bitmap(emblemPath);
        using var pano = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Hq(pano))
        {
            Sky(g);
            Sun(g, 1620, 860);
            Stars(g);
            Ranges(g);
            Embers(g);
            Vignette(g);
            Emblem(g, emblem);
            Title(g);
            Call(g);
        }

        pano.Save(Path.Combine(outDir, "panorama-3240x1350.png"), ImageFormat.Png);
        for (var i = 0; i < 3; i++)
        {
            using var tile = pano.Clone(new Rectangle(i * TileW, 0, TileW, TileH), PixelFormat.Format24bppRgb);
            tile.Save(Path.Combine(outDir, $"post-{i + 1}-de-3.png"), ImageFormat.Png);
        }

        var cut = TileH * 3 / 4;
        var side = (TileW - cut) / 2;
        var gap = 12;
        using var grid = new Bitmap(cut * 3 + gap * 2, TileH, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(grid))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (var i = 0; i < 3; i++)
            {
                g.DrawImage(pano, new Rectangle(i * (cut + gap), 0, cut, TileH), new Rectangle(i * TileW + side, 0, cut, TileH), GraphicsUnit.Pixel);
            }
        }

        grid.Save(Path.Combine(outDir, "previa-grid.png"), ImageFormat.Png);
    }

    static Graphics Hq(Image image)
    {
        var g = Graphics.FromImage(image);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        return g;
    }

    static void Sky(Graphics g)
    {
        using var brush = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.Black, Color.Black, 90f);
        brush.InterpolationColors = new ColorBlend
        {
            Colors = new[]
            {
                Color.FromArgb(8, 10, 26), Color.FromArgb(22, 20, 52), Color.FromArgb(66, 34, 74), Color.FromArgb(150, 62, 70),
                Color.FromArgb(226, 128, 70), Color.FromArgb(120, 52, 54), Color.FromArgb(14, 12, 20)
            },
            Positions = new[] { 0f, 0.22f, 0.42f, 0.55f, 0.63f, 0.72f, 1f }
        };
        g.FillRectangle(brush, 0, 0, W, H);
    }

    static void Sun(Graphics g, int cx, int cy)
    {
        foreach (var (radius, alpha, color) in new[]
        {
            (1500, 60, Color.FromArgb(240, 140, 70)), (900, 70, Color.FromArgb(250, 170, 90)), (480, 90, Color.FromArgb(255, 205, 130)),
            (220, 150, Color.FromArgb(255, 228, 170)), (110, 255, Color.FromArgb(255, 244, 214))
        })
        {
            using var path = new GraphicsPath();
            path.AddEllipse(cx - radius, cy - radius * 0.62f, radius * 2, radius * 1.24f);
            using var brush = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(alpha, color),
                SurroundColors = new[] { Color.FromArgb(0, color) }
            };
            g.FillPath(brush, path);
        }
    }

    static void Stars(Graphics g)
    {
        for (var i = 0; i < 420; i++)
        {
            var x = (float)(Rng.NextDouble() * W);
            var y = (float)(Math.Pow(Rng.NextDouble(), 1.8) * H * 0.45);
            var r = (float)(0.6 + Rng.NextDouble() * 1.6);
            var a = (int)(60 + Rng.NextDouble() * 160 * (1 - y / (H * 0.45)));
            using var b = new SolidBrush(Color.FromArgb(Math.Clamp(a, 0, 255), 240, 232, 214));
            g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        }
    }

    // Value noise smoothed with cosine steps, summed over octaves: the mountain ridges.
    static double Noise(double x, int seed)
    {
        var i = (int)Math.Floor(x);
        var f = x - i;
        double Hash(int n)
        {
            n = (n << 13) ^ n ^ seed * 7919;
            return 1.0 - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0;
        }

        var t = (1 - Math.Cos(f * Math.PI)) / 2;
        return Hash(i) * (1 - t) + Hash(i + 1) * t;
    }

    static double Ridge(double x, int seed, double scale)
    {
        double sum = 0, amp = 1, freq = 1 / scale, norm = 0;
        for (var o = 0; o < 6; o++)
        {
            sum += amp * (1 - Math.Abs(Noise(x * freq, seed + o)));
            norm += amp;
            amp *= 0.5;
            freq *= 2.1;
        }

        return sum / norm;
    }

    static void Ranges(Graphics g)
    {
        var layers = new[]
        {
            (Base: 880, Height: 300, Scale: 520.0, Top: Color.FromArgb(150, 86, 82, 120), Bottom: Color.FromArgb(150, 60, 50, 84), Seed: 11),
            (Base: 960, Height: 330, Scale: 430.0, Top: Color.FromArgb(220, 58, 46, 78), Bottom: Color.FromArgb(220, 36, 30, 52), Seed: 23),
            (Base: 1060, Height: 300, Scale: 360.0, Top: Color.FromArgb(240, 34, 30, 50), Bottom: Color.FromArgb(240, 20, 18, 32), Seed: 37),
            (Base: 1180, Height: 280, Scale: 300.0, Top: Color.FromArgb(255, 18, 18, 30), Bottom: Color.FromArgb(255, 10, 10, 18), Seed: 53)
        };
        for (var l = 0; l < layers.Length; l++)
        {
            var layer = layers[l];
            var points = new List<PointF> { new PointF(0, H) };
            for (var x = 0; x <= W; x += 6)
            {
                var y = layer.Base - Ridge(x, layer.Seed, layer.Scale) * layer.Height;
                points.Add(new PointF(x, (float)y));
            }

            points.Add(new PointF(W, H));
            using var brush = new LinearGradientBrush(new Rectangle(0, layer.Base - layer.Height, W, H - (layer.Base - layer.Height)), layer.Top, layer.Bottom, 90f);
            g.FillPolygon(brush, points.ToArray());

            // Mist settling in front of each range.
            using var mist = new LinearGradientBrush(new Rectangle(0, layer.Base - 140, W, 240), Color.FromArgb(0, 230, 190, 200),
                Color.FromArgb(0, 230, 190, 200), 90f);
            mist.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(0, 210, 170, 190), Color.FromArgb(l == 0 ? 70 : 46, 210, 170, 190), Color.FromArgb(0, 210, 170, 190) },
                Positions = new[] { 0f, 0.55f, 1f }
            };
            g.FillRectangle(mist, 0, layer.Base - 140, W, 240);
        }
    }

    static void Embers(Graphics g)
    {
        for (var i = 0; i < 260; i++)
        {
            var x = (float)(Rng.NextDouble() * W);
            var y = (float)(H * (0.5 + Rng.NextDouble() * 0.5));
            var r = (float)(1 + Rng.NextDouble() * 3.2);
            var a = (int)(70 + Rng.NextDouble() * 170);
            using var glow = new SolidBrush(Color.FromArgb(a / 4, 255, 170, 80));
            g.FillEllipse(glow, x - r * 3, y - r * 3, r * 6, r * 6);
            using var core = new SolidBrush(Color.FromArgb(a, 255, 214, 140));
            g.FillEllipse(core, x - r, y - r, r * 2, r * 2);
        }
    }

    static void Vignette(Graphics g)
    {
        using var top = new LinearGradientBrush(new Rectangle(0, 0, W, 300), Color.FromArgb(150, 4, 4, 12), Color.FromArgb(0, 4, 4, 12), 90f);
        g.FillRectangle(top, 0, 0, W, 300);
        using var bottom = new LinearGradientBrush(new Rectangle(0, H - 360, W, 360), Color.FromArgb(0, 4, 4, 10), Color.FromArgb(200, 4, 4, 10), 90f);
        g.FillRectangle(bottom, 0, H - 360, W, 360);
    }

    // Post 1: the shield, its square background faded away.
    static void Emblem(Graphics g, Bitmap emblem)
    {
        var size = 820;
        using var round = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var gr = Hq(round))
        {
            gr.DrawImage(emblem, 0, 0, size, size);
        }

        var c = size / 2.0;
        Alpha(round, (x, y) =>
        {
            var r = Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            var t = Math.Clamp((0.92 - r) / 0.3, 0, 1);
            return t * t * (3 - 2 * t);
        });
        g.DrawImage(round, (TileW - size) / 2, 230, size, size);
        Spaced(g, "MMORPG DE FANTASIA", TileW / 2, 1090, 34, false, Color.FromArgb(225, 214, 182, 118), 0.32f);
    }

    // Post 2: the title in type, with an ornament and the line under it.
    static void Title(Graphics g)
    {
        var cx = TileW + TileW / 2;
        Spaced(g, "PRISTON", cx, 300, 96, true, Color.Empty, 0.22f);
        Spaced(g, "AVALON", cx, 410, 190, true, Color.Empty, 0.08f);
        Ornament(g, cx, 668);
        Spaced(g, "A LENDA CONTINUA", cx, 720, 40, false, Color.FromArgb(236, 225, 203), 0.34f);
    }

    // Post 3: the call to play and a button-like line.
    static void Call(Graphics g)
    {
        var cx = 2 * TileW + TileW / 2;
        Spaced(g, "JOGUE", cx, 330, 130, true, Color.Empty, 0.16f);
        Spaced(g, "GRÁTIS", cx, 475, 130, true, Color.Empty, 0.16f);
        Ornament(g, cx, 690);
        Spaced(g, "NO NAVEGADOR", cx, 740, 50, false, Color.FromArgb(236, 225, 203), 0.2f);
        Spaced(g, "E NO ANDROID", cx, 806, 50, false, Color.FromArgb(236, 225, 203), 0.2f);
        var button = new RectangleF(cx - 270, 940, 540, 104);
        using (var path = RoundRect(button, 10))
        {
            using var fill = new LinearGradientBrush(button, Color.FromArgb(250, 228, 170), Color.FromArgb(196, 148, 72), 90f);
            g.FillPath(fill, path);
            using var edge = new Pen(Color.FromArgb(255, 246, 214), 2f);
            g.DrawPath(edge, path);
        }

        Spaced(g, "CRIE SUA CONTA", cx, 970, 42, true, Color.FromArgb(52, 34, 12), 0.22f, plain: true);
    }

    static void Ornament(Graphics g, int cx, int y)
    {
        using var line = new LinearGradientBrush(new Rectangle(cx - 330, y - 2, 660, 4), Color.Black, Color.Black, 0f);
        line.InterpolationColors = new ColorBlend
        {
            Colors = new[] { Color.FromArgb(0, 232, 199, 129), Color.FromArgb(255, 232, 199, 129), Color.FromArgb(0, 232, 199, 129) },
            Positions = new[] { 0f, 0.5f, 1f }
        };
        g.FillRectangle(line, cx - 330, y - 1, 660, 3);
        using var diamond = new GraphicsPath();
        diamond.AddPolygon(new[] { new PointF(cx, y - 16), new PointF(cx + 16, y), new PointF(cx, y + 16), new PointF(cx - 16, y) });
        using var dark = new SolidBrush(Color.FromArgb(20, 14, 24));
        g.FillPath(dark, diamond);
        using var pen = new Pen(Color.FromArgb(232, 199, 129), 3f);
        g.DrawPath(pen, diamond);
        using var dot = new SolidBrush(Color.FromArgb(232, 199, 129));
        g.FillEllipse(dot, cx - 5, y - 5, 10, 10);
    }

    // Text centred at cx from top y, letters spaced by `tracking` of the size; gold when display (gradient, dark
    // outline and shadow), else the given colour with a soft shadow.
    static void Spaced(Graphics g, string text, int cx, int y, float size, bool display, Color color, float tracking, bool plain = false)
    {
        using var family = new FontFamily("Palatino Linotype");
        var style = display ? FontStyle.Bold : FontStyle.Regular;
        using var format = new StringFormat(StringFormat.GenericTypographic);
        var widths = new List<float>();
        var total = 0f;
        using (var font = new Font(family, size, style, GraphicsUnit.Pixel))
        {
            foreach (var ch in text)
            {
                var w = ch == ' ' ? size * 0.32f : g.MeasureString(ch.ToString(), font, 10000, format).Width;
                widths.Add(w);
                total += w;
            }
        }

        total += tracking * size * (text.Length - 1);
        using var path = new GraphicsPath();
        var x = cx - total / 2;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ')
            {
                path.AddString(text[i].ToString(), family, (int)style, size, new PointF(x, y), format);
            }

            x += widths[i] + tracking * size;
        }

        var bounds = path.GetBounds();
        using (var shadowPath = (GraphicsPath)path.Clone())
        {
            using var m = new Matrix();
            m.Translate(0, size * 0.05f);
            shadowPath.Transform(m);
            using var shadow = new SolidBrush(Color.FromArgb(plain ? 0 : 190, 0, 0, 0));
            if (!plain)
            {
                using var soft = new Pen(Color.FromArgb(110, 0, 0, 0), size * 0.12f) { LineJoin = LineJoin.Round };
                g.DrawPath(soft, shadowPath);
            }

            g.FillPath(shadow, shadowPath);
        }

        if (display && color.IsEmpty)
        {
            using var gold = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2), Color.Black, Color.Black, 90f);
            gold.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(255, 246, 214), Color.FromArgb(240, 200, 120), Color.FromArgb(196, 140, 58), Color.FromArgb(246, 210, 140) },
                Positions = new[] { 0f, 0.45f, 0.75f, 1f }
            };
            using var outline = new Pen(Color.FromArgb(200, 70, 40, 12), Math.Max(2f, size * 0.025f)) { LineJoin = LineJoin.Round };
            g.DrawPath(outline, path);
            g.FillPath(gold, path);
        }
        else
        {
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static void Alpha(Bitmap bitmap, Func<int, int, double> factor)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var bytes = new byte[data.Stride * data.Height];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        for (var y = 0; y < data.Height; y++)
        {
            for (var x = 0; x < data.Width; x++)
            {
                var i = y * data.Stride + x * 4 + 3;
                bytes[i] = (byte)(bytes[i] * factor(x, y));
            }
        }

        Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        bitmap.UnlockBits(data);
    }
}
