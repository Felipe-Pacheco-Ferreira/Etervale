using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Quests;

// Where the Adventurer Journey happens, for each tribe: its guide, the field it hunts in, how to walk between the two and
// the spawn points worth visiting there. The server names the guide from it; the Android client's automatic quest walks
// it. Points are a field's own planar (x, z), as field definitions hold them.
public static class AdventurerJourneyRoute
{
    public const QuestCode Code = QuestCode.AdventurerJourney;
    public static readonly uint Crystal = ItemCodes.Parse("GP101");

    // The quest view's welcome objective, shown before the first talk to the guide.
    public const string MeetSkadiDescription = "Avalon/MeetSkadi.msg";
    public const string MeetJinDescription = "Avalon/MeetJin.msg";

    public static TribeRoute Tempskron { get; } = new(
        "Adventurer Skadi",
        new RoutePoint(FieldCodes.Ricarten, 506383 / 256f, -4755883 / 256f),
        HuntField: 2,
        Exits:
        [
            // Ricarten and Forest 1 meet at a seam: walking on crosses into the forest, and back.
            new RouteExit(FieldCodes.Ricarten, 2, new RoutePoint(FieldCodes.Ricarten, 2330, -14600), Seam: true),
            new RouteExit(2, FieldCodes.Ricarten, new RoutePoint(2, 2230, -15100), Seam: true)
        ],
        HuntPoints:
        [
            new RoutePoint(2, 2722, -13299), new RoutePoint(2, 2533, -13165), new RoutePoint(2, 2359, -13139),
            new RoutePoint(2, 2936, -12947), new RoutePoint(2, 2797, -12864), new RoutePoint(2, 3283, -13037),
            new RoutePoint(2, 1587, -12885), new RoutePoint(2, 1602, -12799)
        ]);

    public static TribeRoute Morion { get; } = new(
        "Adventurer Jin",
        new RoutePoint(FieldCodes.Pillai, 231628 / 256f, 19394721 / 256f),
        HuntField: FieldCodes.ForeverFall1,
        Exits:
        [
            // Pillai and Forever Fall 1 are joined by warp gates: stepping on one sends the player to the other field.
            new RouteExit(FieldCodes.Pillai, FieldCodes.ForeverFall1, new RoutePoint(FieldCodes.Pillai, 2000, 72907), Seam: false),
            new RouteExit(FieldCodes.ForeverFall1, FieldCodes.Pillai, new RoutePoint(FieldCodes.ForeverFall1, 1962, 71184), Seam: false)
        ],
        HuntPoints:
        [
            new RoutePoint(FieldCodes.ForeverFall1, 3801, 68852), new RoutePoint(FieldCodes.ForeverFall1, 3638, 68715),
            new RoutePoint(FieldCodes.ForeverFall1, 3894, 68663), new RoutePoint(FieldCodes.ForeverFall1, 3811, 68551),
            new RoutePoint(FieldCodes.ForeverFall1, 3796, 68463), new RoutePoint(FieldCodes.ForeverFall1, 4071, 68663),
            new RoutePoint(FieldCodes.ForeverFall1, 3999, 68502), new RoutePoint(FieldCodes.ForeverFall1, 1406, 67011)
        ]);

    public static TribeRoute For(PlayerClass playerClass) =>
        ClassProfiles.Of(playerClass).Brood == CharacterBrood.Tempskron ? Tempskron : Morion;

    public static TribeRoute For(bool tempskron) => tempskron ? Tempskron : Morion;
}

public readonly record struct RoutePoint(int FieldCode, float X, float Z);

// A way from one field to the next: walk to Point, in the field it leaves. A seam is crossed by walking on; a warp gate
// moves the player once it is reached.
public sealed record RouteExit(int From, int To, RoutePoint Point, bool Seam);

public sealed record TribeRoute(string GuideName, RoutePoint Guide, int HuntField, IReadOnlyList<RouteExit> Exits, IReadOnlyList<RoutePoint> HuntPoints)
{
    // The exit to take from a field towards another, or null when there is none.
    public RouteExit ExitTowards(int from, int to)
    {
        foreach (var exit in Exits)
        {
            if (exit.From == from && exit.To == to)
            {
                return exit;
            }
        }

        return null;
    }
}
