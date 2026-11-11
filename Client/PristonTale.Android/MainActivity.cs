using Android;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Text;
using Android.Views;
using Android.Widget;
using PristonTale.Client.Net;
using PristonTale.Contracts.World;
using PristonTale.Geometry;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using System.Numerics;

[assembly: UsesPermission(Manifest.Permission.Internet)]

namespace PristonTale.Mobile;

[Activity(Label = "Etervale", MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.SensorLandscape)]
public sealed class MainActivity : Activity
{
    private readonly CancellationTokenSource _lifetime = new();
    private RicartenSceneView? _scene;
    private MovementJoystick? _joystick;

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        RequestWindowFeature(WindowFeatures.NoTitle);
        var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var padding = (int)(24 * (Resources?.DisplayMetrics?.Density ?? 1f));
        form.SetPadding(padding, padding, padding, padding);
        form.AddView(new TextView(this) { Text = $"Etervale {PackageManager?.GetPackageInfo(PackageName!, 0)?.VersionName}", TextSize = 28 });
        var address = AddField(form, "Endereço do servidor (IP:porta?cert=SHA-256)", InputTypes.ClassText | InputTypes.TextVariationUri);
        var account = AddField(form, "Conta", InputTypes.ClassText);
        var password = AddField(form, "Senha", InputTypes.ClassText | InputTypes.TextVariationPassword);
        var character = AddField(form, "Personagem (opcional)", InputTypes.ClassText);
        var submit = new Button(this) { Text = "Entrar" };
        var status = new TextView(this) { Text = "Informe os dados do servidor para entrar." };
        form.AddView(submit);
        form.AddView(status);
        var scroll = new ScrollView(this);
        scroll.AddView(form);
        SetContentView(scroll);

        submit.Click += async (_, _) =>
        {
            submit.Enabled = false;
            status.Text = "Conectando…";
            try
            {
                status.Text = await ConnectAsync(address.Text?.Trim() ?? "", account.Text?.Trim() ?? "", password.Text ?? "",
                    character.Text?.Trim() ?? "", scroll, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                status.Text = "O servidor demorou para responder.";
            }
            catch (Exception)
            {
                status.Text = "Falha na conexão. Confira o endereço, o certificado e a rede.";
            }
            finally
            {
                if (!_lifetime.IsCancellationRequested)
                {
                    submit.Enabled = true;
                }
            }
        };
    }

    private EditText AddField(LinearLayout form, string hint, InputTypes inputType)
    {
        var field = new EditText(this) { Hint = hint, InputType = inputType };
        field.SetSingleLine();
        form.AddView(field);
        return field;
    }

    private async Task<string> ConnectAsync(string address, string account, string password, string character, ScrollView loginForm, CancellationToken ct)
    {
        if (address.Length == 0 || account.Length == 0 || password.Length == 0)
        {
            return "Preencha o endereço, a conta e a senha.";
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await using var connection = new GameServerConnection(address);
        var login = await connection.LoginAsync(account, password, "pt-BR", timeout.Token);
        if (!login.Ok)
        {
            return "O servidor recusou o login.";
        }

        var characters = await connection.ListCharactersAsync(timeout.Token);
        if (character.Length == 0)
        {
            return characters.Count == 0
                ? "Login aceito. Nenhum personagem cadastrado."
                : "Login aceito. Personagens:\n" + string.Join("\n", characters.Select(entry => entry.Name));
        }

        var entered = await connection.EnterWorldAsync(character, timeout.Token);
        if (!entered.Ok)
        {
            return "Login aceito, mas o servidor recusou a entrada do personagem.";
        }

        if (entered.FieldId != FieldCodes.Ricarten)
        {
            return $"Login aceito. O personagem está no mapa {entered.FieldId}. Esta versão exibe Ricarten (mapa 3).";
        }

        var stage = await Task.Run(() =>
        {
            using var asset = Assets?.Open("ricarten.smd") ?? throw new InvalidOperationException("Ricarten não está no APK.");
            using var bytes = new MemoryStream();
            asset.CopyTo(bytes);
            var triangles = PristonTale.Assets.StageCollisionReader.ReadTriangles(bytes.ToArray());
            using var scene = Assets?.Open("ricarten-scene.bin") ?? throw new InvalidOperationException("A cena 3D não está no APK.");
            return (Mesh: new CollisionMesh(triangles), Scene: MobileScene.Read(scene));
        }, ct);
        var terrain = new FieldTerrain(stage.Mesh);
        var spawn = terrain.PlaceOnFloor(new Vector3(entered.SpawnX, 0, entered.SpawnZ));
        using var playing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var play = connection.OpenPlayChannel(playing.Token);
        var world = new FrameLayout(this) { MotionEventSplittingEnabled = true };
        var info = new LinearLayout(this) { Orientation = Orientation.Vertical };
        info.SetBackgroundColor(Color.Argb(178, 17, 22, 29));
        info.SetPadding(Dp(12), Dp(7), Dp(12), Dp(7));
        var title = new TextView(this) { Text = $"{character} · Ricarten", TextSize = 18 };
        var notice = new TextView(this) { Text = "Aguardando o servidor…", TextSize = 12 };
        title.SetTextColor(Color.White);
        notice.SetTextColor(Color.Rgb(214, 223, 232));
        info.AddView(title);
        info.AddView(notice);
        var leave = new Button(this) { Text = "Voltar ao login" };
        using var map = new RicartenSceneView(this, stage.Scene, spawn);
        using var joystick = new MovementJoystick(this);
        _scene = map;
        _joystick = joystick;
        world.AddView(map, new FrameLayout.LayoutParams(-1, -1));
        world.AddView(info, Overlay(-2, -2, GravityFlags.Top | GravityFlags.Left));
        world.AddView(leave, Overlay(-2, -2, GravityFlags.Top | GravityFlags.Right));
        world.AddView(joystick, Overlay(Dp(128), Dp(128), GravityFlags.Bottom | GravityFlags.Left));
        var npcPanel = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        npcPanel.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        npcPanel.SetBackgroundColor(Color.Argb(235, 17, 22, 29));
        var npcInfo = new TextView(this) { TextSize = 15 };
        npcInfo.SetTextColor(Color.White);
        var closeNpc = new Button(this) { Text = "Fechar" };
        npcPanel.AddView(npcInfo);
        npcPanel.AddView(closeNpc);
        world.AddView(npcPanel, Overlay(Dp(220), -2, GravityFlags.Bottom | GravityFlags.Right));

        using var bubble = new NpcSpeechBubble(this) { Visibility = ViewStates.Invisible };
        world.AddView(bubble, new FrameLayout.LayoutParams(-2, -2, GravityFlags.Top | GravityFlags.Left));

        ulong? speakingNpc = null;
        long speechUntil = 0;
        var requestInProgress = false;
        Java.Lang.Runnable? followSpeech = null;
        void FollowSpeech()
        {
            if (speakingNpc is not { } id || playing.IsCancellationRequested || Environment.TickCount64 >= speechUntil)
            {
                speakingNpc = null;
                bubble.Visibility = ViewStates.Invisible;
                return;
            }

            if (map.TryNpcHead(id, out var point) && point.X >= 0 && point.X <= world.Width &&
                point.Y >= 0 && point.Y <= world.Height)
            {
                bubble.TranslationX = Math.Clamp(point.X - bubble.Width * .5f, 0f, Math.Max(0, world.Width - bubble.Width));
                bubble.TranslationY = Math.Max(0f, point.Y - bubble.Height - Dp(8));
                bubble.Visibility = ViewStates.Visible;
            }
            else
            {
                bubble.Visibility = ViewStates.Invisible;
            }
            bubble.PostOnAnimation(followSpeech);
        }

        using var speechCallback = new Java.Lang.Runnable(FollowSpeech);
        followSpeech = speechCallback;
        closeNpc.Click += (_, _) =>
        {
            npcPanel.Visibility = ViewStates.Gone;
        };
        map.NpcSelected += async id =>
        {
            if (requestInProgress)
            {
                return;
            }

            requestInProgress = true;
            try
            {
                var interaction = await play.InteractNpcAsync(id, playing.Token);
                if (playing.IsCancellationRequested)
                {
                    return;
                }
                if (!interaction.Ok)
                {
                    Toast.MakeText(this, "Aproxime-se do NPC para conversar.", ToastLength.Short)?.Show();
                    return;
                }

                var services = interaction.Services.Select(service => NpcServiceLabel(service.Kind)).Distinct().ToArray();
                npcInfo.Text = services.Length == 0 ? interaction.Name : $"{interaction.Name}\n{string.Join(" · ", services)}";
                npcPanel.Visibility = ViewStates.Visible;
                bubble.Text = string.Join("\n", interaction.Dialog.Where(line => !string.IsNullOrWhiteSpace(line)));
                var wasSpeaking = speakingNpc.HasValue;
                speechUntil = Environment.TickCount64 + 5000;
                speakingNpc = string.IsNullOrWhiteSpace(bubble.Text) ? null : id;
                if (speakingNpc.HasValue && !wasSpeaking)
                {
                    FollowSpeech();
                }

                var service = interaction.Services.FirstOrDefault(offer => offer.Available && offer.Kind is NpcServiceKind.Shop or NpcServiceKind.Warehouse);
                if (service is not null)
                {
                    await OpenNpcServiceAsync(play, interaction, service.Kind, playing.Token);
                }
            }
            catch (OperationCanceledException) when (playing.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            {
                Toast.MakeText(this, "Não foi possível conversar com o NPC.", ToastLength.Short)?.Show();
            }
            finally
            {
                requestInProgress = false;
            }
        };

        leave.Click += (_, _) => playing.Cancel();
        SetContentView(world);
        try
        {
            return await new RicartenSession(play, terrain, map, joystick, notice, spawn).RunAsync(playing.Token);
        }
        finally
        {
            bubble.RemoveCallbacks(speechCallback);
            _scene = null;
            _joystick = null;
            if (!ct.IsCancellationRequested)
            {
                world.RemoveView(map);
                world.RemoveView(joystick);
                SetContentView(loginForm);
            }
        }
    }

    private async Task OpenNpcServiceAsync(IPlayChannel play, NpcInteraction npc, NpcServiceKind kind, CancellationToken ct)
    {
        var reply = await play.RequestNpcServiceAsync(new NpcServiceCommand(Guid.NewGuid(), npc.EntityId, kind, NpcServiceAction.Open), ct);
        if (!reply.Outcome.Ok)
        {
            var message = reply.Outcome.Status switch
            {
                NpcServiceStatus.OutOfReach => "Aproxime-se do NPC.",
                NpcServiceStatus.OverWeight => "Você está carregando peso demais para abrir o armazém.",
                _ => "O NPC não pôde abrir este serviço."
            };
            Toast.MakeText(this, message, ToastLength.Short)?.Show();
            return;
        }

        try
        {
            var rows = kind == NpcServiceKind.Shop
                ? reply.Catalog?.OrderBy(item => item.Tab).ThenBy(item => item.Ordinal)
                    .Select(item => $"{item.Name}   ·   {item.Price:N0} ouro").ToArray() ?? []
                : reply.Warehouse?.Items.OrderBy(item => item.Anchor)
                    .Select(item => $"{item.Name}   ×{item.Count}").ToArray() ?? [];
            var header = kind == NpcServiceKind.Warehouse && reply.Warehouse is { } storage
                ? $"{storage.Money:N0} ouro   ·   Peso {storage.Weight}/{storage.MaxWeight}"
                : "Itens à venda";
            var body = rows.Length == 0 ? "Nenhum item." : string.Join(Environment.NewLine, rows);

            using var content = new TextView(this) { Text = header + Environment.NewLine + Environment.NewLine + body, TextSize = 16 };
            content.SetPadding(Dp(20), Dp(12), Dp(20), Dp(12));
            using var scroll = new ScrollView(this);
            scroll.AddView(content);
            using var builder = new AlertDialog.Builder(this);
            using var dialog = builder.SetTitle($"{NpcServiceLabel(kind)} · {npc.Name}")!
                .SetView(scroll)!.SetPositiveButton("Fechar", (_, _) => { })!.Create()!;
            var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.DismissEvent += (_, _) => dismissed.TrySetResult();
            dialog.Show();
            try
            {
                await dismissed.Task.WaitAsync(ct);
            }
            finally
            {
                if (dialog.IsShowing)
                {
                    dialog.Dismiss();
                }
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await play.RequestNpcServiceAsync(new NpcServiceCommand(Guid.NewGuid(), npc.EntityId, kind, NpcServiceAction.Close), ct);
            }
        }
    }

    private static string NpcServiceLabel(NpcServiceKind kind) => kind switch
    {
        NpcServiceKind.Shop => "Loja",
        NpcServiceKind.SkillMaster => "Habilidades",
        NpcServiceKind.Warehouse => "Armazém",
        NpcServiceKind.QuestGiver or NpcServiceKind.QuestScript or NpcServiceKind.QuestEvent => "Missões",
        NpcServiceKind.WarpGate or NpcServiceKind.Teleporter => "Teleporte",
        NpcServiceKind.ItemAging => "Aprimoramento",
        NpcServiceKind.ItemMix => "Mistura",
        NpcServiceKind.Clan => "Clã",
        NpcServiceKind.Manufacture => "Fabricação",
        _ => "Serviço especial"
    };

    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 1f) + .5f);

    private FrameLayout.LayoutParams Overlay(int width, int height, GravityFlags gravity)
    {
        var layout = new FrameLayout.LayoutParams(width, height, gravity);
        var margin = Dp(12);
        layout.SetMargins(margin, margin, margin, margin);
        return layout;
    }

    protected override void OnPause()
    {
        _joystick?.Reset();
        _scene?.OnPause();
        base.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _scene?.OnResume();
    }

    protected override void OnDestroy()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnDestroy();
    }
}
