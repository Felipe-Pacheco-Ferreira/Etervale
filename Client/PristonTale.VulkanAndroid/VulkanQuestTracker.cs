using System.Numerics;
using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;
using PristonTale.Simulation.Quests;

// The quest card at the top right: the running quest's step, a bar for its kills, what the automatic quest is doing, a
// compass to the guide while it is paused, and the button that starts or pauses it. A tap on the card folds it down to the
// title over the kill bar (the title alone when nothing is counted) and opens it again.
internal sealed class VulkanQuestTracker
{
    private static readonly string[] Compass = ["↑ Norte", "↗ Nordeste", "→ Leste", "↘ Sudeste", "↓ Sul", "↙ Sudoeste", "← Oeste", "↖ Noroeste"];
    private const float NearGuide = 64f;

    private readonly VulkanWorldSession _session;
    private readonly TextView _title;
    private readonly TextView _objective;
    private readonly FrameLayout _bar;
    private readonly View _barFill;
    private readonly TextView _barLabel;
    private readonly TextView _status;
    private readonly Button _auto;
    private readonly float _density;
    private QuestView _shown;
    private string _titleText = "";
    private bool _collapsed;

    public VulkanQuestTracker(Activity activity, VulkanWorldSession session)
    {
        _session = session;
        _density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        View = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        View.SetPadding(Dp(9), Dp(7), Dp(9), Dp(8));
        View.Background = new PlateDrawable(Dp(10));
        View.Clickable = true;
        View.Click += (_, _) =>
        {
            Collapsed = !Collapsed;
            Folded?.Invoke(_collapsed);
        };

        _title = new TextView(activity) { TextSize = 12 };
        _title.SetTextColor(MobileHudTheme.Gold);
        _title.Typeface = Typeface.Create("serif", TypefaceStyle.Bold);
        _title.SetSingleLine(true);
        _title.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        View.AddView(_title);

        _objective = new TextView(activity) { TextSize = 11 };
        _objective.SetTextColor(MobileHudTheme.Ink);
        _objective.SetMaxLines(3);
        View.AddView(_objective, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(2) });

        _bar = new FrameLayout(activity) { Visibility = ViewStates.Gone };
        _bar.Background = Rounded(Color.Argb(200, 12, 12, 15), Color.Argb(160, 153, 120, 65));
        _barFill = new View(activity) { Background = Rounded(Color.Rgb(176, 64, 44), Color.Transparent) };
        _bar.AddView(_barFill, new FrameLayout.LayoutParams(0, -1));
        _barLabel = new TextView(activity) { TextSize = 10, Gravity = GravityFlags.Center };
        _barLabel.SetTextColor(Color.White);
        _barLabel.SetShadowLayer(2f, 0f, 1f, Color.Black);
        _bar.AddView(_barLabel, new FrameLayout.LayoutParams(-1, -1));
        View.AddView(_bar, new LinearLayout.LayoutParams(-1, Dp(14)) { TopMargin = Dp(5) });

        _status = new TextView(activity) { TextSize = 10 };
        _status.SetTextColor(Color.Rgb(168, 214, 199));
        _status.SetMaxLines(2);
        View.AddView(_status, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(4) });

        _auto = MobileHudTheme.Button(activity, "Auto-missão");
        MobileHudTheme.Icon(_auto, HudGlyph.Auto);
        _auto.Click += (_, _) =>
        {
            if (_session.Navigator is { } navigator)
            {
                navigator.SetEnabled(!navigator.Enabled);
                Refresh(force: true);
            }
        };
        View.AddView(_auto, new LinearLayout.LayoutParams(-1, Dp(36)) { TopMargin = Dp(6) });
    }

    public LinearLayout View { get; }

    public View AutoButton => _auto;

    // Raised when a tap folds or opens the card, with whether it is folded now.
    public event Action<bool> Folded;

    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            _collapsed = value;
            Refresh(force: true);
        }
    }

    // Called a few times a second on the UI thread.
    public void Refresh(bool force = false)
    {
        var quest = _session.Quest;
        var navigator = _session.Navigator;
        if (force || !ReferenceEquals(quest, _shown))
        {
            _shown = quest;
            ShowQuest(quest);
        }

        var running = navigator?.Enabled == true;
        var hasWork = navigator?.HasWork(quest) == true;
        _auto.Visibility = hasWork ? ViewStates.Visible : ViewStates.Gone;
        _auto.Text = running ? "Pausar auto-missão" : "Iniciar auto-missão";
        _status.Text = running ? navigator.Status : hasWork ? GuideDirection() ?? "Toque em Iniciar auto-missão para continuar" : "";
        _status.Visibility = _status.Text.Length > 0 ? ViewStates.Visible : ViewStates.Gone;
        if (_collapsed)
        {
            _auto.Visibility = ViewStates.Gone;
            _status.Visibility = ViewStates.Gone;
        }
    }

    private void ShowQuest(QuestView quest)
    {
        var step = quest?.Progress.Count > 0 ? quest.Progress[^1] : null;
        _titleText = step is null ? "Missões" : quest.Code == AdventurerJourneyRoute.Code || step.Description is
            AdventurerJourneyRoute.MeetSkadiDescription or AdventurerJourneyRoute.MeetJinDescription ? "Jornada do Aventureiro" : step.Title;
        _objective.Text = step?.Title ?? "Nenhuma missão ativa";
        var counting = quest is { KillsNeeded: > 0 };
        _bar.Visibility = counting ? ViewStates.Visible : ViewStates.Gone;
        _title.Text = _titleText + (_collapsed ? "  ▸" : "  ▾");
        _objective.Visibility = _collapsed ? ViewStates.Gone : ViewStates.Visible;
        if (counting)
        {
            var done = Math.Min(quest.Kills, quest.KillsNeeded);
            _barLabel.Text = $"{quest.TargetName}  {done}/{quest.KillsNeeded}";
            _bar.Post(() =>
            {
                var layout = _barFill.LayoutParameters;
                layout.Width = _bar.Width * done / quest.KillsNeeded;
                _barFill.LayoutParameters = layout;
            });
        }
    }

    // The way to the tribe's guide while the player is in the guide's town and has not met it yet.
    private string GuideDirection()
    {
        if (_session.MapPose is not { } pose || _session.Quest?.Progress.FirstOrDefault()?.Description is not
            (AdventurerJourneyRoute.MeetSkadiDescription or AdventurerJourneyRoute.MeetJinDescription))
        {
            return null;
        }

        var guide = AdventurerJourneyRoute.For(_session.Character.Class).Guide;
        if (guide.FieldCode != pose.FieldId)
        {
            return null;
        }

        var offset = new Vector2(guide.X, guide.Z) - new Vector2(pose.Position.X, pose.Position.Z);
        var sector = ((int)MathF.Round(MathF.Atan2(offset.X, offset.Y) / (MathF.PI / 4)) + 8) % 8;
        return offset.Length() < NearGuide ? "O guia está por perto" : $"Guia: {Compass[sector]}";
    }

    private GradientDrawable Rounded(Color fill, Color stroke)
    {
        var shape = new GradientDrawable();
        shape.SetColor(fill.ToArgb());
        shape.SetCornerRadius(Dp(4));
        shape.SetStroke(Dp(1), stroke);
        return shape;
    }

    private int Dp(int value) => (int)MathF.Round(value * _density);
}
