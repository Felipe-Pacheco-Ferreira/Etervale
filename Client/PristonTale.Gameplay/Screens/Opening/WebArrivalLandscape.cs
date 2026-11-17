using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Gameplay.Screens.Opening;

internal static class WebArrivalLandscape
{
    internal const string TextureDirectory = "Field/forest/";

    internal static PristonStage Build(string assetRoot)
    {
        var landscape = new Landscape();
        landscape.AddRidges();
        landscape.AddCourtyard();
        landscape.AddRuins();
        landscape.AddPines();
        return landscape.Build(Path.Combine(assetRoot, TextureDirectory, "avalon-arrival.smd"));
    }

    private sealed class Landscape
    {
        private const int Earth = 0;
        private const int Grass = 1;
        private const int Paving = 2;
        private const int Trim = 3;
        private const int Masonry = 4;
        private const int Cliff = 5;
        private const int Bark = 6;
        private const int Needles = 7;
        private const int Ember = 8;
        private const int MossyMasonry = 9;
        private const int Banner = 10;
        private const int BannerEmblem = 11;
        private const float BannerTop = 116f;
        private const float BannerTip = 78f;
        private static readonly Vector3 Court = new(0f, 0f, 143f);
        private static readonly Vector3 HeroMark = new(0f, 0f, 110f);

        private static readonly PristonMaterial[] Materials =
        [
            Material("dirt.bmp"),
            Material("Grass.bmp"),
            Material("stone-35.bmp"),
            Material("stairtop.bmp"),
            Material("stone-34.bmp"),
            Material("stone-35.bmp"),
            Material("bark01.bmp"),
            Material("Grass.bmp", PristonMaterialSurfaceFlags.Move),
            Material("stairtop.bmp", emission: 1f),
            Material("t_nwall-04.bmp"),
            Material("stone-35.bmp", PristonMaterialSurfaceFlags.MoveX1),
            Material("avalon-banner-emblem.png", PristonMaterialSurfaceFlags.MoveX1),
        ];

        private readonly List<PristonStageVertex> _vertices = [];
        private readonly List<PristonStageFace> _faces = [];
        private readonly List<PristonTexLink> _links = [];
        private readonly List<PristonStageLight> _lights = [];
        private readonly Random _random = new(41831);

        internal PristonStage Build(string path)
        {
            var bounds = BoundingBox.CreateFromPoints(_vertices.Select(static vertex => vertex.Position));
            return new PristonStage(path, Materials, _vertices, _faces, _links, _lights, bounds);
        }

        internal void AddRidges()
        {
            // The foreground saddle leaves an open view through the arch before the far peaks rise behind it.
            Ridge(-1500f, 1840f, 645f, 770f, 0.52f, new Color(157, 176, 204), 1.2f);
            Ridge(-1010f, 1440f, 434f, 570f, 0.31f, new Color(138, 163, 190), 3.3f);
            Ridge(-565f, 1170f, 267f, 470f, 0.02f, new Color(122, 154, 157), 5.0f);

            foreach (var side in new[] { -1f, 1f })
            {
                Terrain(12, 31, (u, v) =>
                {
                    var z = MathHelper.Lerp(-575f, 1225f, v);
                    var outward = side < 0f ? 1f - u : u;
                    var x = ShoulderEdge(z) + outward * 506f;
                    return new Vector3(side * x, ShoulderHeight(x, z, side), z);
                }, Grass, new Color(137, 167, 149), 1.8f * side);
            }
        }

        internal void AddCourtyard()
        {
            Ground(-2250f, -1840f, 2250f, 1280f, -11f, Earth, new Color(126, 146, 142));
            Ground(-338f, -105f, 338f, 925f, -6f, Grass, new Color(164, 181, 163));

            // The open court and its approach share a stable plane for actor feet and picking.
            Ground(-226f, 95f, 226f, 885f, -1f, Grass, new Color(149, 174, 159));
            Ground(-224f, 392f, 224f, 865f, -0.8f, Paving, new Color(155, 175, 188));
            Ground(-67f, 443f, 67f, 880f, -0.55f, Trim, new Color(144, 145, 128));
            AddCauseway();
            Band(Court, 0f, 296f, -0.35f, Paving, new Color(105, 128, 141));
            AddCourtPaving();
            Band(Court, 296f, 305f, 0.15f, Trim, new Color(186, 167, 126));
            Band(Court, 283f, 287f, 0.12f, Paving, new Color(187, 164, 121));
            Band(HeroMark, 20.25f, 22.15f, 0.1f, Paving, new Color(225, 183, 96));
            Band(HeroMark, 24.1f, 24.8f, 0.11f, Paving, new Color(187, 154, 91));

            for (var index = 0; index < 12; index++)
            {
                var angle = (index + 0.25f) * MathHelper.TwoPi / 12f;
                if (MathF.Cos(angle) < 0.88f)
                {
                    var center = Court + Polar(288f, angle, 0.17f);
                    Diamond(center, 6f, new Color(219, 194, 145));
                }
            }
        }

        private void AddCourtPaving()
        {
            const int courses = 9;
            const float courseWidth = 32.65f;
            for (var course = 0; course < courses; course++)
            {
                var inner = course == 0 ? 0f : course * courseWidth + 0.57f;
                var outer = (course + 1) * courseWidth - 0.57f;
                var stones = Math.Max(8, (int)MathF.Round((inner + outer) * MathHelper.Pi / 32f));
                var turn = MathHelper.TwoPi / stones;
                var edge = 0.5f / Math.Max(outer, 1f);
                for (var stone = 0; stone < stones; stone++)
                {
                    var angle = (stone + 0.44f * (course % 2)) * turn;
                    var y = -0.05f + ((stone * 17 + course * 11) % 5) * 0.022f;
                    var material = (stone + course * 2) % 11 == 2 ? Masonry : Paving;
                    var tint = Shade(new Color(172, 195, 207), stone + course * 11, 8);
                    if (course is 2 or 4 or 7 && stone % 7 == 2)
                    {
                        tint = Shade(new Color(153, 172, 165), stone, 6);
                    }

                    var a = Court + Polar(inner, angle + edge, y);
                    var b = Court + Polar(outer, angle + edge, y);
                    var c = Court + Polar(outer, angle + turn - edge, y);
                    FlatTriangle(a, b, c, material, tint);
                    if (course > 0)
                    {
                        var d = Court + Polar(inner, angle + turn - edge, y);
                        FlatTriangle(a, c, d, material, tint);
                    }
                }
            }
        }

        private void AddCauseway()
        {
            for (var row = 0; row < 14; row++)
            {
                var z = 445f + row * 31f;
                var start = -59f + (row % 2) * 11f;
                for (var slab = -1; slab < 4; slab++)
                {
                    var left = Math.Max(-60f, start + slab * 31f + 0.5f);
                    var right = Math.Min(60f, start + (slab + 1) * 31f - 0.5f);
                    if (right - left > 2f)
                    {
                        Ground(left, z + 0.55f, right, z + 30.45f, -0.31f, Paving,
                            Shade(new Color(163, 188, 200), row + slab + 1, 8));
                    }
                }
            }
        }

        internal void AddRuins()
        {
            Box(-116f, 116f, -215f, -124f, -2f, 4f, Trim, new Color(188, 192, 183));
            Box(-110f, 110f, -214f, -138f, 4f, 10f, Masonry, new Color(164, 182, 192));
            Box(-106f, 106f, -213f, -150f, 10f, 15f, Trim, new Color(178, 184, 170));
            AddArch(-183f);

            BrokenTower(-231f, -67f, 39f, 174f, 3);
            BrokenTower(212f, -112f, 47f, 207f, 6);
            BrokenTower(-352f, -198f, 49f, 119f, 8);
            BrokenTower(366f, -208f, 46f, 135f, 3);

            foreach (var side in new[] { -1f, 1f })
            {
                AddRuinedWing(side);
                for (var index = 0; index < 5; index++)
                {
                    var x = side * (273f + index * 15f);
                    var z = 44f + index * 46f;
                    var height = 17f + _random.Next(21);
                    Box(x - 16f, x + 16f, z - 19f, z + 19f, -6f, height, index % 2 == 0 ? MossyMasonry : Masonry,
                        new Color(143, 165, 170));
                    if (index != 1)
                    {
                        Box(x - 19f, x + 19f, z - 22f, z + 22f, height, height + 5f, Trim, new Color(163, 174, 177));
                    }
                }

                for (var index = 0; index < 15; index++)
                {
                    var z = -175f + _random.Next(605);
                    var x = side * (296f + _random.Next(147));
                    var center = new Vector3(x, -8f, z);
                    Taper(center, 6f + _random.Next(15), 2f + _random.Next(6), 6f + _random.Next(13),
                        5, Cliff, new Color(154, 169, 165));
                }

                Brazier(side * 120f, 65f, 20f, liveFlame: true);
                Brazier(side * 166f, -80f);
                Brazier(side * 255f, 109f);
                Brazier(side * 247f, 502f);
            }

            _lights.Add(new PristonStageLight(0, new Vector3(-24f, 65f, 163f), 112f, new Vector3(0.88f, 0.72f, 0.57f)));
        }

        private void AddRuinedWing(float side)
        {
            const int bays = 6;
            for (var bay = 0; bay < bays; bay++)
            {
                var x = side * (112f + 29f * bay);
                var z = -158f - 8f * bay;
                var courses = Math.Clamp((int)MathF.Round(2.7f + 1.9f * MathF.Sin(bay * 1.7f + side)), 1, 5);
                for (var course = 0; course < courses; course++)
                {
                    var offset = course % 2 == 0 ? 0f : side * 5f;
                    var y = -6f + 14f * course;
                    var material = (course + bay) % 4 == 0 ? MossyMasonry : Masonry;
                    Box(x - 14f + offset, x + 14f + offset, z - 14f, z + 16f, y, y + 13.55f, material,
                        Shade(new Color(148, 168, 177), bay + course * 3, 7));
                }
            }

            var buttress = side * 104f;
            Box(buttress - 14f, buttress + 14f, -185f, -139f, -3f, 45f, MossyMasonry, new Color(144, 167, 174));
            Box(buttress - 10f, buttress + 10f, -186f, -143f, 45f, 82f, Masonry, new Color(164, 184, 197));
            Box(buttress - 13f, buttress + 13f, -187f, -142f, 82f, 87f, Trim, new Color(174, 180, 174));
        }

        internal void AddPines()
        {
            foreach (var side in new[] { -1f, 1f })
            {
                for (var index = 0; index < 27; index++)
                {
                    var z = -315f + index * 42f + _random.Next(40);
                    var minimumX = z > 560f ? 490f : 367f;
                    var x = side * (minimumX + _random.Next(255));
                    Pine(x, z, 46f + _random.Next(103), ShoulderHeight(MathF.Abs(x), z, side) - 4f);
                }
            }

            for (var index = 0; index < 21; index++)
            {
                var x = -644f + index * 63f + _random.Next(38);
                var z = -407f + _random.Next(-55, 37);
                Pine(x, z, 34f + _random.Next(78), -12f);
            }
        }

        private void AddArch(float z)
        {
            foreach (var x in new[] { -77f, 77f })
            {
                Box(x - 23f, x + 23f, z - 23f, z + 23f, 13f, 22f, Trim, new Color(190, 191, 178));
                for (var course = 0; course < 4; course++)
                {
                    var y = 22f + course * 12f;
                    var offset = course % 2 == 0 ? -1f : 1f;
                    Box(x - 18f + offset, x + 18f + offset, z - 18f, z + 18f,
                        y, y + 11.65f, course == 0 ? MossyMasonry : Masonry, Shade(new Color(173, 190, 200), course, 6));
                }
                Box(x - 23f, x + 23f, z - 22f, z + 22f, 70f, 76f, Trim, new Color(191, 190, 175));
            }

            // Narrow gaps between voussoirs read as masonry seams. The taller centre crowns a pointed passage.
            for (var index = 0; index < 13; index++)
            {
                var from = index * MathHelper.Pi / 13f + 0.008f;
                var to = (index + 1) * MathHelper.Pi / 13f - 0.008f;
                var innerFrom = ArchPoint(from, 57f, 44f);
                var innerTo = ArchPoint(to, 57f, 44f);
                var outerFrom = ArchPoint(from, 96f, 64f);
                var outerTo = ArchPoint(to, 96f, 64f);
                var front = new Vector3(0f, 0f, z + 17f);
                var back = new Vector3(0f, 0f, z - 17f);
                var color = Shade(new Color(186, 201, 207), index, 7);
                Quad(innerFrom + front, outerFrom + front, outerTo + front, innerTo + front, Masonry, color);
                Quad(outerFrom + back, innerFrom + back, innerTo + back, outerTo + back, Masonry, color);
                Quad(innerTo + front, innerTo + back, innerFrom + back, innerFrom + front, Masonry, color);
                Quad(outerFrom + front, outerFrom + back, outerTo + back, outerTo + front, Trim, color);
            }

            Box(-8f, 8f, z + 17f, z + 20f, 126f, 137f, Trim, new Color(216, 197, 148));
            AddBanner(z + 21f);
        }

        private void AddBanner(float z)
        {
            Box(-15f, 15f, z - 1f, z + 1f, 115.5f, 117.1f, Trim, new Color(162, 143, 108));
            var topLeft = new Vector3(-12f, BannerTop, z + 0.3f);
            var topCenter = new Vector3(0f, BannerTop, z + 1.7f);
            var topRight = new Vector3(12f, BannerTop, z + 0.3f);
            var bottomLeft = new Vector3(-12f, 83f, z + 1.4f);
            var bottomCenter = new Vector3(0f, BannerTip, z + 2f);
            var bottomRight = new Vector3(12f, 83f, z + 1.4f);
            Quad(bottomLeft, bottomCenter, topCenter, topLeft, Banner, new Color(154, 49, 53));
            Quad(bottomCenter, bottomRight, topRight, topCenter, Banner, new Color(182, 64, 61));
            _lights.Add(new PristonStageLight(0, new Vector3(0f, 102f, z + 45f), 75f, new Vector3(0.82f, 0.67f, 0.53f)));
            AddBannerEmblem(z);
        }

        private void AddBannerEmblem(float z)
        {
            const int segments = 32;
            const float radius = 8.8f;
            const float centerY = 99.5f;
            const float crop = 0.425f;
            var uvCenter = new Vector2(0.5f);
            var center = BannerSurface(0f, centerY, z);
            for (var segment = 0; segment < segments; segment++)
            {
                var from = segment * MathHelper.TwoPi / segments;
                var to = (segment + 1) * MathHelper.TwoPi / segments;
                var a = new Vector2(MathF.Cos(from), MathF.Sin(from));
                var b = new Vector2(MathF.Cos(to), MathF.Sin(to));
                AddTriangle(center, BannerSurface(a.X * radius, centerY + a.Y * radius, z),
                    BannerSurface(b.X * radius, centerY + b.Y * radius, z), BannerEmblem, Color.White,
                    uvCenter, uvCenter + new Vector2(a.X, -a.Y) * crop, uvCenter + new Vector2(b.X, -b.Y) * crop);
            }

            // Hidden tabs give both material batches the same vertical range for the renderer's wind displacement.
            AddTriangle(new Vector3(-0.22f, BannerTop - 0.32f, z + 1.27f), new Vector3(0.22f, BannerTop - 0.32f, z + 1.27f),
                new Vector3(0f, BannerTop, z + 1.27f), BannerEmblem, Color.White, uvCenter, uvCenter, uvCenter);
            AddTriangle(new Vector3(0f, BannerTip, z + 1.57f), new Vector3(0.22f, BannerTip + 0.32f, z + 1.57f),
                new Vector3(-0.22f, BannerTip + 0.32f, z + 1.57f), BannerEmblem, Color.White, uvCenter, uvCenter, uvCenter);
        }

        private static Vector3 BannerSurface(float x, float y, float z)
        {
            var fold = MathF.Abs(x) / 12f;
            var bottom = MathHelper.Lerp(BannerTip, 83f, fold);
            var height = (y - bottom) / (BannerTop - bottom);
            var depth = MathHelper.Lerp(MathHelper.Lerp(2f, 1.4f, fold), MathHelper.Lerp(1.7f, 0.3f, fold), height);
            return new Vector3(x, y, z + depth + 0.55f);
        }

        private static Vector3 ArchPoint(float angle, float radius, float rise)
        {
            var x = MathF.Cos(angle);
            var y = MathF.Sin(angle);
            return new Vector3(x * radius, 74f + rise * MathF.Pow(y, 0.82f), 0f);
        }

        private void BrokenTower(float x, float z, float radius, float height, int damage)
        {
            var baseCenter = new Vector3(x, -6f, z);
            Taper(baseCenter, radius + 14f, radius + 7f, 15f, 9, Trim, new Color(160, 177, 171));
            var shaft = baseCenter + Vector3.Up * 15f;
            TowerShaft(shaft, radius, height - 44f, damage);
            var ledge = baseCenter + Vector3.Up * (height - 29f);
            Taper(ledge, radius + 3f, radius + 3f, 7f, 9, Trim, new Color(182, 193, 187));
            for (var index = 0; index < 9; index++)
            {
                var from = index * MathHelper.TwoPi / 9f;
                var to = (index + 1) * MathHelper.TwoPi / 9f;
                var bottom = ledge + Vector3.Up * 7f;
                var remaining = 8f + 5f * ((index * damage + damage) % 7);
                var a = bottom + Polar(radius, from, 0f);
                var b = bottom + Polar(radius, to, 0f);
                var c = bottom + Polar(radius - 4f, to, remaining * 0.82f);
                var d = bottom + Polar(radius - 4f, from, remaining);
                Quad(a, b, c, d, Masonry, Shade(new Color(155, 174, 186), index, 9));
                Quad(d, c, bottom + Polar(radius - 12f, to, remaining * 0.82f),
                    bottom + Polar(radius - 12f, from, remaining), Cliff, new Color(153, 173, 177));
            }

            Box(x - 9f, x + 9f, z + radius - 12f, z + radius - 9f, 36f, height - 45f, Masonry, new Color(62, 77, 88));
            Box(x - 12f, x + 12f, z + radius - 6f, z + radius + 2f, height - 48f, height - 43f, Trim,
                new Color(152, 170, 181));
        }

        private void TowerShaft(Vector3 center, float radius, float height, int damage)
        {
            const int faces = 14;
            var rows = Math.Max(4, (int)MathF.Round(height / 18f));
            var turn = MathHelper.TwoPi / faces;
            for (var row = 0; row < rows; row++)
            {
                var lower = height * row / rows;
                var upper = height * (row + 1) / rows;
                var lowerRadius = MathHelper.Lerp(radius + 1f, radius - 2f, row / (float)rows);
                var upperRadius = MathHelper.Lerp(radius + 1f, radius - 2f, (row + 1) / (float)rows);
                for (var face = 0; face < faces; face++)
                {
                    // The slit opens through the shell. Its back and ledge sit inside the tower profile.
                    if (face == 0 && row >= 2 && row < rows - 1)
                    {
                        continue;
                    }

                    var from = (face - 0.5f) * turn;
                    var to = (face + 0.5f) * turn;
                    var a = center + Polar(lowerRadius, from, lower);
                    var b = center + Polar(lowerRadius, to, lower);
                    var c = center + Polar(upperRadius, to, upper + 0.02f);
                    var d = center + Polar(upperRadius, from, upper + 0.02f);
                    var material = (face * 3 + row + damage) % 12 < 3 ? MossyMasonry : Masonry;
                    var tint = Shade(new Color(159, 180, 192), face * 2 + row * 3 + damage, 7);
                    Quad(a, b, c, d, material, tint);
                }
            }
        }

        private void Brazier(float x, float z, float stemHeight = 26f, bool liveFlame = false)
        {
            var center = new Vector3(x, -2f, z);
            Taper(center, 15f, 12f, 5f, 8, Trim, new Color(162, 169, 160));
            Taper(center + Vector3.Up * 5f, 10f, 9f, stemHeight, 8, Masonry, new Color(145, 162, 168));
            Taper(center + Vector3.Up * (stemHeight + 5f), 9f, 16f, 8f, 8, Trim, new Color(183, 181, 153));
            var lampColor = new Vector3(1f, 0.67f, 0.37f);
            if (liveFlame)
            {
                _lights.Add(new PristonStageLight(0, center + Vector3.Up * (stemHeight + 24f), 103f, lampColor * 0.65f));
            }
            else
            {
                Taper(center + Vector3.Up * (stemHeight + 13f), 12f, 3f, 2f, 8, Ember, new Color(255, 165, 94));
                _lights.Add(new PristonStageLight(0x80002, center + Vector3.Up * (stemHeight + 20f), 125f, lampColor * 0.8f));
            }
        }

        private void Pine(float x, float z, float height, float ground)
        {
            var center = new Vector3(x, ground, z);
            Taper(center, height * 0.052f, height * 0.018f, height * 0.72f,
                5, Bark, new Color(138, 155, 143));
            for (var tier = 0; tier < 4; tier++)
            {
                var baseY = height * (0.25f + tier * 0.16f);
                var width = height * (0.26f - tier * 0.046f);
                var layer = center + new Vector3(tier * 0.9f, baseY, 0f);
                var crown = layer + new Vector3(-2.5f, height * 0.38f, 1.5f);
                for (var needle = 0; needle < 10; needle++)
                {
                    var angle = (needle + tier * 0.35f) * MathHelper.TwoPi / 10f;
                    var next = angle + MathHelper.TwoPi / 10f;
                    var a = layer + Polar(width * (needle % 2 == 0 ? 1f : 0.75f), angle, needle % 2 == 0 ? -4f : 4f);
                    var b = layer + Polar(width * (needle % 2 == 0 ? 0.75f : 1f), next, needle % 2 == 0 ? 4f : -4f);
                    var color = Shade(new Color(95 + tier * 11, 140 + tier * 9, 128 + tier * 9), needle, 8);
                    Triangle(a, b, crown, Needles, color);
                    Triangle(layer + new Vector3(0f, 4f, 0f), b, a, Needles, new Color(81, 118, 117));
                }
            }
        }

        private void Ridge(float z, float halfWidth, float height, float saddleWidth, float saddleHeight, Color color, float phase)
        {
            Terrain(48, 12, (u, v) =>
            {
                var x = (2f * u - 1f) * halfWidth;
                var saddle = MathHelper.SmoothStep(saddleHeight, 1f, MathF.Abs(x) / saddleWidth);
                var peaks = 0.73f + 0.16f * MathF.Sin(x / 167f + phase)
                    + 0.105f * MathF.Sin(x / 63f - phase) + 0.046f * MathF.Sin(x / 31f + phase);
                var crest = 0.38f + 0.06f * MathF.Sin(x / 145f + phase);
                var slope = v < crest ? v / crest : (1f - v) / (1f - crest);
                var grade = MathF.Pow(MathHelper.SmoothStep(0f, 1f, slope), 1.2f);
                var scree = 0.045f * grade * MathF.Sin(x / 68f + v * 20f + phase);
                var y = -13f + height * (peaks + scree) * grade * saddle * MathF.Sin(u * MathHelper.Pi);
                var depth = z + (v - 0.38f) * 520f + 45f * MathF.Sin(x / 190f + phase);
                return new Vector3(x, y, depth);
            }, Cliff, color, phase);
        }

        private void Terrain(int columns, int rows, Func<float, float, Vector3> sample, int material, Color color, float phase)
        {
            var rowLength = columns + 1;
            var positions = new Vector3[rowLength * (rows + 1)];
            var colors = new Color[positions.Length];
            for (var row = 0; row <= rows; row++)
            {
                for (var column = 0; column <= columns; column++)
                {
                    var index = row * rowLength + column;
                    var point = sample(column / (float)columns, row / (float)rows);
                    positions[index] = point;
                    var mottled = 0.49f + 0.17f * MathF.Sin(point.X / 76f + point.Z / 93f + phase)
                        + 0.12f * MathF.Sin(point.X / 47f - point.Z / 51f + phase);
                    colors[index] = Color.Lerp(Scale(color, 0.66f), Scale(color, 1.11f), mottled + Math.Clamp(point.Y / 600f, 0f, 0.24f));
                }
            }

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var a = row * rowLength + column;
                    var b = a + rowLength;
                    TerrainTriangle(positions, colors, a, b, b + 1, material);
                    TerrainTriangle(positions, colors, a, b + 1, a + 1, material);
                }
            }
        }

        private void TerrainTriangle(Vector3[] positions, Color[] colors, int a, int b, int c, int material)
        {
            var first = _vertices.Count;
            _vertices.Add(new PristonStageVertex(positions[a], colors[a]));
            _vertices.Add(new PristonStageVertex(positions[b], colors[b]));
            _vertices.Add(new PristonStageVertex(positions[c], colors[c]));
            _faces.Add(new PristonStageFace(first, first + 1, first + 2, material, _links.Count));
            _links.Add(new PristonTexLink(TerrainUv(positions[a]), TerrainUv(positions[b]), TerrainUv(positions[c]), 0));
        }

        private static Vector2 TerrainUv(Vector3 point) => new(point.X / 122f, (point.Z + point.Y * 0.48f) / 122f);

        private static float ShoulderEdge(float z) => 317f + 21f * MathF.Sin(z / 151f) + 11f * MathF.Cos(z / 71f);

        private static float ShoulderHeight(float x, float z, float side)
        {
            var cross = MathHelper.Clamp((x - ShoulderEdge(z)) / 506f, 0f, 1f);
            var fade = MathHelper.SmoothStep(0f, 1f, cross / 0.14f)
                * MathHelper.SmoothStep(0f, 1f, (1f - cross) / 0.27f);
            var folds = 52f + 24f * MathF.Sin(z / 145f + side) + 15f * MathF.Sin(z / 74f + cross * 5f - side);
            return -12f + fade * (folds + 66f * cross + 18f * MathF.Sin(cross * 12f + z / 160f));
        }

        private void Ground(float left, float back, float right, float front, float y, int material, Color color) =>
            Quad(new Vector3(left, y, back), new Vector3(left, y, front),
                new Vector3(right, y, front), new Vector3(right, y, back), material, color);

        private void Band(Vector3 origin, float inner, float outer, float y, int material, Color color)
        {
            const int segments = 48;
            for (var index = 0; index < segments; index++)
            {
                var from = index * MathHelper.TwoPi / segments;
                var to = (index + 1) * MathHelper.TwoPi / segments;
                var a = origin + Polar(inner, from, y);
                var b = origin + Polar(outer, from, y);
                var c = origin + Polar(outer, to, y);
                var d = origin + Polar(inner, to, y);
                var tint = Shade(color, index, inner == 0f ? 1 : 4);
                FlatTriangle(a, b, c, material, tint);
                if (inner > 0f)
                {
                    FlatTriangle(a, c, d, material, tint);
                }
            }
        }

        private void Diamond(Vector3 center, float radius, Color color) =>
            Quad(center + new Vector3(0f, 0f, -radius), center + new Vector3(-radius * 0.65f, 0f, 0f),
                center + new Vector3(0f, 0f, radius), center + new Vector3(radius * 0.65f, 0f, 0f), Trim, color);

        private void Box(float left, float right, float back, float front, float bottom, float top, int material, Color color)
        {
            var a = new Vector3(left, bottom, back);
            var b = new Vector3(right, bottom, back);
            var c = new Vector3(right, bottom, front);
            var d = new Vector3(left, bottom, front);
            var rise = Vector3.Up * (top - bottom);
            Quad(d, c, c + rise, d + rise, material, color);
            Quad(c, b, b + rise, c + rise, material, Shade(color, 1, 6));
            Quad(b, a, a + rise, b + rise, material, Shade(color, 3, 6));
            Quad(a, d, d + rise, a + rise, material, Shade(color, 5, 6));
            Quad(a + rise, d + rise, c + rise, b + rise, material, Shade(color, 2, 6));
        }

        private void Taper(Vector3 center, float bottom, float top, float height, int sides, int material, Color color)
        {
            for (var index = 0; index < sides; index++)
            {
                var angle = index * MathHelper.TwoPi / sides;
                var next = (index + 1) * MathHelper.TwoPi / sides;
                var a = center + Polar(bottom, angle, 0f);
                var b = center + Polar(bottom, next, 0f);
                var c = center + Polar(top, next, height);
                var d = center + Polar(top, angle, height);
                Quad(a, b, c, d, material, Shade(color, index, 5));
                FlatTriangle(center + Vector3.Up * height, d, c, material, color);
            }
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int material, Color color)
        {
            var u = Vector3.Distance(a, b) / 61f;
            var v = Vector3.Distance(a, d) / 61f;
            AddTriangle(a, b, c, material, color, Vector2.Zero, new Vector2(u, 0f), new Vector2(u, v));
            AddTriangle(a, c, d, material, color, Vector2.Zero, new Vector2(u, v), new Vector2(0f, v));
        }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, int material, Color color) =>
            AddTriangle(a, b, c, material, color, Vector2.Zero, new Vector2(Vector3.Distance(a, b) / 96f, 0f),
                new Vector2(Vector3.Distance(a, b) / 192f, Vector3.Distance(a, c) / 96f));

        private void FlatTriangle(Vector3 a, Vector3 b, Vector3 c, int material, Color color) =>
            AddTriangle(a, b, c, material, color, FloorUv(a), FloorUv(b), FloorUv(c));

        private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, int material, Color color, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var first = _vertices.Count;
            _vertices.Add(new PristonStageVertex(a, color));
            _vertices.Add(new PristonStageVertex(b, color));
            _vertices.Add(new PristonStageVertex(c, color));
            _faces.Add(new PristonStageFace(first, first + 1, first + 2, material, _links.Count));
            _links.Add(new PristonTexLink(ua, ub, uc, 0));
        }

        private static Vector2 FloorUv(Vector3 point) => new(point.X / 49f, point.Z / 49f);

        private static Vector3 Polar(float radius, float angle, float y) =>
            new(radius * MathF.Sin(angle), y, radius * MathF.Cos(angle));

        private static Color Shade(Color color, int index, int spread)
        {
            var offset = ((index * 7 + 3) % 5 - 2) * spread;
            return new Color(Math.Clamp(color.R + offset, 0, 255), Math.Clamp(color.G + offset, 0, 255),
                Math.Clamp(color.B + offset, 0, 255));
        }

        private static Color Scale(Color color, float factor) =>
            new(Math.Min(255, (int)(color.R * factor)), Math.Min(255, (int)(color.G * factor)),
                Math.Min(255, (int)(color.B * factor)));

        private static PristonMaterial Material(string texture, PristonMaterialSurfaceFlags flags = PristonMaterialSurfaceFlags.Solid, float emission = 0f) =>
            new(InUse: true, TextureCounter: 1, TextureType: 0, BlendType: 0, TwoSided: true,
                Diffuse: Vector3.One, Transparency: 0f,
                MeshState: (flags & PristonMaterialSurfaceFlags.Solid) != 0 ? StageCollisionReader.CheckFace : 0u,
                SurfaceFlags: flags, TextureName: texture, OpacityName: string.Empty, TextureFormStates: [],
                AnimationTextureNames: [], FrameMask: 0, ShiftFrameSpeed: 0, AnimationFrame: 0, SelfIllum: emission);
    }
}
