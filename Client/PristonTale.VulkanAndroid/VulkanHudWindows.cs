using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using PristonTale.Engine3D.Rendering;
using PristonTale.Mobile;

// The HUD's small windows, in the HUD's own look (VulkanPanel): the chat and the graphics quality. The status is
// VulkanStatusPanel.
internal static class VulkanHudWindows
{
    private static readonly (GraphicsQuality Quality, string Name, string Hint)[] Qualities =
    [
        (GraphicsQuality.Low, "Baixo", "Mais leve"),
        (GraphicsQuality.Medium, "Médio", "Equilíbrio"),
        (GraphicsQuality.High, "Alto", "Sombras e luz"),
        (GraphicsQuality.Ultra, "Ultra", "Máximo")
    ];

    // The chat: the last lines seen in their channels' colours, newest at the bottom, the switch that shows or hides them
    // on the screen (feedShown, feedChanged) and a field to write in with its send button.
    public static void Chat(Activity activity, FrameLayout overlay, VulkanWorldSession session, IReadOnlyList<VulkanWorldSession.ChatEntry> history,
        bool feedShown, Action<bool> feedChanged)
    {
        var panel = new VulkanPanel(activity, overlay, "Chat", 600, 360);
        int Dp(int value) => (int)MathF.Round(value * (activity.Resources?.DisplayMetrics?.Density ?? 1f));
        var options = new LinearLayout(activity);
        options.SetGravity(GravityFlags.CenterVertical);
        options.AddView(VulkanPanel.Body(activity, "Mensagens na tela", 13, VulkanPanel.Muted), new LinearLayout.LayoutParams(0, -2, 1));
        var toggle = MobileHudTheme.Button(activity, feedShown ? "Mostrando" : "Ocultas", prominent: feedShown);
        toggle.TextSize = 13;
        toggle.Click += (_, _) =>
        {
            feedShown = !feedShown;
            toggle.Text = feedShown ? "Mostrando" : "Ocultas";
            feedChanged(feedShown);
        };
        options.AddView(toggle, new LinearLayout.LayoutParams(Dp(120), Dp(34)));
        panel.Content.AddView(options, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(8) });

        var scroll = new ScrollView(activity);
        var well = new GradientDrawable();
        well.SetColor(Color.Argb(170, 6, 8, 12).ToArgb());
        well.SetCornerRadius(Dp(8));
        well.SetStroke(Dp(1), Color.Argb(90, 176, 138, 72));
        scroll.Background = well;
        scroll.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        var lines = VulkanPanel.Body(activity, "", 13);
        lines.SetLineSpacing(Dp(3), 1f);
        scroll.AddView(lines);
        panel.Content.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        if (history.Count == 0)
        {
            lines.Text = "Nenhuma mensagem ainda.";
            lines.SetTextColor(VulkanPanel.Muted);
        }
        else
        {
            using var text = VulkanChatText.Format(history, 0);
            lines.SetText(text, TextView.BufferType.Spannable);
        }

        scroll.Post(() => scroll.FullScroll(FocusSearchDirection.Down));
        var row = new LinearLayout(activity);
        row.SetGravity(GravityFlags.CenterVertical);
        panel.Content.AddView(row, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });
        var input = new EditText(activity) { Hint = "Escreva uma mensagem…", TextSize = 15 };
        input.SetSingleLine(true);
        input.SetTextColor(MobileHudTheme.Ink);
        input.SetHintTextColor(VulkanPanel.Muted);
        input.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        var field = new GradientDrawable();
        field.SetColor(Color.Argb(230, 8, 10, 14).ToArgb());
        field.SetCornerRadius(Dp(8));
        field.SetStroke(Dp(1), Color.Argb(180, 176, 138, 72));
        input.Background = field;
        row.AddView(input, new LinearLayout.LayoutParams(0, Dp(44), 1) { RightMargin = Dp(8) });
        var send = MobileHudTheme.Button(activity, "Enviar", prominent: true);
        row.AddView(send, new LinearLayout.LayoutParams(-2, Dp(44)));
        send.Click += async (_, _) =>
        {
            var text = input.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                await session.SendChatAsync(text);
                panel.Close();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
            {
                Toast.MakeText(activity, "Não foi possível enviar a mensagem.", ToastLength.Short)?.Show();
            }
        };
    }

    // The graphics quality: one card per preset, the chosen one lit, and the memory warning.
    public static void Graphics(Activity activity, FrameLayout overlay, VulkanWorldSession session)
    {
        if (session.Graphics is not { } graphics)
        {
            return;
        }

        var panel = new VulkanPanel(activity, overlay, "Qualidade gráfica", 600);
        int Dp(int value) => (int)MathF.Round(value * (activity.Resources?.DisplayMetrics?.Density ?? 1f));
        var row = new LinearLayout(activity);
        panel.Content.AddView(row);
        foreach (var (quality, name, hint) in Qualities)
        {
            var card = MobileHudTheme.Button(activity, $"{name}\n{hint}", prominent: quality == graphics.Quality);
            card.TextSize = 14;
            card.Click += (_, _) =>
            {
                graphics.Request(quality);
                panel.Close();
            };
            row.AddView(card, new LinearLayout.LayoutParams(0, Dp(72), 1) { RightMargin = Dp(6) });
        }

        panel.Content.AddView(VulkanPanel.Body(activity, "Alto e Ultra usam bem mais memória: em alguns celulares o sistema pode fechar o jogo. "
            + "A escolha vale na hora e fica salva depois de 30 segundos.", 12, VulkanPanel.Muted),
            new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(10) });
    }
}
