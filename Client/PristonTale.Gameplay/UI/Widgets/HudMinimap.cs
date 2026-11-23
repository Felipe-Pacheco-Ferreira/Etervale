using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D;

namespace PristonTale.Gameplay.UI.Widgets;

// NeighbourFieldId is another loaded field whose map is drawn under the active one where the two meet.
public readonly record struct HudWorldView(
    string FieldId,
    bool Underground,
    PristonIntRectangle MapRectangle,
    Vector3 PlayerPosition,
    Vector3 PlayerForward,
    float TimeOfDay,
    bool Running,
    bool MapHidden = false,
    string NeighbourFieldId = null,
    PristonIntRectangle NeighbourMapRectangle = default,
    int FieldCode = 0);

// The route the quest guide walks: render-plane (x, z) points, From the next one to reach.
public readonly record struct HudMinimapRoute(IReadOnlyList<Vector2> Points, int From)
{
    public bool Shown => Points is not null && From < Points.Count;
}

// Original field minimap: 128x128 box at (656,426), right and bottom anchored, black 50% backdrop, the
// field\map\<id>.tga region around the player (the neighbouring field's map first), NPC dots, the heading arrow,
// MapBox.tga and the field title. North is up; the map does not rotate.
public sealed class HudMinimap
{
    // The view radius starts at 8 cells and eases toward the field's size by 8 units per 70 Hz frame.
    private const float InitialViewSize = 8 * 64f;
    private const float ZoomSpeed = 8f * GameUnits.TicksPerSecond;
    // The original adds a specular 96 to the frame (a quarter of it to the title) under the cursor; adding the sprite's own
    // colour once (a quarter for the title) gives the same brightening on the grey frame art.
    private const int HoverHighlight = 255;
    private const int BoxSize = 128;
    private const int TitleHeight = 16;
    private const int TouchMargin = 10;

    private string _field;
    private string _neighbour;
    private string _fieldMap;
    private string _fieldTitle;
    private string _neighbourMap;
    private float _viewSize = InitialViewSize;

    // The touch HUD's skill wheel holds the lower right corner, so there the box sits in the upper right corner, drawn as
    // the Android HUD's: no title or frame art, a gold edge, the guide's route and the journey's hunting area.
    public static Rectangle Box(HudLayout layout) =>
        layout.Touch ? new Rectangle(layout.Width - TouchMargin - BoxSize, TouchMargin, BoxSize, BoxSize)
            : layout.BottomRight(layout.Browser ? new Rectangle(662, 434, BoxSize, BoxSize) : new Rectangle(656, 426, BoxSize, BoxSize));

    // 16 (dungeon) or 24 (other fields) cells of 64 world units from the centre to the box edge.
    public static float ViewSize(bool underground) => (underground ? 16 : 24) * 64f;

    public float CurrentViewSize => _viewSize;

    // The original shows no minimap in the Bellatra arena, the quest arena, the boss field and the action fields.
    public static bool HiddenOn(int fieldCode, FieldState state) =>
        fieldCode is FieldCodes.Bellatra or FieldCodes.QuestArena or FieldCodes.Boss
        || state is FieldState.QuestArena or FieldState.Room or FieldState.Action;

    // A route's point (the game's planar x, z) in render space, where the player and the minimap's marks are.
    public static Vector3 RenderPoint(RoutePoint point) => WorldSpace.Mirror(new Vector3(point.X, 0f, point.Z));

    // Original stage coordinates are fixed point with Z mirrored relative to the engine (stage vertex Z = -z).
    public static Vector2 MapPoint(PristonIntRectangle rectangle, Vector3 position) =>
        new(position.X - rectangle.Left / GameUnits.FixedScale, rectangle.Bottom / GameUnits.FixedScale + position.Z);

    // Returns the texture source (normalized) and destination (relative to the 126x126 interior) after the original clipping.
    public static bool MapRegion(PristonIntRectangle rectangle, Vector3 position, bool underground, out Vector4 source, out Vector4 destination) =>
        MapRegion(rectangle, position, ViewSize(underground), out source, out destination);

    public static bool MapRegion(PristonIntRectangle rectangle, Vector3 position, float size, out Vector4 source, out Vector4 destination)
    {
        source = default;
        destination = default;
        var width = (rectangle.Right - rectangle.Left) / GameUnits.FixedScale;
        var height = (rectangle.Bottom - rectangle.Top) / GameUnits.FixedScale;
        if (width <= 0 || height <= 0 || size <= 0)
        {
            return false;
        }

        var point = MapPoint(rectangle, position);
        var fpx = size / width;
        var fpy = size / height;
        var fx = point.X / width - fpx;
        var fw = point.X / width + fpx;
        var fy = point.Y / height - fpy;
        var fh = point.Y / height + fpy;
        if (fx < 0 && fw < 0 || fx > 1 && fw > 1 || fy < 0 && fh < 0 || fy > 1 && fh > 1)
        {
            return false;
        }

        float dx = 0;
        float dy = 0;
        float dw = 126;
        float dh = 126;
        if (fx < 0)
        {
            var clip = -fx * (64 / fpx);
            dx += clip;
            dw -= clip;
            fx = 0;
        }

        if (fw > 1)
        {
            dw -= (fw - 1) * (64 / fpx);
            fw = 1;
        }

        if (fy < 0)
        {
            var clip = -fy * (64 / fpy);
            dy += clip;
            dh -= clip;
            fy = 0;
        }

        if (fh > 1)
        {
            dh -= (fh - 1) * (64 / fpy);
            fh = 1;
        }

        source = new Vector4(fx, fy, fw - fx, fh - fy);
        destination = new Vector4(dx, dy, dw, dh);
        return dw > 0 && dh > 0;
    }

    // The heading clockwise from north (up on the map), 0 without a forward.
    public static float Heading(Vector3 forward) => forward.X == 0 && forward.Z == 0 ? 0f : WorldSpace.TransportFacing(forward);

    // The minimap arrow bitmap points down and is turned half a circle before applying the heading.
    public static float ArrowRotation(Vector3 forward) => Heading(forward) + MathF.PI;

    // A world offset from the player to the box centre: x east, y south (engine z), 64 pixels per view radius.
    public static Vector2 MarkerOffset(Vector3 player, Vector3 marker, float size) =>
        new((marker.X - player.X) * 64 / size, (marker.Z - player.Z) * 64 / size);

    // The guide marker's offset: the destination's, or on the same bearing GuideEdge pixels from the centre when farther.
    public static Vector2 GuideOffset(Vector3 player, Vector3 destination, float size)
    {
        var offset = MarkerOffset(player, destination, size);
        var far = MathF.Max(MathF.Abs(offset.X), MathF.Abs(offset.Y));
        return far > GuideEdge ? offset * (GuideEdge / far) : offset;
    }

    private const float GuideEdge = 58f;
    private static readonly Color GuideRim = new(60, 30, 0);
    private static readonly Color GuideColor = new(255, 208, 64);

    public static float EaseViewSize(float current, float target, double elapsedSeconds)
    {
        var step = (float)(ZoomSpeed * Math.Max(0, elapsedSeconds));
        return current < target ? MathF.Min(target, current + step) : MathF.Max(target, current - step);
    }

    public void Update(HudSprites sprites, HudWorldView world, double elapsedSeconds)
    {
        var neighbour = string.Equals(world.NeighbourFieldId, world.FieldId, StringComparison.OrdinalIgnoreCase) ? null : world.NeighbourFieldId;
        if (!string.Equals(world.FieldId, _field, StringComparison.OrdinalIgnoreCase) || !string.Equals(neighbour, _neighbour, StringComparison.OrdinalIgnoreCase))
        {
            // Field art is not reference counted: when the two fields swap, keep what stays in use.
            var fieldMap = world.FieldId is null ? null : HudArt.FieldMap(world.FieldId);
            var fieldTitle = world.FieldId is null ? null : HudArt.FieldTitle(world.FieldId);
            var neighbourMap = neighbour is null ? null : HudArt.FieldMap(neighbour);
            ReleaseUnless(sprites, _fieldMap, fieldMap, neighbourMap);
            ReleaseUnless(sprites, _fieldTitle, fieldTitle, null);
            ReleaseUnless(sprites, _neighbourMap, fieldMap, neighbourMap);
            _field = world.FieldId;
            _neighbour = neighbour;
            _fieldMap = fieldMap;
            _fieldTitle = fieldTitle;
            _neighbourMap = neighbourMap;
            Request(sprites, _fieldMap);
            Request(sprites, _fieldTitle);
            Request(sprites, _neighbourMap);
        }

        _viewSize = EaseViewSize(_viewSize, ViewSize(world.Underground), elapsedSeconds);
    }

    // party: the other members' places, marked red past PartyLimits.NearRange, as the original marks them.
    // guide: the quest destination the minimap guide points at (ClientSettings.MinimapGuide), held to the box's edge while
    // it lies beyond the view.
    private const int HalfBox = BoxSize / 2;
    private const int ZoneDots = 72;
    private static readonly Color ZoneColor = new(90, 200, 255);

    // Dots along the touch HUD's route are this many world units apart; its colours are the Android minimap's.
    private const float RouteDotSpacing = 72f;
    private const int HuntDots = 96;
    private const float TouchArrowSize = BoxSize * .055f;
    private static readonly Color TouchBackdrop = new(27, 22, 17);
    private static readonly Color TouchEdge = new(233, 196, 117);
    private static readonly Color RouteColor = new(255, 214, 64);
    private static readonly Color HuntFill = Color.FromNonPremultiplied(255, 50, 36, 80);
    private static readonly Color HuntEdge = Color.FromNonPremultiplied(255, 96, 72, 230);

    // zone: the Battle Royale's safe zone (centre and radius), drawn as a dotted circle. The touch HUD's box also draws
    // route (the quest guide's) and hunt (the journey's hunting area, render space).
    public void Draw(HudPainter painter, HudLayout layout, HudWorldView world, IReadOnlyList<Vector3> npcs, IReadOnlyList<Vector3> party, bool hovered,
        Vector3? guide = null, (Vector3 Center, float Radius)? zone = null, HudMinimapRoute route = default, (Vector3 Center, float Radius)? hunt = null)
    {
        var box = Box(layout);
        var inside = new Rectangle(box.X + 1, box.Y + 1, BoxSize - 2, BoxSize - 2);
        if (layout.Touch)
        {
            painter.Fill(box, TouchBackdrop);
        }
        else
        {
            painter.ColorBox(box, 0, 0, 0, 128);
        }
        if (_neighbour is not null)
        {
            DrawMap(painter, box, _neighbourMap, world.NeighbourMapRectangle, world.PlayerPosition);
        }

        if (_field is not null)
        {
            DrawMap(painter, box, _fieldMap, world.MapRectangle, world.PlayerPosition);
        }

        var center = new Vector2(box.X + HalfBox, box.Y + HalfBox);
        if (layout.Touch && hunt is { } area)
        {
            var offset = MarkerOffset(world.PlayerPosition, area.Center, _viewSize);
            painter.Disc((int)(center.X + offset.X), (int)(center.Y + offset.Y), (int)(area.Radius * 64 / _viewSize), HuntFill, inside);
            DotRing(painter, center, world.PlayerPosition, area.Center, area.Radius, HuntDots, HuntEdge);
        }

        for (var index = 0; index < npcs.Count; index++)
        {
            var npc = npcs[index];
            if (MathF.Abs(npc.X - world.PlayerPosition.X) < _viewSize && MathF.Abs(npc.Z - world.PlayerPosition.Z) < _viewSize)
            {
                var offset = MarkerOffset(world.PlayerPosition, npc, _viewSize);
                painter.Sprite(HudArt.MapNpc, (int)(center.X + offset.X) - 3, (int)(center.Y + offset.Y) - 3);
            }
        }

        for (var index = 0; index < party.Count; index++)
        {
            var member = party[index];
            var dx = member.X - world.PlayerPosition.X;
            var dz = member.Z - world.PlayerPosition.Z;
            if (MathF.Abs(dx) < _viewSize && MathF.Abs(dz) < _viewSize)
            {
                var offset = MarkerOffset(world.PlayerPosition, member, _viewSize);
                var near = dx * dx + dz * dz < PartyLimits.NearRange * PartyLimits.NearRange;
                painter.Stretch(Parties.HudPartyArt.MapMark, new Rectangle((int)(center.X + offset.X) - 3, (int)(center.Y + offset.Y) - 3, 8, 8), null,
                    near ? Color.White : Color.Red);
            }
        }

        if (zone is { } safe)
        {
            DotRing(painter, center, world.PlayerPosition, safe.Center, safe.Radius, ZoneDots, ZoneColor);
        }

        if (layout.Touch && route.Shown)
        {
            DrawRoute(painter, inside, center, world.PlayerPosition, route);
        }

        if (guide is { } destination)
        {
            var offset = GuideOffset(world.PlayerPosition, destination, _viewSize);
            var at = new Point((int)(center.X + offset.X), (int)(center.Y + offset.Y));
            painter.Fill(new Rectangle(at.X - 3, at.Y - 3, 7, 7), GuideRim);
            painter.Fill(new Rectangle(at.X - 2, at.Y - 2, 5, 5), GuideColor);
        }

        if (layout.Touch)
        {
            DrawTouchFrame(painter, box, center, world.PlayerForward);
            return;
        }

        painter.Rotated(HudArt.MapArrow, center, ArrowRotation(world.PlayerForward));
        painter.Sprite(HudArt.MapBox, box.X, box.Y);
        if (hovered)
        {
            painter.Highlight(HudArt.MapBox, box, HoverHighlight);
        }

        if (_field is not null)
        {
            var title = new Rectangle(box.X, box.Y - TitleHeight, BoxSize, TitleHeight);
            if (layout.Browser && string.Equals(_field, RicartenField.Definition.Id, StringComparison.OrdinalIgnoreCase))
            {
                painter.FramedBox(title, new Color(166, 142, 88), new Color(64, 51, 31), new Color(13, 12, 9), 2);
                painter.TextCentered(RicartenField.Definition.Name, title.Left + 3, title.Right - 3, title.Y + 3, 11f / HudPainter.BakePixels);
            }
            else
            {
                painter.Stretch(_fieldTitle, title);
                if (hovered)
                {
                    painter.Highlight(_fieldTitle, title, HoverHighlight >> 2);
                }
            }
        }
    }

    // A circle of 2x2 dots around ring (render space), those inside the box.
    private void DotRing(HudPainter painter, Vector2 center, Vector3 player, Vector3 ring, float radius, int dots, Color color)
    {
        for (var dot = 0; dot < dots; dot++)
        {
            var angle = MathF.Tau * dot / dots;
            var point = ring + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            var offset = MarkerOffset(player, point, _viewSize);
            if (MathF.Abs(offset.X) < HalfBox - 1 && MathF.Abs(offset.Y) < HalfBox - 1)
            {
                painter.Fill(new Rectangle((int)(center.X + offset.X) - 1, (int)(center.Y + offset.Y) - 1, 2, 2), color);
            }
        }
    }

    // Gold dots from the player along the route's points still ahead, a larger one at its end.
    private void DrawRoute(HudPainter painter, Rectangle inside, Vector2 center, Vector3 player, HudMinimapRoute route)
    {
        var from = new Vector2(player.X, player.Z);
        for (var index = route.From; index < route.Points.Count; index++)
        {
            var to = route.Points[index];
            var dots = Math.Max(1, (int)(Vector2.Distance(from, to) / RouteDotSpacing));
            for (var dot = 1; dot <= dots; dot++)
            {
                RouteDot(painter, inside, center, player, Vector2.Lerp(from, to, dot / (float)dots), 1);
            }

            from = to;
        }

        RouteDot(painter, inside, center, player, from, 3);
    }

    private void RouteDot(HudPainter painter, Rectangle inside, Vector2 center, Vector3 player, Vector2 point, int radius)
    {
        var offset = MarkerOffset(player, new Vector3(point.X, 0f, point.Y), _viewSize);
        painter.Disc((int)(center.X + offset.X), (int)(center.Y + offset.Y), radius, RouteColor, inside);
    }

    // The Android minimap's gold edge and its gold arrow turned to the heading.
    private static void DrawTouchFrame(HudPainter painter, Rectangle box, Vector2 center, Vector3 forward)
    {
        painter.Fill(new Rectangle(box.X, box.Y, box.Width, 2), TouchEdge);
        painter.Fill(new Rectangle(box.X, box.Bottom - 2, box.Width, 2), TouchEdge);
        painter.Fill(new Rectangle(box.X, box.Y + 2, 2, box.Height - 4), TouchEdge);
        painter.Fill(new Rectangle(box.Right - 2, box.Y + 2, 2, box.Height - 4), TouchEdge);
        var (sin, cos) = MathF.SinCos(Heading(forward));
        Vector2 Turned(float x, float y) => center + new Vector2(x * cos - y * sin, x * sin + y * cos) * TouchArrowSize;
        painter.Triangle(Turned(0f, -1f), Turned(.7f, 1f), Turned(-.7f, 1f), TouchEdge);
    }

    private void DrawMap(HudPainter painter, Rectangle box, string path, PristonIntRectangle rectangle, Vector3 player)
    {
        var map = painter.Texture(path);
        if (map is null || !MapRegion(rectangle, player, _viewSize, out var source, out var destination))
        {
            return;
        }

        var from = new Rectangle((int)(source.X * map.Width), (int)(source.Y * map.Height),
            Math.Max(1, (int)(source.Z * map.Width)), Math.Max(1, (int)(source.W * map.Height)));
        var to = new Rectangle(box.X + 1 + (int)destination.X, box.Y + 1 + (int)destination.Y, (int)destination.Z, (int)destination.W);
        painter.Stretch(path, to, from);
    }

    private static void Request(HudSprites sprites, string path)
    {
        if (path is not null)
        {
            sprites.Request(path);
        }
    }

    private static void ReleaseUnless(HudSprites sprites, string path, string keep, string keepToo)
    {
        if (path is not null && !string.Equals(path, keep, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, keepToo, StringComparison.OrdinalIgnoreCase))
        {
            sprites.Release(path);
        }
    }
}
