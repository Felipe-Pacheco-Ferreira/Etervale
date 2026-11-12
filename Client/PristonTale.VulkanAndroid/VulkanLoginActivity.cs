using System;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Widget;
using PristonTale.Client.Net;

using PristonTale.Contracts.Sessions;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;

[Activity(Label = "Priston Avalon", MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.Landscape)]
public sealed class VulkanLoginActivity : Activity
{
    private readonly CancellationTokenSource lifetime = new();
    private GameServerConnection connection;
    private LinearLayout panel;
    private TextView status;
    private bool awaitingGame;
    private bool pausedForGame;
    private Task assetPreparation;
    private static string PackagedAddress => typeof(VulkanLoginActivity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "EtervaleServerAddress")?.Value;

    protected override void OnCreate(Bundle state)
    {
        base.OnCreate(state);
        assetPreparation = VulkanAssets.PrepareAsync();
        if (PackagedAddress is { Length: > 0 } endpoint)
        {
            if (!ProbeApplication.FirstFrameReady)
            {
                var loading = new VulkanLoadingScreen(this, "Preparando arquivos do jogo…");
                status = loading.Status;
                SetContentView(loading);
            }

            _ = OpenAnimatedLoginAsync(endpoint);
        }
        else
        {
            ShowLogin();
        }
    }

    private async Task OpenAnimatedLoginAsync(string endpoint)
    {
        try
        {
            await assetPreparation.WaitAsync(lifetime.Token);
            ProbeApplication.SetConnection(new GameServerConnection(endpoint), endpoint);
            awaitingGame = true;
            StartActivity(new Intent().SetClassName(PackageName!, "org.libsdl.app.SDLActivity"));
        }
        catch (System.OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Android.Util.Log.Error("EtervaleVulkanProbe", exception.ToString());
            if (status is not null)
            {
                status.Text = "Falha ao abrir o Priston Avalon.";
            }
            else
            {
                Toast.MakeText(this, "Falha ao abrir o Priston Avalon.", ToastLength.Long)?.Show();
                FinishAndRemoveTask();
            }
        }
    }

    private void ShowLogin()
    {
        CreatePanel("Etervale");
        var packagedAddress = PackagedAddress;
        EditText address = null;
        if (string.IsNullOrWhiteSpace(packagedAddress))
        {
            address = AddField("Endereço do servidor", InputTypes.ClassText | InputTypes.TextVariationUri);
            address.Text = GetPreferences(FileCreationMode.Private)?.GetString("server", "");
        }
        var account = AddField("Conta", InputTypes.ClassText);
        var password = AddField("Senha", InputTypes.ClassText | InputTypes.TextVariationPassword);
        var submit = AddButton("Entrar");
        submit.Click += async (_, _) =>
        {
            submit.Enabled = false;
            status.Text = "Conectando…";
            try
            {
                await LoginAsync(packagedAddress ?? address?.Text?.Trim() ?? "", account.Text?.Trim() ?? "", password.Text ?? "");
            }
            catch (Exception ex) when (!lifetime.IsCancellationRequested)
            {
                status.Text = ex is System.OperationCanceledException ? "O servidor demorou para responder."
                    : "Falha na conexão. Confira o endereço, o certificado e a rede.";
                await DisconnectAsync();
            }
            finally
            {
                submit.Enabled = true;
            }
        };
    }

    private async Task LoginAsync(string address, string account, string password)
    {
        if (address.Length == 0 || account.Length == 0 || password.Length == 0)
        {
            status.Text = "Preencha o endereço, a conta e a senha.";
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        connection = new GameServerConnection(address);
        var login = await connection.LoginAsync(account, password, "pt-BR", timeout.Token);
        if (!login.Ok)
        {
            status.Text = "O servidor recusou o login.";
            await DisconnectAsync();
            return;
        }

        try
        {
            if (assetPreparation.IsFaulted)
            {
                assetPreparation = VulkanAssets.PrepareAsync();
            }

            if (!assetPreparation.IsCompleted)
            {
                status.Text = "Preparando arquivos do jogo…";
            }

            await assetPreparation.WaitAsync(lifetime.Token);
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            Android.Util.Log.Error("EtervaleVulkanProbe", exception.ToString());
            status.Text = "Falha ao preparar os arquivos do jogo.";
            await DisconnectAsync();
            return;
        }

        GetPreferences(FileCreationMode.Private)?.Edit()?.PutString("server", address)?.Apply();
        var active = connection;
        connection = null;
        ProbeApplication.SetConnection(active);
        awaitingGame = true;
        StartActivity(new Intent().SetClassName(PackageName!, "org.libsdl.app.SDLActivity"));
    }
    protected override void OnPause()
    {
        base.OnPause();
        pausedForGame = awaitingGame;
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (!awaitingGame || !pausedForGame || IsFinishing)
        {
            return;
        }

        awaitingGame = false;
        pausedForGame = false;
        var failure = ProbeApplication.TakeStartupFailure();
        if (PackagedAddress is { Length: > 0 })
        {
            if (failure is not null)
            {
                Toast.MakeText(this, $"Falha ao iniciar o jogo: {failure}", ToastLength.Long)?.Show();
            }

            FinishAndRemoveTask();
            return;
        }

        ShowLogin();
        if (failure is not null)
        {
            status.Text = $"Falha ao iniciar Vulkan: {failure}";
        }
    }

    private void CreatePanel(string title)
    {
        panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var padding = (int)(24 * (Resources?.DisplayMetrics?.Density ?? 1));
        panel.SetPadding(padding, padding, padding, padding);
        panel.AddView(new TextView(this) { Text = title, TextSize = 32 });
        status = new TextView(this) { TextSize = 17 };
        panel.AddView(status);
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(panel);
        SetContentView(scroll);
    }

    private EditText AddField(string hint, InputTypes type)
    {
        var input = new EditText(this) { Hint = hint, InputType = type, TextSize = 20 };
        input.SetSingleLine();
        input.ImeOptions |= (Android.Views.InputMethods.ImeAction)Android.Views.InputMethods.ImeFlags.NoExtractUi;
        panel.AddView(input);
        return input;
    }

    private Button AddButton(string label)
    {
        var button = new Button(this) { Text = label, TextSize = 20 };
        panel.AddView(button);
        return button;
    }

    private async Task DisconnectAsync()
    {
        var active = connection;
        connection = null;
        if (active is not null)
        {
            await active.DisposeAsync();
        }
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        _ = DisconnectAsync();
        base.OnDestroy();
    }
}
