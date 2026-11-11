using Android.App;
using Android.Text;
using Android.Widget;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Mobile.CharacterSelect;
using PristonTale.Simulation.Social;

namespace PristonTale.Mobile;

internal sealed class MobileWorldChat(Activity activity, IPlayChannel play)
{
    private const int HistoryLimit = 80;
    private readonly Queue<string> _history = new();
    private TextView? _transcript;
    private ScrollView? _scroll;
    private AlertDialog? _dialog;

    public void Observe(IReadOnlyList<ChatLineInfo> chat)
    {
        if (chat.Count == 0)
        {
            return;
        }
        foreach (var entry in chat)
        {
            _history.Enqueue(entry.Line.IsNone ? entry.Text : CharSelectText.Format(entry.Line));
            if (_history.Count > HistoryLimit)
            {
                _history.Dequeue();
            }
        }
        Refresh();
    }

    public void Open(CancellationToken ct)
    {
        if (_dialog is not null)
        {
            return;
        }

        var body = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        var transcript = new TextView(activity) { TextSize = 14 };
        transcript.SetTextIsSelectable(true);
        var scroll = new ScrollView(activity);
        scroll.AddView(transcript);
        body.AddView(scroll, new LinearLayout.LayoutParams(-1, (int)(144 * activity.Resources!.DisplayMetrics!.Density)));

        var line = new EditText(activity) { Hint = "Mensagem", InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences };
        line.SetSingleLine();
        line.SetFilters([new InputFilterLengthFilter(ChatRules.InputLimit)]);
        body.AddView(line);
        var party = new CheckBox(activity) { Text = "Grupo" };
        body.AddView(party);
        var send = new Button(activity) { Text = "Enviar" };
        body.AddView(send);
        send.Click += async (_, _) =>
        {
            var text = line.Text?.Trim() ?? "";
            if (text.Length == 0)
            {
                return;
            }

            send.Enabled = false;
            try
            {
                await play.SendChatAsync(text, party.Checked, ct);
                if (_dialog is not null)
                {
                    line.Text = "";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
            {
                Toast.MakeText(activity, "Não foi possível enviar a mensagem.", ToastLength.Short)?.Show();
            }
            finally
            {
                if (_dialog is not null)
                {
                    send.Enabled = true;
                }
            }
        };

        using var builder = new AlertDialog.Builder(activity);
        builder.SetTitle("Chat");
        builder.SetView(body);
        builder.SetNegativeButton("Fechar", (_, _) => { });
        var dialog = builder.Create()!;
        _dialog = dialog;
        _transcript = transcript;
        _scroll = scroll;
        dialog.DismissEvent += (_, _) =>
        {
            _dialog = null;
            _transcript = null;
            _scroll = null;
            dialog.Dispose();
        };
        dialog.Show();
        Refresh();
    }

    public void Close() => _dialog?.Dismiss();

    private void Refresh()
    {
        if (_transcript is { } transcript && _scroll is { } scroll)
        {
            transcript.Text = string.Join("\n", _history);
            scroll.Post(() => scroll.FullScroll(global::Android.Views.FocusSearchDirection.Down));
        }
    }
}
