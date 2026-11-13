using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.UI.Clans;

namespace PristonTale.Gameplay.UI.Widgets;

// Name boxes over the scene: an NPC's name always within 768 units of the camera, a party member's always with its life
// bar, a clan mate's always and another player's while it is under the cursor or the clicked target, a player's clan in
// a "[Clan]" line over its name (for another clan only while hovered or clicked, as in the original), a monster's only
// while it is under the cursor or is the clicked target. A summon or crystal pet's box shows within
// the same range, with its owner's name in parentheses on a second line and, on the viewer's own, its life bar. Farther
// boxes draw first; the hovered and the clicked one draw last. Every box is HudPainter.NameBox,
// the speech bubble's fill and gold rim with rounded corners, highlighted under the cursor. Classic (classicNamePlates)
// is the original's layout: the HUD text size and the life bar while the cursor is on the monster. Otherwise the same box
// holds smaller text, and the bar shows once the monster was clicked or the local player swung at it, not for the
// cursor alone.
public sealed partial class HudCombat
{
    // Classic: the frame's top this far above the head point. Compact: the fill's bottom this far above it.
    private const int ClassicPlateLift = 36;
    private const int PlateLift = 12;
    private const int PlatePadY = 1;
    private const float PlateTextScale = 12f / HudPainter.BakePixels;
    // The original's 100x16 life bar drawn at 0.6 x 0.7.
    private const int PlateBarWidth = 60;
    private const int PlateBarHeight = 11;
    private readonly List<(ulong Id, HudOverheadPoint Point, bool Focus)> _plates = new();

    // Whether a player is in the local player's party.
    public Func<ulong, bool> InParty { get; set; } = static _ => false;

    // Whether a monster's name box carries its life bar.
    public static bool ShowsPlateBar(bool classic, bool hovered, bool traced, bool attacked) => classic ? hovered : traced || attacked;

    public void DrawNamePlates(HudPainter painter, HudLayout layout, Func<ulong, HudOverheadPoint?> project, ulong hovered, ulong traced, bool classic)
    {
        _plates.Clear();
        foreach (var (id, entity) in _entities)
        {
            var focus = id == hovered || id == traced;
            var kind = entity.Spawn.Kind;
            var member = kind == EntityKind.Player && InParty(id);
            var mate = kind == EntityKind.Player && id != _self && OwnClan != 0 && ClanOf(id) == OwnClan;
            var summon = kind == EntityKind.Monster && entity.Spawn.OwnerId != 0;
            var shown = kind == EntityKind.Npc || member || mate || kind == EntityKind.Player && id != _self && focus
                || (kind == EntityKind.Monster && entity.Life > 0 && (summon || focus));
            if (shown && project(id) is { } point && (focus || member || point.Depth < HudNameColors.OverheadDepth))
            {
                _plates.Add((id, point, focus));
            }
        }

        _plates.Sort(static (left, right) => left.Focus != right.Focus ? (left.Focus ? 1 : -1) : right.Point.Depth.CompareTo(left.Point.Depth));
        var scale = Math.Max(1, layout.Scale);
        foreach (var (id, point, _) in _plates)
        {
            var entity = _entities[id];
            var monster = entity.Spawn.Kind == EntityKind.Monster;
            var member = entity.Spawn.Kind == EntityKind.Player;
            var summon = monster && entity.Spawn.OwnerId != 0;
            var color = member || summon ? HudNameColors.Party : !monster ? HudNameColors.Npc : id == traced ? HudNameColors.HoveredMonster : HudNameColors.Monster;
            var owner = summon ? OwnerLabel(entity) : null;
            var clan = member || entity.Spawn.Kind == EntityKind.Player && (id == hovered || id == traced || OwnClan != 0 && ClanOf(id) == OwnClan)
                ? ClanLine(id)
                : null;
            var x = (int)(point.Screen.X / scale);
            var y = (int)(point.Screen.Y / scale);
            var ownSummon = summon && entity.Spawn.OwnerId == _self;
            if (classic)
            {
                DrawClassicPlate(painter, layout, Name(id), owner ?? clan, owner is null && clan is not null, x, y, color, id == hovered,
                    member || ownSummon || monster && !summon && ShowsPlateBar(true, id == hovered, id == traced, entity.Attacked) ? entity : null);
            }
            else
            {
                DrawPlate(painter, layout, Name(id), owner ?? clan, owner is null && clan is not null, x, y, color, id == hovered,
                    member || ownSummon || monster && !summon && ShowsPlateBar(false, id == hovered, id == traced, entity.Attacked) ? entity : null);
            }
        }
    }

    // The marker over the monster the player attacks, for a client that asks for it (the Android one): a red arrow in a
    // gold rim and a dark outline pointing down at the top of the monster's one-line name box, whether or not the box is
    // shown, bobbing and glowing on a loop. A dead or unseen monster, or one whose marker would leave the screen, has none.
    public void DrawTargetMarker(HudPainter painter, HudLayout layout, Func<ulong, HudOverheadPoint?> project, ulong target, long nowMs, bool classic)
    {
        if (target == 0 || !_entities.TryGetValue(target, out var entity) || entity.Spawn.Kind != EntityKind.Monster || entity.Life <= 0
            || project(target) is not { } point)
        {
            return;
        }

        var scale = Math.Max(1, layout.Scale);
        var x = (int)(point.Screen.X / scale);
        var anchorY = (int)(point.Screen.Y / scale);
        var plateTop = classic ? anchorY - ClassicPlateLift : anchorY - PlateLift - HudPainter.NameBoxExtent(0, painter.NameBoxText(PlateTextScale, PlatePadY).Line, 1).Y;
        var (sin, _) = MathF.SinCos(nowMs % MarkerLoopMs / (float)MarkerLoopMs * MathF.Tau);
        var pulse = .5f + .5f * sin;
        var tip = plateTop - MarkerGap + (int)MathF.Round(MarkerBob * sin);
        var top = tip - MarkerHeight - MarkerGlow;
        if (x - MarkerHalfWidth - MarkerGlow < 0 || x + MarkerHalfWidth + MarkerGlow >= layout.Width || top < 0 || tip + MarkerGlow >= layout.Height)
        {
            return;
        }

        painter.DownTriangle(x, tip + MarkerGlow, MarkerHalfWidth + MarkerGlow + 1, MarkerHeight + MarkerGlow * 2, MarkerGlowColor * (.2f + .3f * pulse));
        painter.DownTriangle(x, tip + 2, MarkerHalfWidth + 3, MarkerHeight + 4, MarkerOutline);
        painter.DownTriangle(x, tip + 1, MarkerHalfWidth + 2, MarkerHeight + 2, MarkerRim);
        painter.DownTriangle(x, tip, MarkerHalfWidth, MarkerHeight, Color.Lerp(MarkerRed, MarkerRedLit, pulse));
        painter.Fill(new Rectangle(x - MarkerHalfWidth + 2, tip - MarkerHeight + 2, 2 * MarkerHalfWidth - 3, 2), MarkerShine * (.5f + .5f * pulse));
    }

    // The target marker: the arrow's red body (half width and height), its gap above the name box, how far it bobs, how
    // far its glow reaches and how long one bob and glow take.
    private const int MarkerHalfWidth = 9;
    private const int MarkerHeight = 12;
    private const int MarkerGap = 3;
    private const float MarkerBob = 3f;
    private const int MarkerGlow = 3;
    private const int MarkerLoopMs = 1100;
    private static readonly Color MarkerGlowColor = new(255, 200, 90);
    private static readonly Color MarkerOutline = new(30, 12, 6);
    private static readonly Color MarkerRim = new(236, 196, 104);
    private static readonly Color MarkerRed = new(150, 20, 16);
    private static readonly Color MarkerRedLit = new(255, 72, 48);
    private static readonly Color MarkerShine = new(255, 170, 140);

    // "(owner)" under a summon's name, formatted once per summon; null while its owner is not in view.
    private string OwnerLabel(Entity summon)
    {
        if (summon.OwnerLabel is null && PlayerName(summon.Spawn.OwnerId) is { } owner)
        {
            summon.OwnerLabel = $"({owner})";
        }

        return summon.OwnerLabel;
    }

    // A box that would leave the screen is not drawn, as in the original. A second line (a summon's owner under the name,
    // a player's clan over it) grows the box upwards.
    private static void DrawClassicPlate(HudPainter painter, HudLayout layout, string name, string owner, bool clanLine, int anchorX, int anchorY, Color color,
        bool hovered, Entity bar)
    {
        var nameWidth = painter.Measure(name);
        var ownerWidth = owner is null ? 0 : painter.Measure(owner) + (clanLine ? ClanMarks.Span : 0);
        var width = Math.Max(nameWidth, ownerWidth);
        var lineHeight = Math.Max(16, painter.TextHeight);
        var extraLine = owner is null ? 0 : lineHeight;
        var frameHeight = 4 + lineHeight + extraLine + 6 + 2;
        var x = anchorX - width / 2 - 9;
        var y = anchorY - ClassicPlateLift - extraLine;
        if (x < 0 || y < 0 || x + width + 18 > layout.Width || y + frameHeight + PlateBarHeight > layout.Height)
        {
            return;
        }

        painter.Flush();
        painter.NameBox(new Rectangle(x, y, width + 18, frameHeight), hovered);
        painter.Flush();
        var nameTop = clanLine ? y + 5 + 2 + lineHeight : y + 5 + 2;
        painter.Text(name, anchorX - nameWidth / 2, nameTop, color, false);
        if (owner is not null && clanLine)
        {
            painter.ClanMark(anchorX - ownerWidth / 2, y + 5 + (lineHeight - ClanMarks.Size) / 2);
            painter.Text(owner, anchorX - ownerWidth / 2 + ClanMarks.Span, y + 5 + 2, HudNameColors.Clan, false);
        }
        else if (owner is not null)
        {
            painter.Text(owner, anchorX - ownerWidth / 2, y + 5 + 2 + lineHeight, HudNameColors.Player, false);
        }

        if (bar is not null)
        {
            DrawHealthBar(painter, new Rectangle(anchorX - PlateBarWidth / 2, y + frameHeight - 2, PlateBarWidth, PlateBarHeight), bar.Life, bar.Spawn.MaxLife);
        }
    }

    // The name box around the smaller text, the capitals centred inside with room for descenders.
    private static void DrawPlate(HudPainter painter, HudLayout layout, string name, string owner, bool clanLine, int anchorX, int anchorY, Color color, bool hovered,
        Entity bar)
    {
        var nameWidth = painter.MeasureAt(name, PlateTextScale);
        var ownerWidth = owner is null ? 0 : painter.MeasureAt(owner, PlateTextScale) + (clanLine ? ClanMarks.Span : 0);
        var (line, textTop) = painter.NameBoxText(PlateTextScale, PlatePadY);
        var (width, height) = HudPainter.NameBoxExtent(Math.Max(nameWidth, ownerWidth), line, owner is null ? 1 : 2);
        var x = anchorX - width / 2;
        var y = anchorY - PlateLift - height;
        if (x < 0 || y < 0 || x + width > layout.Width || y + height + 1 + PlateBarHeight > layout.Height)
        {
            return;
        }

        painter.DrawNameBox(new Rectangle(x, y, width, height), hovered);
        var top = y + textTop;
        painter.Text(name, anchorX - nameWidth / 2, clanLine ? top + line : top, color, PlateTextScale);
        if (owner is not null && clanLine)
        {
            painter.ClanMark(anchorX - ownerWidth / 2, y + HudPainter.NameBoxTop + (line - ClanMarks.Size) / 2);
            painter.Text(owner, anchorX - ownerWidth / 2 + ClanMarks.Span, top, HudNameColors.Clan, PlateTextScale);
        }
        else if (owner is not null)
        {
            painter.Text(owner, anchorX - ownerWidth / 2, top + line, HudNameColors.Player, PlateTextScale);
        }

        if (bar is not null)
        {
            DrawHealthBar(painter, new Rectangle(anchorX - PlateBarWidth / 2, y + height + 1, PlateBarWidth, PlateBarHeight), bar.Life, bar.Spawn.MaxLife);
        }
    }
}
