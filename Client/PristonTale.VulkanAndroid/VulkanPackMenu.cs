using Android.App;
using Android.Widget;
using PristonTale.Mobile;

internal static class VulkanPackMenu
{
    public static async Task OpenAsync(Activity activity, FrameLayout overlay)
    {
        using var lifetime = new CancellationTokenSource();
        var cancellation = lifetime.Token;
        var panel = new VulkanPanel(activity, overlay, "Mapas baixados", 600, 340);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.Closed += () =>
        {
            lifetime.Cancel();
            closed.TrySetResult();
        };

        var status = VulkanPanel.Body(activity, "Consultando pacotes…", 14, VulkanPanel.Muted);
        panel.Content.AddView(status);
        var list = VulkanPanel.Body(activity, "", 13);
        var scroll = new ScrollView(activity) { Background = VulkanPanelArt.Inset(activity) };
        var padding = (int)MathF.Round(10 * (activity.Resources?.DisplayMetrics?.Density ?? 1f));
        list.SetPadding(padding, padding, padding, padding);
        scroll.AddView(list);
        panel.Content.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1) { TopMargin = padding, BottomMargin = padding });
        var download = MobileHudTheme.Button(activity, "Baixar tudo", prominent: true);
        download.Enabled = false;
        panel.Content.AddView(download);

        async Task RefreshAsync()
        {
            var packs = await VulkanPacks.ListAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            var installed = packs.Where(entry => entry.Installed).ToArray();
            status.Text = packs.Length == 0 ? "Manifesto de pacotes indisponível." : $"Pacotes baixados: {installed.Length} / {packs.Length}";
            list.Text = installed.Length == 0
                ? "Nenhum pacote baixado."
                : string.Join(Environment.NewLine, installed.Select(entry => $"{entry.Pack.Id} · {entry.Pack.Size / 1048576d:N1} MB"));
            download.Enabled = installed.Length < packs.Length;
        }

        download.Click += async (_, _) =>
        {
            download.Enabled = false;
            status.Text = "Baixando pacotes…";
            try
            {
                await VulkanPacks.DownloadAllAsync(cancellation);
                await RefreshAsync();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                status.Text = "Não foi possível concluir o download.";
                download.Enabled = true;
            }
            catch (Exception) when (cancellation.IsCancellationRequested) { }
        };

        try
        {
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            status.Text = "Não foi possível consultar os pacotes.";
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }

        await closed.Task;
    }
}
