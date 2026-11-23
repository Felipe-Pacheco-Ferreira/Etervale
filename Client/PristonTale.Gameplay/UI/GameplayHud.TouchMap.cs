using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using PristonTale.Assets;
using PristonTale.Contracts.Units;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.UI;

// The touch HUD's field map, drawn by the page as the Android client's (touch-map.js): a tap on the minimap opens it. The
// game hands out the field's map picture and size and, in the map's own units (x east and y south of its top left
// corner), the player, the NPCs in view, the guide's route and the journey's marks.
public sealed partial class GameplayHud
{
    // An NPC this near the journey guide's place is the guide, in world units.
    private const float TouchGuideReach = 48f;

    private readonly List<HudNpcMark> _touchNpcs = new(64);
    private WorldCatalog _touchFields;

    // Grows each time the minimap was tapped while the page draws the map.
    private int _touchMapRequests;

    // The quest guide's route while it walks one (GameplayScreen sets it every update), on both maps.
    internal HudMinimapRoute QuestRoute { get; set; }

    private bool ClickTouchMap(HudLayout layout, Point pointer)
    {
        if (!TouchHudShown || !MinimapShown || !HudMinimap.Box(layout).Contains(pointer))
        {
            return false;
        }

        _touchMapRequests++;
        return true;
    }

    // The journey's hunting area in this field while its hunt counts, in render space.
    private (Vector3 Center, float Radius)? JourneyHuntArea()
    {
        if (!JourneyHuntCounts(_questView) || AdventurerJourneyRoute.For(_class).HuntAreaIn(World.FieldCode) is not { } area)
        {
            return null;
        }

        return (HudMinimap.RenderPoint(new RoutePoint(World.FieldCode, area.X, area.Z)), area.Radius);
    }

    // How many times the minimap was tapped and whether there is a map; with full, the map itself.
    internal string TouchMap(bool full)
    {
        var world = World;
        var shown = MinimapShown && world.FieldId is not null && world.MapRectangle.Right > world.MapRectangle.Left
            && world.MapRectangle.Bottom > world.MapRectangle.Top;
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("requests", _touchMapRequests);
            json.WriteBoolean("shown", shown);
            if (full && shown)
            {
                WriteTouchMap(json, world);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private void WriteTouchMap(Utf8JsonWriter json, HudWorldView world)
    {
        var rectangle = world.MapRectangle;
        var fields = _touchFields ??= new WorldCatalog();
        var route = AdventurerJourneyRoute.For(_class);
        json.WriteString("field", world.FieldId);
        json.WriteString("title", fields.FieldName(world.FieldCode));
        json.WriteString("picture", TouchPicturePath(AssetFiles.Combine(_context.AssetRoot, HudArt.FieldMap(world.FieldId))));
        json.WriteNumber("width", (rectangle.Right - rectangle.Left) / GameUnits.FixedScale);
        json.WriteNumber("height", (rectangle.Bottom - rectangle.Top) / GameUnits.FixedScale);
        json.WriteStartObject("player");
        WriteMapPoint(json, rectangle, world.PlayerPosition);
        json.WriteNumber("facing", HudMinimap.Heading(world.PlayerForward));
        json.WriteEndObject();

        Vector3? guide = route.Guide.FieldCode == world.FieldCode ? HudMinimap.RenderPoint(route.Guide) : null;
        var guideName = route.GuideName;
        Combat.CollectNpcMarks(_touchNpcs);
        json.WriteStartArray("npcs");
        foreach (var npc in _touchNpcs)
        {
            var isGuide = guide is { } place && Vector2.Distance(new Vector2(place.X, place.Z), new Vector2(npc.Position.X, npc.Position.Z)) < TouchGuideReach;
            guideName = isGuide ? npc.Name : guideName;
            json.WriteStartObject();
            json.WriteString("name", npc.Name);
            WriteMapPoint(json, rectangle, npc.Position);
            json.WriteBoolean("guide", isGuide);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("route");
        var walked = QuestRoute;
        for (var index = walked.Shown ? walked.From : 0; walked.Shown && index < walked.Points.Count; index++)
        {
            var point = HudMinimap.MapPoint(rectangle, new Vector3(walked.Points[index].X, 0f, walked.Points[index].Y));
            json.WriteNumberValue(point.X);
            json.WriteNumberValue(point.Y);
        }

        json.WriteEndArray();
        if (JourneyHuntArea() is { } hunt)
        {
            json.WriteStartObject("hunt");
            WriteMapPoint(json, rectangle, hunt.Center);
            json.WriteNumber("radius", hunt.Radius);
            json.WriteEndObject();
        }
        else if (JourneyHuntCounts(_questView) && route.ExitTowards(world.FieldCode, route.HuntField) is { } exit)
        {
            json.WriteStartObject("exit");
            WriteMapPoint(json, rectangle, HudMinimap.RenderPoint(exit.Point));
            json.WriteString("to", fields.FieldName(exit.To));
            json.WriteEndObject();
        }

        if (guide is { } spot)
        {
            json.WriteStartObject("guide");
            WriteMapPoint(json, rectangle, spot);
            json.WriteString("name", guideName);
            json.WriteEndObject();
        }

        WriteTouchQuestCount(json, _questView);
        json.WriteString("huntField", JourneyHuntCounts(_questView) ? fields.FieldName(route.HuntField) : "");
    }

    private static void WriteMapPoint(Utf8JsonWriter json, PristonIntRectangle rectangle, Vector3 position)
    {
        var point = HudMinimap.MapPoint(rectangle, position);
        json.WriteNumber("x", point.X);
        json.WriteNumber("y", point.Y);
    }
}
