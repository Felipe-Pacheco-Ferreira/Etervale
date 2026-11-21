namespace PristonTale.Simulation.Social;

// Which feats of a player the server tells everybody: an aging that rose to a high level and a level that is a
// milestone, never an ordinary aging or every level.
public static class ServerNoticeRules
{
    public const int DefaultAgingFrom = 10;
    public const int DefaultLevelFrom = 100;
    public const int DefaultLevelEvery = 10;

    // An aging that rose to level (a success or a double) is told from agingFrom on; agingFrom 0 tells none.
    public static bool TellsAging(int level, int agingFrom) => agingFrom > 0 && level >= agingFrom;

    // The highest milestone passed going from level from to level to: a multiple of every, from levelFrom on; 0 for none
    // (levelFrom 0 tells none).
    public static int LevelMilestone(int from, int to, int levelFrom, int every)
    {
        if (levelFrom <= 0 || every <= 0 || to <= from)
        {
            return 0;
        }

        var milestone = to / every * every;
        return milestone > from && milestone >= levelFrom ? milestone : 0;
    }
}
