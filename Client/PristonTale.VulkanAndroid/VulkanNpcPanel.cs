using Android.App;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;

internal sealed class VulkanNpcPanel(Activity activity, FrameLayout overlay, VulkanWorldSession session)
{
    public ulong EntityId { get; set; }
    public string NpcName { get; set; }

    public async Task<int?> ShowAsync(string title, string message, string[] choices, CancellationToken cancellation)
    {
        var answer = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var card = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        View portrait = null;
        var entityId = EntityId;
        if (entityId != 0)
        {
            portrait = new View(activity);
            card.AddView(portrait, new LinearLayout.LayoutParams(-1, Dp(92)));
        }

        var body = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        body.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        MobileHudTheme.Card(body);
        card.AddView(body, new LinearLayout.LayoutParams(-1, -2));
        var heading = new TextView(activity) { Text = NpcName ?? title, TextSize = 15, Gravity = GravityFlags.Center };
        heading.SetTextColor(MobileHudTheme.Gold);
        heading.Typeface = Android.Graphics.Typeface.Create("serif", Android.Graphics.TypefaceStyle.Bold);
        heading.SetPadding(0, 0, 0, Dp(5));
        MobileHudTheme.Card(heading);
        card.AddView(heading, card.IndexOfChild(body), new LinearLayout.LayoutParams(-1, -2));
        if (NpcName is not null && !string.IsNullOrWhiteSpace(title) && title != NpcName)
        {
            var topic = new TextView(activity) { Text = title, TextSize = 13 };
            topic.SetTextColor(MobileHudTheme.Gold);
            body.AddView(topic);
        }

        var scroll = new ScrollView(activity);
        var subtitle = new TextView(activity) { Text = message.Trim(), TextSize = 14 };
        subtitle.SetTextColor(MobileHudTheme.Ink);
        subtitle.SetLineSpacing(Dp(2), 1f);
        scroll.AddView(subtitle);
        subtitle.Measure(View.MeasureSpec.MakeMeasureSpec(Dp(196), MeasureSpecMode.Exactly),
            View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
        body.AddView(scroll, new LinearLayout.LayoutParams(-1, Math.Min(subtitle.MeasuredHeight, Dp(86))));

        var actions = new LinearLayout(activity);
        var actionScroll = new HorizontalScrollView(activity);
        actionScroll.AddView(actions);
        body.AddView(actionScroll, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(6) });
        for (var i = 0; i < choices.Length; i++)
        {
            var index = i;
            var button = MobileHudTheme.Button(activity, choices[i]);
            button.Click += (_, _) => answer.TrySetResult(index);
            actions.AddView(button);
        }

        var close = MobileHudTheme.Button(activity, "Fechar");
        close.Click += (_, _) => answer.TrySetResult(null);
        actions.AddView(close);
        overlay.AddView(card, new FrameLayout.LayoutParams(Dp(220), -2, GravityFlags.Right | GravityFlags.Bottom)
        {
            RightMargin = Dp(83),
            BottomMargin = Dp(66)
        });

        void PlacePortrait()
        {
            if (portrait is null || !card.IsAttachedToWindow || overlay.Width == 0 || overlay.Height == 0)
            {
                return;
            }

            session.SetNpcPortrait(new NpcPortraitFrame(entityId,
                card.Left / (float)overlay.Width, card.Top / (float)overlay.Height,
                portrait.Width / (float)overlay.Width, portrait.Height / (float)overlay.Height));
        }

        card.LayoutChange += (_, _) => PlacePortrait();
        card.Post(PlacePortrait);
        try
        {
            return await answer.Task.WaitAsync(cancellation);
        }
        finally
        {
            session.SetNpcPortrait(null);
            overlay.RemoveView(card);
        }
    }

    private int Dp(int value) => (int)MathF.Round(value * (activity.Resources?.DisplayMetrics?.Density ?? 1f));
}
