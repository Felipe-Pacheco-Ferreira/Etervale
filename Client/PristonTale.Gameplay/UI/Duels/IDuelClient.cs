namespace PristonTale.Gameplay.UI.Duels;

// Answers a challenge to a duel; a challenge itself is the chat line "/duelo name".
public interface IDuelClient
{
    Task AnswerDuelAsync(ulong fromId, bool accept);
}
