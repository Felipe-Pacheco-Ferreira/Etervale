using System.Text;
using Android.App;
using Android.Widget;
using PristonTale.Client.Net;
using PristonTale.Contracts.Texts;
using PristonTale.Mobile.CharacterSelect;
using PristonTale.Simulation.Quests;

namespace PristonTale.Mobile;

internal sealed class MobileQuestDialog(Activity activity, IPlayChannel play, TextView notice)
{
    private static readonly Encoding DocumentEncoding = CreateEncoding();

    public Task<bool> TalkAsync(ulong npc, string name, CancellationToken ct) => RunAsync(npc, name, null, ct);

    public async void ShowWorldPrompt(QuestPrompt prompt, CancellationToken ct)
    {
        try
        {
            await RunAsync(0, "Missão", prompt, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            global::Android.Util.Log.Warn("Etervale.World", ex.ToString());
            notice.Text = "Não foi possível abrir o diálogo de missão.";
        }
    }

    private async Task<bool> RunAsync(ulong npc, string name, QuestPrompt? incoming, CancellationToken ct)
    {
        var action = QuestAction.Talk;
        var code = QuestCode.None;
        var state = 0;
        var choice = 0;
        var answered = false;
        while (true)
        {
            var outcome = incoming is null
                ? await play.RequestQuestAsync(new QuestCommand(Guid.NewGuid(), npc, action, code, state, choice), ct)
                : new QuestOutcome(true, QuestStatus.Ok, GameText.None, 0, incoming, []);
            incoming = null;
            if (!outcome.Message.IsNone)
            {
                notice.Text = CharSelectText.Format(outcome.Message);
            }

            if (outcome.Prompt is not { } prompt)
            {
                if (outcome.ClassChangeOffered && action == QuestAction.Talk
                    && await AskAsync(name, "Mudança de classe disponível.", ["Mudar de classe"], ct) == 0)
                {
                    action = QuestAction.ChangeClass;
                    answered = true;
                    continue;
                }
                return answered || outcome.ClassChangeOffered;
            }

            answered = true;
            code = prompt.Code;
            state = prompt.State;
            var title = prompt.Title.IsNone ? name : CharSelectText.Format(prompt.Title);
            var document = ReadDocument(prompt.Document, CharSelectText.Format(prompt.Subject));
            switch (prompt.Followup)
            {
                case QuestFollowup.ChooseFromList:
                    if (await AskAsync(title, document, prompt.Choices.ToArray(), ct) is not { } index)
                    {
                        return true;
                    }
                    action = QuestAction.ChooseReward;
                    choice = index + 1;
                    break;
                case QuestFollowup.ConfirmChoice:
                    if (await AskAsync(title, document, prompt.Choices.ToArray(), ct) is not { } reward)
                    {
                        return true;
                    }
                    var rewardDocument = reward < prompt.ChoiceDocuments.Count
                        ? ReadDocument(prompt.ChoiceDocuments[reward], CharSelectText.Format(prompt.Subject))
                        : document;
                    if (await AskAsync(title, rewardDocument + "\n\n" + prompt.Choices[reward], ["Confirmar"], ct) is null)
                    {
                        return true;
                    }
                    action = QuestAction.Accept;
                    choice = reward + 1;
                    break;
                case QuestFollowup.Confirm:
                    if (await AskAsync(title, document, ["OK"], ct) is null)
                    {
                        return true;
                    }
                    var confirmation = string.Join("\n", new[] { CharSelectText.Format(prompt.ConfirmLine1), CharSelectText.Format(prompt.ConfirmLine2),
                        CharSelectText.Format(prompt.ConfirmDetail) }.Where(line => line.Length > 0));
                    if (await AskAsync(title, confirmation, ["Sim"], ct) is null)
                    {
                        return true;
                    }
                    action = QuestAction.Accept;
                    choice = prompt.Choice;
                    break;
                case QuestFollowup.Proceed:
                    if (await AskAsync(title, document, ["OK"], ct) is null)
                    {
                        return true;
                    }
                    action = QuestAction.Proceed;
                    choice = 0;
                    break;
                default:
                    await AskAsync(title, document, ["OK"], ct);
                    return true;
            }
        }
    }

    private async Task<int?> AskAsync(string title, string message, string[] choices, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var builder = new AlertDialog.Builder(activity);
        builder.SetTitle(title);
        if (choices.Length == 1)
        {
            builder.SetMessage(message);
            builder.SetPositiveButton(choices[0], (_, _) => answer.TrySetResult(0));
        }
        else
        {
            var rows = string.IsNullOrWhiteSpace(message) ? choices : [message, .. choices];
            builder.SetItems(rows, (_, args) =>
            {
                var index = args.Which - (rows.Length - choices.Length);
                answer.TrySetResult(index < 0 ? null : index);
            });
        }
        builder.SetNegativeButton("Fechar", (_, _) => answer.TrySetResult(null));
        using var dialog = builder.Create()!;
        dialog.DismissEvent += (_, _) => answer.TrySetResult(null);
        dialog.Show();
        try
        {
            return await answer.Task.WaitAsync(ct);
        }
        finally
        {
            dialog.Dismiss();
        }
    }

    private string ReadDocument(string document, string subject)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            return "";
        }

        var path = document.Replace('\\', '/').ToLowerInvariant();
        try
        {
            using var stream = activity.Assets!.Open($"QuestDialogs/{path}.sin");
            using var reader = new StreamReader(stream, DocumentEncoding);
            return reader.ReadToEnd().Replace("$$%s", subject, StringComparison.Ordinal);
        }
        catch (Java.IO.IOException)
        {
            return "Texto da missão indisponível.";
        }
    }

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
