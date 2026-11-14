using Android.App;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;

// The card at the top of the screen while a field's packs download (VulkanPacks.State): the field, a gold bar with the
// megabytes, and Cancelar, or the error with Tentar de novo. It sits over the game itself, so it also shows at world
// entry, before the HUD exists. Polled on the UI thread.
internal sealed class VulkanDownloadCard : LinearLayout
{
    private const int PollMilliseconds = 250;

    private readonly Activity _activity;
    private readonly TextView _title;
    private readonly VulkanGaugeView _bar;
    private readonly Button _action;
    private VulkanPacks.DownloadState _shown;

    private VulkanDownloadCard(Activity activity) : base(activity)
    {
        _activity = activity;
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        int Dp(int value) => (int)MathF.Round(value * density);
        Orientation = Orientation.Vertical;
        Visibility = ViewStates.Gone;
        Clickable = true;
        Background = new PlateDrawable(Dp(12));
        SetPadding(Dp(14), Dp(10), Dp(14), Dp(10));
        _title = new TextView(activity) { TextSize = 14 };
        _title.SetTextColor(MobileHudTheme.Gold);
        AddView(_title);
        var row = new LinearLayout(activity);
        row.SetGravity(GravityFlags.CenterVertical);
        _bar = new VulkanGaugeView(activity, eased: true);
        row.AddView(_bar, new LayoutParams(0, Dp(16), 1) { RightMargin = Dp(10) });
        _action = MobileHudTheme.Button(activity, "Cancelar");
        _action.TextSize = 13;
        _action.Click += (_, _) =>
        {
            if (_shown?.Error is null)
            {
                VulkanPacks.CancelField();
            }
            else
            {
                VulkanPacks.RetryField();
            }
        };
        row.AddView(_action, new LayoutParams(Dp(132), Dp(34)));
        AddView(row, new LayoutParams(-1, -2) { TopMargin = Dp(6) });
    }

    // Puts the card over the activity's content; it shows itself while a download runs or failed.
    public static void Attach(Activity activity)
    {
        var card = new VulkanDownloadCard(activity);
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        var layout = new FrameLayout.LayoutParams((int)(420 * density), -2, GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = (int)(12 * density)
        };
        var holder = new FrameLayout(activity);
        holder.AddView(card, layout);
        activity.AddContentView(holder, new ViewGroup.LayoutParams(-1, -1));
        card.Post(card.Poll);
    }

    private void Poll()
    {
        if (_activity.IsFinishing || _activity.IsDestroyed)
        {
            return;
        }

        var state = VulkanPacks.State;
        if (!ReferenceEquals(state, _shown))
        {
            _shown = state;
            Show(state);
        }

        PostDelayed(Poll, PollMilliseconds);
    }

    private void Show(VulkanPacks.DownloadState state)
    {
        if (state is null)
        {
            Visibility = ViewStates.Gone;
            return;
        }

        // Kept above the HUD, which is added after the card.
        (Parent as View)?.BringToFront();
        Visibility = ViewStates.Visible;
        var share = state.Total > 0 ? state.Done / (float)state.Total : 0f;
        _title.Text = state.Error ?? $"Baixando mapa {state.FieldName}…";
        _title.SetTextColor(state.Error is null ? MobileHudTheme.Gold : VulkanPanelArt.Warning);
        _bar.Set(VulkanHudArt.Experience, share, $"{share:P0} · {state.Done >> 20} / {Math.Max(1, state.Total >> 20)} MB");
        _action.Text = state.Error is null ? "Cancelar" : "Tentar de novo";
    }
}
