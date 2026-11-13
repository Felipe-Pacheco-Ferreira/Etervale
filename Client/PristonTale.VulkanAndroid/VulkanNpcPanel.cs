using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Mobile;

// An NPC's dialog: one HUD plate at the bottom, between the menu buttons and the action wheel (LeftClear, RightClear),
// with the NPC's live 3D portrait in an ornate bezel at its left (the game draws the head under the plate's round
// window, at the place SetNpcPortrait publishes), the NPC's name as a section heading, the line in a recessed card that
// scrolls when long, and the choices as buttons, the first one highlighted.
internal sealed class VulkanNpcPanel(Activity activity, FrameLayout overlay, VulkanWorldSession session)
{
    // In dp: the portrait window's radius and the room for its bezel between it and the plate's edge; the plate's widest and
    // narrowest width, its top and bottom padding, its side padding, its gap to the HUD beside it and its lift off the bottom;
    // the line card's tallest text and its padding; a button's size; the plate's corner. NameSize is in sp.
    private const int PortraitRadius = 36;
    private const int PortraitMargin = 18;
    private const int MaxWidth = 420;
    private const int MinWidth = 300;
    private const int Padding = 14;
    private const int Side = 18;
    private const int Clearance = 12;
    private const int Lift = 24;
    private const int MaxLineHeight = 84;
    private const int LinePadding = 10;
    private const int ButtonHeight = 44;
    private const int ButtonWidth = 88;
    private const int Corner = 14;
    private const float NameSize = 18;

    public ulong EntityId { get; set; }
    public string NpcName { get; set; }

    // The room the HUD keeps at the bottom left (the menu buttons) and bottom right (the action wheel), in pixels.
    public int LeftClear { get; set; }
    public int RightClear { get; set; }

    public async Task<int?> ShowAsync(string title, string message, string[] choices, CancellationToken cancellation)
    {
        var answer = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entityId = EntityId;
        var portrait = entityId != 0;
        var holeCentre = portrait ? Dp(PortraitMargin + PortraitRadius) : 0;
        var holeRadius = portrait ? Dp(PortraitRadius) : 0;

        var free = overlay.Width - LeftClear - RightClear - Dp(Clearance) * 2;
        var width = Math.Min(Dp(MaxWidth), free);
        var left = LeftClear + Dp(Clearance) + (free - width) / 2;
        if (width < Dp(MinWidth))
        {
            // Too narrow a gap on this screen: centred over the HUD instead.
            width = Math.Min(Dp(MaxWidth), overlay.Width - Dp(Clearance) * 2);
            left = (overlay.Width - width) / 2;
        }

        // Taps on the plate stay on it instead of reaching the world below.
        var card = new FrameLayout(activity) { Clickable = true };
        card.AddView(new Backdrop(activity, Dp(Corner), holeCentre, holeRadius), new FrameLayout.LayoutParams(-1, -1));
        var textLeft = portrait ? holeCentre * 2 : Dp(Side);
        var content = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        content.SetPadding(textLeft, Dp(Padding), Dp(Side), Dp(Padding));
        content.SetMinimumHeight(holeCentre * 2);
        card.AddView(content, new FrameLayout.LayoutParams(-1, -2));

        content.AddView(VulkanPanelArt.Section(activity, NpcName ?? title, size: NameSize));
        if (NpcName is not null && !string.IsNullOrWhiteSpace(title) && title != NpcName)
        {
            content.AddView(VulkanPanel.Text(activity, title, 13, MobileHudTheme.Gold, serif: true), new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(2) });
        }

        var scroll = new ScrollView(activity) { Background = VulkanPanelArt.Inset(activity) };
        scroll.SetPadding(Dp(LinePadding), Dp(LinePadding / 2), Dp(LinePadding), Dp(LinePadding / 2));
        var line = VulkanPanel.Body(activity, message.Trim());
        line.SetLineSpacing(Dp(2), 1f);
        scroll.AddView(line);
        var lineWidth = width - textLeft - Dp(Side) - Dp(LinePadding) * 2;
        line.Measure(View.MeasureSpec.MakeMeasureSpec(lineWidth, MeasureSpecMode.Exactly), View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
        var lineHeight = Math.Min(line.MeasuredHeight, Dp(MaxLineHeight)) + Dp(LinePadding);
        content.AddView(scroll, new LinearLayout.LayoutParams(-1, lineHeight) { TopMargin = Dp(6) });

        var actions = new LinearLayout(activity);
        actions.SetGravity(GravityFlags.End | GravityFlags.CenterVertical);
        var actionScroll = new HorizontalScrollView(activity) { FillViewport = true, HorizontalScrollBarEnabled = false };
        actionScroll.AddView(actions, new FrameLayout.LayoutParams(-1, -2));
        content.AddView(actionScroll, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });
        for (var i = 0; i < choices.Length; i++)
        {
            var index = i;
            AddButton(choices[i], prominent: i == 0).Click += (_, _) => answer.TrySetResult(index);
        }

        AddButton("Fechar", prominent: false).Click += (_, _) => answer.TrySetResult(null);
        overlay.AddView(card, new FrameLayout.LayoutParams(width, -2, GravityFlags.Left | GravityFlags.Bottom)
        {
            LeftMargin = left,
            BottomMargin = Dp(Lift)
        });

        Button AddButton(string text, bool prominent)
        {
            var button = MobileHudTheme.Button(activity, text, prominent);
            button.TextSize = 14;
            button.Typeface = Typeface.Create("serif", TypefaceStyle.Bold);
            button.SetMinWidth(Dp(ButtonWidth));
            button.SetMinimumWidth(Dp(ButtonWidth));
            actions.AddView(button, new LinearLayout.LayoutParams(-2, Dp(ButtonHeight)) { LeftMargin = Dp(8) });
            return button;
        }

        void PlacePortrait()
        {
            if (!portrait || !card.IsAttachedToWindow || overlay.Width == 0 || overlay.Height == 0)
            {
                return;
            }

            session.SetNpcPortrait(new NpcPortraitFrame(entityId,
                (card.Left + holeCentre - holeRadius) / (float)overlay.Width, (card.Top + holeCentre - holeRadius) / (float)overlay.Height,
                holeRadius * 2 / (float)overlay.Width, holeRadius * 2 / (float)overlay.Height));
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

    // The dialog's plate; with a window (holeRadius > 0) centred holeCentre pixels from its top and left, the NPC's bezel
    // around it with a glow breathing over it (VulkanHudArt.Aura), redrawn every VulkanHudArt.AnimationFrameMs.
    private sealed class Backdrop(Context context, float corner, float holeCentre, float holeRadius) : View(context)
    {
        private readonly RectF _bounds = new();

        protected override void OnDraw(Canvas canvas)
        {
            _bounds.Set(0, 0, Width, Height);
            VulkanHudArt.Plate(canvas, _bounds, corner, holeCentre, holeCentre, holeRadius, VulkanHudArt.NpcGem);
            if (holeRadius <= 0f)
            {
                return;
            }

            VulkanHudArt.Aura(canvas, holeCentre, holeCentre, holeRadius, VulkanHudArt.Phase(Android.OS.SystemClock.ElapsedRealtimeNanos()));
            PostInvalidateDelayed(VulkanHudArt.AnimationFrameMs);
        }
    }
}
