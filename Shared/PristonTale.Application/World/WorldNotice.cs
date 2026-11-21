using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

// A line that a world change answered (a party or a trade), for one player or, To Everyone, for every player in the
// chat, delivered through the chat after the world answered (ChatDirectory.Deliver).
public readonly record struct WorldNotice(ulong To, ChatKind Kind, GameText Text)
{
    public const ulong Everyone = 0;
}
