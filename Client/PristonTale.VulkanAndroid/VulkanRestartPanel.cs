using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Mobile;

// The death window, the PC's restart choice in the HUD's look: standing up in the field (the death's share and a
// hundredth more, refused where the experience does not cover it), in town (the death's share) or, after
// HudRestartPanel.TimeoutMs without a choice, in town by itself. It opens HudRestartPanel.OpenDelayMs after the fall and
// closes once the player is alive; a tap outside does not close it while the player is down. Called on the UI thread.
internal sealed class VulkanRestartPanel(Activity activity, FrameLayout overlay, VulkanWorldSession session)
{
    private VulkanPanel _panel;
    private TextView _countdown;
    private long _deadSince = -1;
    private long _openedAt;
    private bool _sending;

    // Each HUD refresh: opens, counts down, chooses the town on time and closes once alive.
    public void Refresh()
    {
        var death = session.Death;
        var now = Android.OS.SystemClock.ElapsedRealtime();
        if (death is null)
        {
            _deadSince = -1;
            _openedAt = 0;
            _sending = false;
            Close();
            return;
        }

        if (_deadSince < 0)
        {
            _deadSince = now;
        }

        if (_sending || now - _deadSince < HudRestartPanel.OpenDelayMs)
        {
            return;
        }

        if (_panel is null)
        {
            Open(death);
            if (_openedAt == 0)
            {
                _openedAt = now;
            }
        }

        var left = (long)HudRestartPanel.TimeoutMs - (now - _openedAt);
        if (left <= 0)
        {
            _ = ChooseAsync(RestartChoice.Town);
            return;
        }

        _countdown.Text = $"Volta à cidade sozinho em {left / 1000 + 1} s";
    }

    private void Open(VulkanWorldSession.DeathView death)
    {
        int Dp(int value) => (int)MathF.Round(value * (activity.Resources?.DisplayMetrics?.Density ?? 1f));
        _panel = new VulkanPanel(activity, overlay, "Você morreu", 460);
        _panel.Closed += () => _panel = null;
        var offer = death.Offer;
        string cost;
        if (offer is { Scroll: true })
        {
            cost = "O Pergaminho de Ressurreição será usado: sem penalidade.";
        }
        else if (offer is { } known && (known.ExperienceShare > 0 || known.GoldShare > 0))
        {
            cost = $"Penalidade da morte: -{known.ExperienceShare:N0} XP e -{known.GoldShare:N0} de ouro.";
        }
        else
        {
            cost = "Sem penalidade de morte no seu nível.";
        }

        _panel.Content.AddView(VulkanPanel.Body(activity, cost, 14), new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(10) });
        var fieldOpen = offer is not { FieldOpen: false };
        var fieldHint = offer is { Scroll: true } ? "No mesmo lugar, sem penalidade"
            : fieldOpen ? "No mesmo lugar: penalidade extra de 1% (ouro e XP)" : "XP insuficiente para ficar no mapa";
        Choice("Recomeçar no mapa", fieldHint, fieldOpen, RestartChoice.Field);
        Choice("Recomeçar na cidade", "Na cidade, em segurança", true, RestartChoice.Town);
        _countdown = VulkanPanel.Body(activity, "", 12, VulkanPanel.Muted);
        _panel.Content.AddView(_countdown, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(6), Gravity = GravityFlags.CenterHorizontal });

        void Choice(string title, string hint, bool enabled, RestartChoice choice)
        {
            var button = MobileHudTheme.Button(activity, $"{title}\n{hint}", prominent: choice == RestartChoice.Town);
            button.TextSize = 14;
            button.Enabled = enabled;
            button.Alpha = enabled ? 1f : .45f;
            button.Click += (_, _) => _ = ChooseAsync(choice);
            _panel.Content.AddView(button, new LinearLayout.LayoutParams(-1, Dp(64)) { BottomMargin = Dp(8) });
        }
    }

    private async Task ChooseAsync(RestartChoice choice)
    {
        if (_sending)
        {
            return;
        }

        _sending = true;
        Close();
        try
        {
            var outcome = await session.RestartAsync(choice);
            if (!outcome.Ok)
            {
                Toast.MakeText(activity, outcome.Status == RestartStatus.FieldRefused ? "XP insuficiente para ficar no mapa." : "Não foi possível renascer agora.",
                    ToastLength.Short)?.Show();
                _sending = false;
                _deadSince = Android.OS.SystemClock.ElapsedRealtime() - (long)HudRestartPanel.OpenDelayMs;
                _openedAt = 0;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            _sending = false;
        }
    }

    private void Close()
    {
        if (_panel is { } panel)
        {
            _panel = null;
            panel.Close();
        }
    }
}
