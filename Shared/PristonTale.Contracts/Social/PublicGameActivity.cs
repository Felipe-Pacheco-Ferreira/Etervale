namespace PristonTale.Contracts.Social;

public sealed record PublicGameMessage(long Sequence, DateTimeOffset At, string Channel, string Text);

public sealed record PublicGameActivity(Guid StreamId, DateTimeOffset UpdatedAt, int OnlinePlayers, PublicGameMessage[] Messages);
