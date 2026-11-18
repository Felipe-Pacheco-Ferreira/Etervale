using System.Numerics;
using PristonTale.Application.Chat;

namespace PristonTale.Application.GameMasters;

// A game master acting from outside the world (the Discord bot): the name and user id the audit trail records, and the
// level the bot granted from the member's Discord role.
public sealed record RemoteGameMaster(string UserId, string Name, int Level);

public enum GmCommandOutcome
{
    // No command starts the line.
    Unknown,

    // The command needs the issuer's character in the world, which a remote game master does not have.
    NeedsWorld,

    Denied,
    Done
}

public sealed record GmCommandResult(GmCommandOutcome Outcome, IReadOnlyList<string> Replies);

// The connection of a remote game master: replies are collected by the command context, and there is no client to warp,
// close or disconnect.
internal sealed class DetachedChatLink : IChatLink
{
    public static readonly DetachedChatLink Instance = new();

    public void Deliver(ChatLine line)
    {
    }

    public void Warp(int fieldId, Vector3 position)
    {
    }

    public void RequestClose()
    {
    }

    public void Disconnect(string reason)
    {
    }

    public string RemoteAddress => "remote";

    public long PacketsReceived => 0;

    public long PacketsSent => 0;
}
