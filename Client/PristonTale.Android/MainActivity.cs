using Android;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using MediaPlayer = Android.Media.MediaPlayer;
using Android.Text;
using Android.Views;
using Android.Widget;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Mobile.CharacterSelect;
using PristonTale.Contracts.Sessions;
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
    // How long the world takes to appear out of the curtain once it is drawn.
    private static readonly TimeSpan FirstWorldFrameTimeout = TimeSpan.FromSeconds(30);

    private bool _runRequested = true;
    private readonly CancellationTokenSource _lifetime = new();
    private RicartenSceneView? _scene;
    private MediaPlayer? _worldMusic;
    private CharacterSelection? _selection;
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
                status.Text = await ConnectAsync(address.Text?.Trim() ?? "", account.Text?.Trim() ?? "", password.Text ?? "", scroll,
                    _lifetime.Token);
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

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplyImmersiveMode();
        }
    }

    private void ApplyImmersiveMode()
    {
        if (Window is not { } window)
        {
            return;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(35))
            {
                window.SetDecorFitsSystemWindows(false);
            }
            if (window.DecorView is { IsAttachedToWindow: true } decor && decor.WindowInsetsController is { } controller)
            {
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                controller.Hide(WindowInsets.Type.SystemBars());
            }
        }
        else
        {
            window.AddFlags(WindowManagerFlags.Fullscreen);
        }
    }

    private async Task<string> ConnectAsync(string address, string account, string password, ScrollView loginForm, CancellationToken ct)
    {
        if (address.Length == 0 || account.Length == 0 || password.Length == 0)
        {
            return "Preencha o endereço, a conta e a senha.";
        }

        await using var connection = new GameServerConnection(address);
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var login = await connection.LoginAsync(account, password, "pt-BR", timeout.Token);
            if (!login.Ok)
            {
                return "O servidor recusou o login.";
            }
        }

        var (result, frame) = await SelectCharacterAsync(connection, loginForm, ct);
        using var selectionFrame = frame;
        if (result is not { } selected)
        {
            return "Você saiu da seleção de personagens.";
        }

        var fieldId = selected.Outcome.FieldId;
        var spawn = new Vector3(selected.Outcome.SpawnX, 0f, selected.Outcome.SpawnZ);
        var model = selected.Model ?? await CharacterModel.LoadAsync(Assets!, selected.Character.Class);
        PresenceUpdate? initial = null;
        using var transition = new FrameLayout(this);
        using var selectionImage = new ImageView(this) { Clickable = true };
        if (selectionFrame is not null)
        {
            selectionImage.SetImageBitmap(selectionFrame);
            selectionImage.SetScaleType(ImageView.ScaleType.FitXy!);
            transition.AddView(selectionImage, new FrameLayout.LayoutParams(-1, -1));
            SetContentView(transition);
        }
        await using var play = connection.OpenPlayChannel(ct);
        try
        {
            while (true)
            {
                var field = fieldId switch
                {
                    FieldCodes.Ricarten => (Asset: "ricarten", Name: "Ricarten"),
                    FieldCodes.Pillai => (Asset: "pillai", Name: "Pillai"),
                    FieldCodes.ForeverFall1 => (Asset: "forever-fall-01", Name: "Forever Fall 1"),
                    FieldCodes.QuestArena => (Asset: "quest-arena", Name: "Arena de Missões"),
                    _ => default
                };
                if (field.Asset is null)
                {
                    return $"O mapa {fieldId} ainda não está disponível no Android.";
                }

                var stage = await Task.Run(() => ReadField(field.Asset), ct);
                var fieldResult = await PlayFieldAsync(play, selected.Character.Name, selected.Character.FaceIndex, model, fieldId,
                    spawn, stage, field.Asset, field.Name, transition, selectionImage, initial, ct);
                if (initial is not null)
                {
                    play.Recycle(initial);
                }
                initial = fieldResult.Update;
                if (initial?.Warp is not { } warp)
                {
                    return fieldResult.Message;
                }

                fieldId = warp.FieldId;
                spawn = warp.Position;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            global::Android.Util.Log.Error("Etervale.World", ex.ToString());
            return $"Não foi possível abrir o mapa {fieldId}. Consulte o log do jogo (Etervale.World).";
        }
        finally
        {
            if (initial is not null)
            {
                play.Recycle(initial);
            }
            if (!ct.IsCancellationRequested)
            {
                SetContentView(loginForm);
            }
        }
    }

    private (CollisionMesh Mesh, MobileScene Scene, WorldArrival Arrival) ReadField(string field)
    {
        using var asset = Assets!.Open($"{field}.smd");
        using var bytes = new MemoryStream();
        asset.CopyTo(bytes);
        var triangles = PristonTale.Assets.StageCollisionReader.ReadTriangles(bytes.ToArray(), out var lamps);
        using var scene = Assets.Open($"{field}-scene.bin");
        using var buffered = new BufferedStream(scene, BakedReader.BufferSize);
        using var arrival = Assets.Open(WorldArrival.ModelAsset);
        return (new CollisionMesh(triangles), MobileScene.Read(buffered) with { Lamps = lamps }, WorldArrival.Read(arrival));
    }

    // Plays in the selected field until the player leaves or the connection ends.
    private async Task<FieldSessionResult> PlayFieldAsync(IPlayChannel play, string characterName, int face, CharacterModel? model,
        int fieldId, Vector3 position, (CollisionMesh Mesh, MobileScene Scene, WorldArrival Arrival) stage, string fieldAsset, string fieldName,
        FrameLayout transition, ImageView selectionImage, PresenceUpdate? initial, CancellationToken ct)
    {
        var terrain = new FieldTerrain(stage.Mesh);
        var spawn = terrain.PlaceOnFloor(position);
        using var playing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var world = new FrameLayout(this) { MotionEventSplittingEnabled = true };
        var notice = new TextView(this) { Text = "Aguardando o servidor�", TextSize = 10 };
        notice.SetTextColor(MobileHudTheme.Ink);
        notice.SetShadowLayer(Dp(1), 0f, Dp(1), Color.Black);
        notice.SetMaxLines(2);
        notice.SetMaxWidth(Dp(170));
        var hud = new MobileWorldHud(this, characterName);
        var shortcuts = new MobileShortcuts(this, play, notice, playing.Token);
        var leave = MobileHudTheme.Button(this, "Sair");
        MobileHudTheme.Icon(leave, HudGlyph.Exit);
        using var map = new RicartenSceneView(this, stage.Scene, spawn, model, face, stage.Arrival);
        using var arrivalSound = PrepareFieldAudio(WorldArrival.SoundAsset);
        using var fieldMusic = PrepareFieldAudio($"{fieldAsset}-music.ogg");
        if (fieldMusic is not null)
        {
            fieldMusic.Looping = true;
            fieldMusic.SetVolume(0.6f, 0.6f);
        }
        using var joystick = new MovementJoystick(this);
        _scene = map;
        _joystick = joystick;
        world.AddView(map, new FrameLayout.LayoutParams(-1, -1));
        using var rainSound = PrepareFieldAudio("rain01.wav");
        using var thunderSound = PrepareFieldAudio("thunder1.wav");
        using var rain = new MobileRainView(this, map, stage.Mesh, rainSound, thunderSound);
        world.AddView(rain, new FrameLayout.LayoutParams(-1, -1));
        using var nameplates = new MobileNameplates(this, map);
        world.AddView(nameplates, new FrameLayout.LayoutParams(-1, -1));
        var chat = new MobileWorldChat(this, play, world, map, characterName, playing.Token);
        var statusColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var profile = new LinearLayout(this);
        profile.SetGravity(GravityFlags.CenterVertical);
        profile.AddView(hud.Portrait, new LinearLayout.LayoutParams(Dp(62), Dp(68)));
        profile.AddView(hud.Status, new LinearLayout.LayoutParams(Dp(142), -2) { LeftMargin = Dp(4) });
        statusColumn.AddView(profile);
        hud.Portrait.LayoutChange += (_, _) => map.ShowPlayerPortrait(hud.Portrait);
        statusColumn.AddView(chat.View, new LinearLayout.LayoutParams(Dp(225), -2) { TopMargin = Dp(4) });
        world.AddView(hud.Experience, new FrameLayout.LayoutParams(-1, Dp(14), GravityFlags.Bottom));
        var shortcutsLayout = Overlay(-2, -2, GravityFlags.Bottom | GravityFlags.CenterHorizontal);
        shortcutsLayout.BottomMargin += Dp(10);
        world.AddView(shortcuts.View, shortcutsLayout);
        world.AddView(statusColumn, Overlay(-2, -2, GravityFlags.Top | GravityFlags.Left));
        var rightPanel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        rightPanel.SetGravity(GravityFlags.Right);
        var mapTitle = new TextView(this) { Text = fieldName, TextSize = 11, Gravity = GravityFlags.Center };
        mapTitle.SetTextColor(MobileHudTheme.Gold);
        MobileHudTheme.Card(mapTitle);
        rightPanel.AddView(mapTitle, new LinearLayout.LayoutParams(Dp(100), Dp(19)));

        var portalGuide = MobileHudTheme.Button(this, fieldId == FieldCodes.Pillai ? "Ir à floresta" : "Ir a Pillai");
        using var minimap = MobileMinimap.Open(this, fieldAsset);
        if (minimap is not null)
        {
            minimap.UpdatePosition(spawn, 0f);
            var mapFrame = new FrameLayout(this);
            MobileHudTheme.Card(mapFrame);
            mapFrame.SetPadding(Dp(2), Dp(2), Dp(2), Dp(2));
            mapFrame.AddView(minimap, new FrameLayout.LayoutParams(-1, -1));
            rightPanel.AddView(mapFrame, new LinearLayout.LayoutParams(Dp(100), Dp(100)));
        }
        if (fieldId is FieldCodes.Pillai or FieldCodes.ForeverFall1)
        {
            rightPanel.AddView(portalGuide);
        }
        world.AddView(rightPanel, Overlay(-2, -2, GravityFlags.Top | GravityFlags.Right));
        var stickLayout = Overlay(Dp(128), Dp(128), GravityFlags.Bottom | GravityFlags.Left);
        stickLayout.BottomMargin += Dp(44);
        world.AddView(joystick, stickLayout);
        var journal = new MobileQuestJournal(this);
        var questDialog = new MobileQuestDialog(this, play, notice);
        hud.Journal.Click += (_, _) => journal.Open();
        var tracker = new LinearLayout(this) { Orientation = Orientation.Vertical };
        tracker.SetGravity(GravityFlags.CenterHorizontal);

        tracker.AddView(hud.Target);
        statusColumn.AddView(notice);
        map.SetWorldClock(play.GameClockMs);
        var pace = MobileHudTheme.Button(this, _runRequested ? "Corrida" : "Caminhada");
        MobileHudTheme.Icon(pace, HudGlyph.Run);
        pace.Click += (_, _) =>
        {
            _runRequested = !_runRequested;
            pace.Text = _runRequested ? "Corrida" : "Caminhada";
        };

        var autoHunt = MobileHudTheme.Button(this, "Auto-caça");
        MobileHudTheme.Icon(autoHunt, HudGlyph.Auto);
        var combatModes = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        pace.SetSingleLine(true);
        autoHunt.SetSingleLine(true);
        pace.TextSize = autoHunt.TextSize = 10;
        combatModes.AddView(pace, new LinearLayout.LayoutParams(0, -1, 1f));
        combatModes.AddView(autoHunt, new LinearLayout.LayoutParams(0, -1, 1f) { LeftMargin = Dp(4) });
        rightPanel.AddView(combatModes, new LinearLayout.LayoutParams(Dp(176), Dp(38)));
        var menu = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        menu.AddView(leave);
        menu.AddView(hud.Journal, new LinearLayout.LayoutParams(-2, -2) { LeftMargin = Dp(4) });
        var menuLayout = Overlay(-2, -2, GravityFlags.Bottom | GravityFlags.Left);
        menuLayout.BottomMargin += Dp(10);
        world.AddView(menu, menuLayout);
        world.AddView(tracker, Overlay(-2, -2, GravityFlags.Top | GravityFlags.CenterHorizontal));
        var attack = MobileHudTheme.Button(this, "Atacar", prominent: true);
        MobileHudTheme.Icon(attack, HudGlyph.Attack, stacked: true);
        var attackLayout = Overlay(Dp(76), Dp(76), GravityFlags.Bottom | GravityFlags.Right);
        attackLayout.BottomMargin += Dp(32);
        world.AddView(attack, attackLayout);
        var npcPanel = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        npcPanel.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        MobileHudTheme.Card(npcPanel);
        var npcInfo = new TextView(this) { TextSize = 15 };
        npcInfo.SetTextColor(Color.White);
        var closeNpc = MobileHudTheme.Button(this, "Fechar");
        npcPanel.AddView(npcInfo);
        npcPanel.AddView(closeNpc);
        world.AddView(npcPanel, Overlay(Dp(220), -2, GravityFlags.Bottom | GravityFlags.Right));

        var speech = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        using var portrait = new View(this);
        using var portraitFrame = new global::Android.Graphics.Drawables.GradientDrawable();
        portraitFrame.SetColor(Color.Transparent);
        portraitFrame.SetStroke(Dp(1), MobileHudTheme.Gold);
        portrait.Foreground = portraitFrame;
        speech.AddView(portrait, new LinearLayout.LayoutParams(-1, Dp(82)));
        var dialogue = new LinearLayout(this) { Orientation = Orientation.Vertical };
        MobileHudTheme.Card(dialogue);
        using var speakerName = new TextView(this) { TextSize = 12, Gravity = GravityFlags.Center };
        speakerName.SetTextColor(MobileHudTheme.Gold);
        speakerName.SetPadding(Dp(6), Dp(3), Dp(6), Dp(3));
        dialogue.AddView(speakerName);
        using var subtitle = new TextView(this) { TextSize = 12, Gravity = GravityFlags.Center };
        subtitle.SetTextColor(MobileHudTheme.Ink);
        subtitle.SetPadding(Dp(7), Dp(3), Dp(7), Dp(6));
        subtitle.SetMaxLines(4);
        dialogue.AddView(subtitle);
        speech.AddView(dialogue, new LinearLayout.LayoutParams(-1, -2));
        world.AddView(speech, Overlay(Dp(148), -2, GravityFlags.CenterVertical | GravityFlags.Right));

        ulong speakingNpc = 0;
        using var hideSpeech = new Java.Lang.Runnable(() =>
        {
            speakingNpc = 0;
            speech.Visibility = ViewStates.Gone;
            map.ClearNpcPortrait();
        });
        portrait.LayoutChange += (_, _) =>
        {
            if (speakingNpc != 0)
            {
                map.ShowNpcPortrait(speakingNpc, portrait);
            }
        };
        var requestInProgress = false;
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
                speakerName.Text = interaction.Name;
                subtitle.Text = string.Join("\n", interaction.Dialog.Where(line => !string.IsNullOrWhiteSpace(line)));
                speech.RemoveCallbacks(hideSpeech);
                map.ClearNpcPortrait();
                if (string.IsNullOrWhiteSpace(subtitle.Text))
                {
                    hideSpeech.Run();
                }
                else
                {
                    speakingNpc = id;
                    speech.Visibility = ViewStates.Visible;
                    map.ShowNpcPortrait(id, portrait);
                    speech.PostDelayed(hideSpeech, 5000);
                }

                if (interaction.Services.Any(offer =>
                    offer.Kind is NpcServiceKind.QuestGiver or NpcServiceKind.QuestScript or NpcServiceKind.SkillMaster or NpcServiceKind.ItemAging
                    || offer.Kind == NpcServiceKind.WarpGate && offer.Value <= 1)
                    && await questDialog.TalkAsync(id, interaction.Name, playing.Token))
                {
                    return;
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
        if (transition.Parent is null)
        {
            SetContentView(transition);
        }

        transition.AddView(world, 0, new FrameLayout.LayoutParams(-1, -1));
        try
        {
            await map.FirstFrame.WaitAsync(FirstWorldFrameTimeout, playing.Token);
            transition.RemoveView(selectionImage);
            selectionImage.SetImageDrawable(null);
            _worldMusic = fieldMusic;
            PlayFieldAudio(fieldMusic);
            var session = new RicartenSession(play, terrain, map, joystick, notice, spawn, fieldId, fieldName,
                () => PlayFieldAudio(arrivalSound), journal, attack, hud, questDialog, chat, minimap, shortcuts, autoHunt, portalGuide, rain, () => _runRequested);
            return await session.RunAsync(initial, playing.Token);
        }
        finally
        {
            playing.Cancel();
            chat.Close();
            speech.RemoveCallbacks(hideSpeech);
            map.ClearNpcPortrait();
            _worldMusic = null;
            fieldMusic?.Release();
            arrivalSound?.Release();
            _scene = null;
            _joystick = null;
            if (!ct.IsCancellationRequested)
            {
                transition.RemoveView(world);
            }
        }
    }

    private MediaPlayer? PrepareFieldAudio(string asset)
    {
        var player = new MediaPlayer();
        try
        {
            using var descriptor = Assets!.OpenFd(asset);
            player.SetDataSource(descriptor.FileDescriptor, descriptor.StartOffset, descriptor.Length);
            player.Prepare();
            return player;
        }
        catch (Exception ex) when (ex is Java.IO.IOException or Java.Lang.IllegalStateException or Java.Lang.IllegalArgumentException)
        {
            global::Android.Util.Log.Warn("Etervale.World", $"Audio preparation failed for {asset}: {ex}");
            player.Release();
            player.Dispose();
            return null;
        }
    }

    private static void PlayFieldAudio(MediaPlayer? player)
    {
        try
        {
            player?.Start();
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            global::Android.Util.Log.Warn("Etervale.World", $"Audio playback failed: {ex}");
        }
    }

    // Shows the character selection until a character enters the world or the player leaves it. Leaving restores the
    // login form; after an entry the selection's last frame stays until the caller shows what comes next.
    private async Task<(CharSelectEntered? Result, Bitmap? Frame)> SelectCharacterAsync(GameServerConnection connection, ScrollView loginForm, CancellationToken ct)
    {
        using var selection = await CharacterSelection.OpenAsync(this, connection, ct);
        _selection = selection;
        SetContentView(selection.View);
        CharSelectEntered? result = null;
        try
        {
            result = await selection.RunAsync(ct);
            return (result, result is null ? null : await selection.CaptureLastFrameAsync());
        }
        finally
        {
            _selection = null;
            if (!ct.IsCancellationRequested && result is null)
            {
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
        _worldMusic?.Pause();
        _joystick?.Reset();
        _scene?.OnPause();
        _selection?.OnPause();
        base.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        PlayFieldAudio(_worldMusic);
        _scene?.OnResume();
        _selection?.OnResume();
    }

    protected override void OnDestroy()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnDestroy();
    }
}
