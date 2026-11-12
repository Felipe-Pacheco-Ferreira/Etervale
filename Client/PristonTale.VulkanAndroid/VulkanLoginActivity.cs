using System;
using System.Threading;
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

[Activity(Label = "Etervale Vulkan", MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.Landscape)]
public sealed class VulkanLoginActivity : Activity
{
    private readonly CancellationTokenSource lifetime = new();
    private GameServerConnection connection;
    private LinearLayout panel;
    private TextView status;

    protected override void OnCreate(Bundle state)
    {
        base.OnCreate(state);
        ShowLogin();
    }

    private void ShowLogin()
    {
        CreatePanel("Etervale");
        var address = AddField("Endereço do servidor (IP:porta?cert=SHA-256)", InputTypes.ClassText | InputTypes.TextVariationUri);
        address.Text = GetPreferences(FileCreationMode.Private)?.GetString("server", "");
        var account = AddField("Conta", InputTypes.ClassText);
        var password = AddField("Senha", InputTypes.ClassText | InputTypes.TextVariationPassword);
        var submit = AddButton("Entrar");
        submit.Click += async (_, _) =>
        {
            submit.Enabled = false;
            status.Text = "Conectando…";
            try
            {
                await LoginAsync(address.Text?.Trim() ?? "", account.Text?.Trim() ?? "", password.Text ?? "");
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

        GetPreferences(FileCreationMode.Private)?.Edit()?.PutString("server", address)?.Apply();
        var active = connection;
        connection = null;
        ProbeApplication.SetConnection(active);
        StartActivity(new Intent().SetClassName(PackageName!, "org.libsdl.app.SDLActivity"));
        ShowLogin();
    }
    private void CreatePanel(string title)
    {
        panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var padding = (int)(24 * (Resources?.DisplayMetrics?.Density ?? 1));
        panel.SetPadding(padding, padding, padding, padding);
        panel.AddView(new TextView(this) { Text = title, TextSize = 26 });
        status = new TextView(this);
        panel.AddView(status);
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(panel);
        SetContentView(scroll);
    }

    private EditText AddField(string hint, InputTypes type)
    {
        var input = new EditText(this) { Hint = hint, InputType = type };
        input.SetSingleLine();
        panel.AddView(input);
        return input;
    }

    private Button AddButton(string label)
    {
        var button = new Button(this) { Text = label };
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
