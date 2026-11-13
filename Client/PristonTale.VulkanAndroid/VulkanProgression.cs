using PristonTale.Client.Net;
using PristonTale.Gameplay.Localization;
using PristonTale.Simulation.Progression;

// A progression command (an attribute point, a skill rank) sent from a HUD window: the server's answer within
// AnswerSeconds, its refusal as text (refused when it gave none), or the connection error. A window waits for the owner
// state at the answer's revision before it sends again.
internal static class VulkanProgression
{
    private const int AnswerSeconds = 8;

    internal readonly record struct Answer(bool Ok, long Revision, string Refusal);

    public static async Task<Answer> ApplyAsync(IPlayChannel play, ProgressionCommand command, string refused)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(AnswerSeconds));
        try
        {
            var outcome = await play.ApplyProgressionAsync(command, timeout.Token);
            return new Answer(outcome.Ok, outcome.Revision, outcome.Ok ? null : outcome.Message.IsNone ? refused : ClientText.Format(outcome.Message));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            return new Answer(false, command.ExpectedRevision, ClientText.Get("Progression.ConnectionError"));
        }
    }
}
