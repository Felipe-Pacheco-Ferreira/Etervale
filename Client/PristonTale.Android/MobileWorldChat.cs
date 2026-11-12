using Android.App;
using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Mobile.CharacterSelect;
using PristonTale.Simulation.Social;

namespace PristonTale.Mobile;

internal sealed class MobileWorldChat
{
    private const int HistoryLimit = 80;
    private const int BubbleLimit = 8;
    private readonly Activity _activity;
    private readonly string _characterName;
    private readonly IPlayChannel _play;
    private readonly FrameLayout _world;
    private readonly RicartenSceneView _map;
    private readonly CancellationToken _token;
    private readonly Queue<(string Text, ChatKind Kind)> _history = new();
    private readonly Dictionary<ulong, (NpcSpeechBubble View, long Until)> _bubbles = new();
    private readonly List<ulong> _expired = new();
    private readonly Java.Lang.Runnable _refreshBubbles;
    private readonly TextView _transcript;
    private readonly ScrollView _scroll;
    private readonly LinearLayout _composer;
    private readonly EditText _line;
    private readonly Button _channel;
    private readonly Button _send;
    private bool _party;
    private bool _closed;
    public ulong SelfId { get; set; }
    public LinearLayout View { get; }

    public MobileWorldChat(Activity activity, IPlayChannel play, FrameLayout world, RicartenSceneView map, string characterName, CancellationToken token)
    {
        _activity = activity;
        _characterName = characterName;
        _play = play;
        _world = world;
        _map = map;
        _token = token;
        _refreshBubbles = new Java.Lang.Runnable(UpdateBubbles);
        View = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        var toggle = MobileHudTheme.Button(activity, "Chat");
        MobileHudTheme.Icon(toggle, HudGlyph.Chat);
        View.AddView(toggle, new LinearLayout.LayoutParams(Dp(76), Dp(32)));
        _transcript = new TextView(activity) { TextSize = 12 };
        _transcript.SetShadowLayer(2f, 0f, 1f, Color.Black);
        _scroll = new ScrollView(activity) { Visibility = ViewStates.Gone };
        _scroll.AddView(_transcript);
        View.AddView(_scroll, new LinearLayout.LayoutParams(-1, Dp(62)));

        _composer = new LinearLayout(activity) { Orientation = Orientation.Horizontal, Visibility = ViewStates.Gone };
        MobileHudTheme.Card(_composer);
        _line = new EditText(activity) { Hint = "Mensagem", TextSize = 12, InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences };
        _line.SetTextColor(Color.White);
        _line.SetHintTextColor(MobileHudTheme.Ink);
        _line.SetSingleLine();
        _line.ImeOptions = global::Android.Views.InputMethods.ImeAction.Send;
        _line.SetFilters([new InputFilterLengthFilter(ChatRules.InputLimit)]);
        _composer.AddView(_line, new LinearLayout.LayoutParams(0, -2, 1f));
        _channel = MobileHudTheme.Button(activity, "Local");
        _channel.Click += (_, _) =>
        {
            _party = !_party;
            _channel.Text = _party ? "Grupo" : "Local";
        };
        _composer.AddView(_channel);
        _send = MobileHudTheme.Button(activity, "Enviar");
        _send.Click += async (_, _) => await SendAsync();
        _line.EditorAction += async (_, args) =>
        {
            if (args.ActionId == global::Android.Views.InputMethods.ImeAction.Send)
            {
                args.Handled = true;
                await SendAsync();
            }
        };
        _composer.AddView(_send);
        View.AddView(_composer);
        toggle.Click += (_, _) =>
        {
            var open = _composer.Visibility != ViewStates.Visible;
            _composer.Visibility = open ? ViewStates.Visible : ViewStates.Gone;
            Refresh();
        };
    }

    public void Observe(IReadOnlyList<ChatLineInfo> chat)
    {
        if (chat.Count == 0 || _closed)
        {
            return;
        }
        foreach (var entry in chat)
        {
            var text = entry.Line.IsNone ? entry.Text : CharSelectText.Format(entry.Line);
            AddLine(text, entry.Kind);
            if (entry.Kind == ChatKind.Player && entry.SpeakerId != 0 && !string.IsNullOrWhiteSpace(text))
            {
                Say(entry.SpeakerId, text);
            }
        }
        Refresh();
    }

    private void AddLine(string text, ChatKind kind)
    {
        _history.Enqueue((text, kind));
        if (_history.Count > HistoryLimit)
        {
            _history.Dequeue();
        }
    }

    private void Say(ulong speaker, string text)
    {
        var start = _bubbles.Count == 0;
        if (!_bubbles.TryGetValue(speaker, out var bubble))
        {
            if (_bubbles.Count >= BubbleLimit)
            {
                RemoveBubble(_bubbles.MinBy(entry => entry.Value.Until).Key);
            }
            bubble.View = new NpcSpeechBubble(_activity) { TextSize = 12, Visibility = ViewStates.Invisible };
            bubble.View.SetMaxLines(3);
            _world.AddView(bubble.View, new FrameLayout.LayoutParams(-2, -2));
        }
        bubble.View.Text = text;
        bubble.Until = Environment.TickCount64 + (speaker == SelfId ? ChatRules.SelfBubbleMs : ChatRules.OtherBubbleMs);
        _bubbles[speaker] = bubble;
        if (start)
        {
            _world.PostDelayed(_refreshBubbles, 33);
        }
    }

    private void UpdateBubbles()
    {
        _expired.Clear();
        foreach (var (id, bubble) in _bubbles)
        {
            if (Environment.TickCount64 >= bubble.Until)
            {
                _expired.Add(id);
                continue;
            }
            var view = bubble.View;
            var visible = _map.TrySpeakerHead(id, SelfId, out var point);
            view.Visibility = visible ? ViewStates.Visible : ViewStates.Invisible;
            if (visible)
            {
                view.TranslationX = Math.Clamp(point.X - view.Width * .5f, 0f, Math.Max(0, _world.Width - view.Width));
                view.TranslationY = Math.Max(0, point.Y - view.Height);
            }
        }
        foreach (var id in _expired)
        {
            RemoveBubble(id);
        }
        if (_bubbles.Count > 0 && !_closed)
        {
            _world.PostDelayed(_refreshBubbles, 33);
        }
    }

    private void RemoveBubble(ulong id)
    {
        var view = _bubbles[id].View;
        _bubbles.Remove(id);
        _world.RemoveView(view);
        view.Dispose();
    }

    private async Task SendAsync()
    {
        var text = _line.Text?.Trim() ?? "";
        if (text.Length == 0 || !_send.Enabled)
        {
            return;
        }
        _send.Enabled = false;
        try
        {
            var party = _party;
            await _play.SendChatAsync(text, party, _token);
            if (!_closed && !ChatRules.IsCommand(ChatRules.WithoutRoute(text)))
            {
                var shown = ChatRules.Compose(_characterName, text);
                AddLine(shown, party ? ChatKind.Party : ChatKind.Player);
                if (!party && SelfId != 0)
                {
                    Say(SelfId, shown);
                }
                Refresh();
            }
            if (!_closed && _line.Text?.Trim() == text)
            {
                _line.Text = "";
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            Toast.MakeText(_activity, "Não foi possível enviar a mensagem.", ToastLength.Short)?.Show();
        }
        finally
        {
            if (!_closed)
            {
                _send.Enabled = true;
            }
        }
    }

    public void Close()
    {
        _closed = true;
        _world.RemoveCallbacks(_refreshBubbles);
        foreach (var id in _bubbles.Keys.ToArray())
        {
            RemoveBubble(id);
        }
        _refreshBubbles.Dispose();
    }

    private void Refresh()
    {
        var expanded = _composer.Visibility == ViewStates.Visible;
        _scroll.Visibility = expanded || _history.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
        using var text = new SpannableStringBuilder();
        foreach (var (line, kind) in expanded ? _history.AsEnumerable() : _history.TakeLast(3))
        {
            var start = text.Length();
            text.Append(line);
            if (text.Length() > start)
            {
                var color = kind switch
                {
                    ChatKind.Party => Color.Rgb(121, 224, 255),
                    ChatKind.Whisper => Color.Rgb(244, 177, 255),
                    ChatKind.Clan => Color.Rgb(168, 243, 115),
                    ChatKind.Player => Color.White,
                    _ => MobileHudTheme.Gold
                };
                text.SetSpan(new ForegroundColorSpan(color), start, text.Length(), SpanTypes.ExclusiveExclusive);
            }
            text.Append("\n");
        }
        _transcript.SetText(text, TextView.BufferType.Spannable);
        _scroll.Post(() => { if (!_closed) { _scroll.FullScroll(FocusSearchDirection.Down); } });
    }

    private int Dp(int value) => (int)MathF.Round(value * (_activity.Resources?.DisplayMetrics?.Density ?? 1f));
}
