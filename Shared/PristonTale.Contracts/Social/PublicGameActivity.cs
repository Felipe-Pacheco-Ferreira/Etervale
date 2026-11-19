using PristonTale.Contracts.Characters;

namespace PristonTale.Contracts.Social;

// A line of the game's public activity; a feat (a boss slain, a player killed) carries its details for the Discord feeds.
public sealed record PublicGameMessage(long Sequence, DateTimeOffset At, string Channel, string Text, GameFeat? Feat = null);

public sealed record PublicGameActivity(Guid StreamId, DateTimeOffset UpdatedAt, int OnlinePlayers, PublicGameMessage[] Messages);

// A boss slain (Kind "boss": Target and its level, the killer first in Players and the party members in the field after
// it, the drops) or a player killed by another (Kind "pvp": the killer in Players, the Victim). Names are in Portuguese.
public sealed record GameFeat(string Kind, string Field, string Target, int TargetLevel, FeatPlayer[] Players, FeatPlayer? Victim, string[] Drops)
{
    public const string Boss = "boss";
    public const string Pvp = "pvp";
}

public sealed record FeatPlayer(string Name, int Level, PlayerClass Class, int Tier, string Clan);
