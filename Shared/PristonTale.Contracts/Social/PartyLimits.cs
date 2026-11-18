namespace PristonTale.Contracts.Social;

// The original's party sizes, shared by its rules, its messages and the HUD: the members of one party (the leader
// first), the planar distance within which a member shares a kill's experience and gold, and the shorter one within which
// the minimap draws a member in its own colour (red beyond it).
public static class PartyLimits
{
    public const int MaxMembers = 6;
    public const float ShareRange = 18 * 64;
    public const float NearRange = 17 * 64;
    // Fits a complete response with maximum-length UTF-8 names inside the 8192-byte protocol frame.
    public const int MaxDirectoryEntries = 80;
}

// What a party member asks of its party, as the party window's buttons number them. The first three are the leader's.
public enum PartyCommand : byte
{
    Delegate = 0,
    Disband = 1,
    Kick = 2,
    Leave = 3,
    Create = 4,
    Open = 5,
    Close = 6,
    Search = 7,
    Join = 8
}

// A server-local, authoritative group listing. Joining rechecks its current leader, capacity and level rules.
public sealed record PartyDirectoryEntry(ulong LeaderId, string LeaderName, int FieldId, int AverageLevel, int MemberCount, bool IsOpen);
