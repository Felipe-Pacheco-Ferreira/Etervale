using PristonTale.Gameplay.World;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.Audio;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering.Weather;
using PristonTale.Gameplay.Weather;
using PristonTale.Contracts.World;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.Screens.Opening;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Social;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.Characters;
using System;
using System.IO;
using System.Linq;
using Android.Util;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using PristonTale.Assets;
using PristonTale.Engine3D;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World.Fields;

internal sealed class RicartenProbe : Game
{
    private const string Tag = "EtervaleVulkanProbe";
    // The character screens' 2D layer is laid out on a screen this tall and enlarged to the phone's (CharacterSelectScreen
    // .UiScale); their buttons grow this much more again, for a finger.
    private const float SelectionUiHeight = 620f;
    private const float SelectionButtonScale = 1.3f;
    private readonly GraphicsDeviceManager graphics;
    private readonly TouchOrbit orbit = new();
    private GameScene scene;
    private EffectLibrary effects;
    private PristonSkyLibrary sky;
    private PristonAudioLibrary audio;
    private SpriteBatch joystickBatch;
    private HudTargetPortrait portrait;
    private HudTargetPortrait npcPortrait;
    private HudTargetPortrait targetPortrait;
    private HudCombat namePlates;
    private HudPainter namePainter;
    private HudSprites nameSprites;
    // The PC's speech bubbles over the speakers' heads, fed by the presence updates and the player's own lines.
    private HudChat chat;
    private Func<ulong, HudOverheadPoint?> overhead;
    private Func<ulong, Color> speakerColor;
    private Texture2D joystickCircle;
    private int frames;
    private GameServerConnection connection;
    private readonly string animatedLoginEndpoint;
    internal GameServerConnection Connection => connection;
    private OpeningScreen opening;
    private HudAtlas loginAtlas;
    private ScreenContext screen;
    private bool selectionRequested;
    private bool openingFrameReleased;
    private VulkanWorldSession session;
    private bool worldLoadPending;
    private bool selectionFrameReleased;
    private CharacterSelectScreen selection;
    private SpriteBatch selectionBatch;
    private string assetRoot;
    private PresentationSettings presentation;
    private MobileGraphics quality;
    private bool keyboardShown;

    [DllImport("SDL2")]
    private static extern void SDL_StartTextInput();

    [DllImport("SDL2")]
    private static extern void SDL_StopTextInput();

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_Vulkan_LoadLibrary(IntPtr path);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_Vulkan_UnloadLibrary();

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetError();

    public void RunWithVulkanLoader()
    {
        // MonoGame enumerates adapters before creating its SDL window.
        if (SDL_Vulkan_LoadLibrary(IntPtr.Zero) < 0)
        {
            throw new NoSuitableGraphicsDeviceException(Marshal.PtrToStringUTF8(SDL_GetError()));
        }

        try
        {
            Run();
        }
        finally
        {
            SDL_Vulkan_UnloadLibrary();
        }
    }

    public RicartenProbe(GameServerConnection connection, string animatedLoginEndpoint)
    {
        this.connection = connection;
        this.animatedLoginEndpoint = animatedLoginEndpoint;
        var display = Android.App.Application.Context.Resources!.DisplayMetrics!;
        var width = Math.Max(display.WidthPixels, display.HeightPixels);
        var height = Math.Min(display.WidthPixels, display.HeightPixels);
        const int renderWidth = 1280;
        graphics = new GraphicsDeviceManager(this)
        {
            IsFullScreen = true,
            PreferredBackBufferWidth = renderWidth,
            PreferredBackBufferHeight = width > 0 && height > 0 ? Math.Max(1, renderWidth * height / width) : 720,
            GraphicsProfile = GraphicsProfile.HiDef
        };
    }

    protected override void LoadContent()
    {
        var root = VulkanAssets.Root;
        Content.Dispose();
        Content = new FileContentManager(Services, Path.Combine(root, "Content"));
        assetRoot = root;
        presentation = new PresentationSettings();
        quality = new MobileGraphics(Android.App.Application.Context.FilesDir!.AbsolutePath);
        presentation.ApplyRendering(quality.Rendering);
        PristonTale.Gameplay.Localization.ClientText.Use("pt-BR");
        effects = EffectLibrary.LoadBootstrap(GraphicsDevice, Content, assetRoot, ConsoleClientLog.Instance);
        audio = PristonAudioLibrary.LoadBootstrap(assetRoot, ConsoleClientLog.Instance, loadOpeningMusic: animatedLoginEndpoint is not null);
        sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, assetRoot);
        selectionBatch = new SpriteBatch(GraphicsDevice);
        if (animatedLoginEndpoint is not null)
        {
            loginAtlas = HudAtlas.LoadBootstrap(GraphicsDevice, assetRoot, ConsoleClientLog.Instance);
        }

        screen = new ScreenContext(GraphicsDevice, Content, selectionBatch, Content.Load<SpriteFont>("Fonts/UI"),
            assetRoot, connection, null, RequestScreen, presentation, uiAtlas: loginAtlas, sky: sky, effects: effects, audio: audio,
            renewBackend: RenewBackend);
        Window.TextInput += (_, args) => screen.TypedText.Push(args.Character);
        if (animatedLoginEndpoint is not null)
        {
            opening = new OpeningScreen(screen) { PointerInput = ReadWorldPointer };
            opening.Enter();
        }
        else
        {
            StartSelection();
        }
    }

    private void RequestScreen(GameScreen next)
    {
        if (opening is not null && next is CharacterSelectScreen)
        {
            next.Dispose();
            selectionRequested = true;
        }
        else
        {
            next.Dispose();
            Exit();
        }
    }

    private void RenewBackend()
    {
        var previous = connection;
        connection = new GameServerConnection(animatedLoginEndpoint);
        screen.ReplaceBackend(connection);
        _ = previous.DisposeAsync();
    }

    private void StartSelection()
    {
        selection = new CharacterSelectScreen(screen)
        {
            PointerInput = ReadWorldPointer,
            ButtonScale = SelectionButtonScale,
            UiScale = MathF.Max(1f, GraphicsDevice.Viewport.Height / SelectionUiHeight),
            TouchKeyboard = true,
            PickScale = 2.5f,
            CameraDistanceScale = .82f,
            WorldEntered = (character, entered) =>
            {
                if (entered.FieldId is not (FieldCodes.Ricarten or FieldCodes.Pillai or 2 or FieldCodes.ForeverFall1))
                {
                    Log.Warn(Tag, $"Map {entered.FieldId} is not packaged.");
                    Exit();
                    return;
                }

                session = new VulkanWorldSession(connection, character, entered);
                ProbeApplication.SetWorld(session);
            }
        };
        selection.Enter();
    }

    private MouseState ReadSelectionPointer()
    {
        var mouse = Mouse.GetState();
        var bounds = Window.ClientBounds;
        var viewport = GraphicsDevice.Viewport;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return mouse;
        }

        return new MouseState(mouse.X * viewport.Width / bounds.Width, mouse.Y * viewport.Height / bounds.Height,
            mouse.ScrollWheelValue, mouse.LeftButton, mouse.MiddleButton, mouse.RightButton, mouse.XButton1, mouse.XButton2);
    }

    private MouseState ReadWorldPointer()
    {
        var mouse = ReadSelectionPointer();
        var viewport = GraphicsDevice.Viewport;
        var point = orbit.Tap;
        return new MouseState(point is { } tap ? (int)(tap.X * viewport.Width) : mouse.X,
            point is { } yTap ? (int)(yTap.Y * viewport.Height) : mouse.Y, mouse.ScrollWheelValue,
            point.HasValue ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released);
    }

    private void LoadWorld(string root)
    {
        presentation.ApplyRendering(quality.Rendering with { ExposureCompensation = WorldExposure });
        session.Graphics = quality;
        scene = new GameScene(GraphicsDevice, Content, sky: sky, effects: effects, presentation: presentation, audio: audio);
        var character = session.Character;
        var outcome = session.Entered;
        var entered = new EnteredCharacter(character.Class, character.Name, character.Level, character.FaceIndex,
            outcome.FieldId, outcome.SpawnX, outcome.SpawnZ, character.Tier, character.HairIndex);
        scene.LoadEntered(root, entered);
        scene.WorldPointerInput = ReadWorldPointer;
        scene.SelectCombatTargetOnTap = true;
        namePlates = new HudCombat();
        nameSprites = new HudSprites(GraphicsDevice, root, ConsoleClientLog.Instance);
        namePainter = new HudPainter(selectionBatch, screen.Font, nameSprites);
        scene.PresenceObserved += session.Observe;
        scene.PresenceObserved += namePlates.Observe;
        chat = new HudChat { SelfId = session.Entered.EntityId, PlayerName = character.Name };
        chat.Lookup = id => (namePlates.KindOf(id, out var owner), owner);
        overhead = scene.Overhead;
        speakerColor = id => chat.NameColor(id, scene.SelectedTargetId);
        nameSprites.Request(HudChatBubbles.Art);
        scene.PresenceObserved += ObserveChat;
        _ = FetchCatalogAsync(scene);
        var play = connection.OpenPlayChannel();
        scene.BeginRemotePresence(play, entered);
        scene.NpcSelected += session.SelectNpc;
        session.Bind(scene, play);
        // The automatic quest waits for the quest card's button; the card shows the way to the guide meanwhile.
        session.AssetRoot = root;
        session.Navigator = new QuestNavigator(session);
        joystickBatch = new SpriteBatch(GraphicsDevice);
        portrait = new HudTargetPortrait(GraphicsDevice);
        npcPortrait = new HudTargetPortrait(GraphicsDevice);
        targetPortrait = new HudTargetPortrait(GraphicsDevice);
        const int size = 64;
        joystickCircle = new Texture2D(GraphicsDevice, size, size);
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(size / 2f));
                pixels[y * size + x] = Color.White * Math.Clamp(size / 2f - distance, 0f, 1f);
            }
        }
        joystickCircle.SetData(pixels);
    }

    protected override void Update(GameTime gameTime)
    {
        if (opening is not null)
        {
            orbit.Update(allowJoystick: false);
            opening.Update(gameTime);
            UpdateKeyboard(opening.KeyboardRequested, opening.KeyboardFieldTapped);
            if (selectionRequested)
            {
                UpdateKeyboard(false);
                opening.Dispose();
                opening = null;
                loginAtlas?.Dispose();
                loginAtlas = null;
            }

            return;
        }

        if (selectionRequested && openingFrameReleased)
        {
            selectionRequested = false;
            StartSelection();
        }

        if (selection is not null)
        {
            orbit.Update(allowJoystick: false);
            selection.Update(gameTime);
            if (session is null)
            {
                UpdateKeyboard(selection.IsNameInputActive, selection.NameFieldTapped);
                return;
            }

            UpdateKeyboard(false);
            selection.Dispose();
            selection = null;
            worldLoadPending = true;
            return;
        }
        if (worldLoadPending && selectionFrameReleased)
        {
            worldLoadPending = false;
            Log.Info(Tag, $"Loading field {session.Entered.FieldId}.");
            LoadWorld(assetRoot);
            Log.Info(Tag, $"Loaded field {session.Entered.FieldId}.");
        }
        if (scene is null)
        {
            return;
        }
        quality.Update(presentation, gameTime.ElapsedGameTime.TotalSeconds);
        orbit.Update();
        scene.PointerCaptured = orbit.JoystickActive || orbit.CameraActive;
        scene.TouchMovement = orbit.Movement;
        scene.TouchCameraOffset = orbit.Offset;
        if (session.TakeRunToggle())
        {
            scene.ToggleRun();
        }
        scene.Update(gameTime);
        scene.PlayInitialArrival(session.Entered.EntityId);
        session.PublishOwner(scene.Owner);
        session.PublishDeath(scene.PlayerDead, scene.RestartOffer);
        var manual = orbit.Movement != Vector2.Zero;
        session.Navigator?.Update(scene, manual);
        session.UpdateAutoHunt(manual);
        session.PublishTarget(namePlates, session.AttackTarget != 0 ? session.AttackTarget : scene.SelectedTargetId);
        while (session.TryReadSpoken(out var line))
        {
            chat.Bubbles.Say(session.Entered.EntityId, line, Environment.TickCount64, ChatRules.SelfBubbleMs);
        }
    }

    private void ObserveChat(PresenceUpdate update) => chat.Observe(update, Environment.TickCount64);

    private void UpdateKeyboard(bool show, bool reopen = false)
    {
        if (keyboardShown == show && !reopen)
        {
            return;
        }

        if (show && reopen && keyboardShown)
        {
            SDL_StopTextInput();
        }

        keyboardShown = show;
        if (show)
        {
            SDL_StartTextInput();
        }
        else
        {
            SDL_StopTextInput();
        }
    }

    private async Task FetchCatalogAsync(GameScene world)
    {
        try
        {
            var catalog = await connection.GetWorldCatalogAsync();
            namePlates.SetCatalog(catalog);
            world.SetWorldRenderCatalog(catalog);
        }
        catch (Exception exception)
        {
            Log.Warn(Tag, exception.Message);
        }
    }
    // A frame is drawn only while Android lets the game keep its surface (SurfaceGate).
    protected override bool BeginDraw()
    {
        if (!SurfaceGate.TryEnterDraw())
        {
            return false;
        }

        if (base.BeginDraw())
        {
            return true;
        }

        SurfaceGate.LeaveDraw();
        return false;
    }

    protected override void EndDraw()
    {
        base.EndDraw();
        SurfaceGate.LeaveDraw();
    }

    protected override void Draw(GameTime gameTime)
    {
        if (opening is not null)
        {
            opening.Draw();
            ProbeApplication.MarkFirstFrame();
            return;
        }

        if (selection is not null)
        {
            selection.Draw();
            ProbeApplication.MarkFirstFrame();
            return;
        }

        if (scene is null)
        {
            if (selectionRequested || worldLoadPending)
            {
                GraphicsDevice.Clear(Color.Black);
                openingFrameReleased = selectionRequested;
                selectionFrameReleased = worldLoadPending;
            }
            return;
        }
        var entity = session.Entered.EntityId;
        var viewport = GraphicsDevice.Viewport;
        var layout = HudLayout.Of(viewport);
        var npc = session.NpcPortrait;
        portrait.Render(scene, new HudPortraitSubject(entity, 0, 0), layout);
        if (npc is not null)
        {
            npcPortrait.Render(scene, new HudPortraitSubject(npc.EntityId, 0, 0), layout);
        }
        var target = session.Target;
        var framed = default(HudPortraitSubject);
        var framing = target is not null && namePlates.TryGetPortraitSubject(target.Id, out framed);
        if (framing)
        {
            targetPortrait.Render(scene, framed, layout);
        }
        GraphicsDevice.Clear(scene.SkyColor);
        scene.Draw();
        nameSprites.Update();
        namePainter.Begin(layout);
        namePlates.DrawNamePlates(namePainter, layout, overhead, scene.HoveredTargetId, scene.TracedTargetId, classic: true);
        var attacked = scene.AttackTargetId != 0 ? scene.AttackTargetId : session.AttackTarget;
        namePlates.DrawTargetMarker(namePainter, layout, overhead, attacked, Environment.TickCount64, classic: true);
        chat.Bubbles.Draw(namePainter, layout, Environment.TickCount64, overhead, speakerColor, scene.SelectedTargetId);
        namePainter.End();
        var viewHeight = viewport.Height;
        if (portrait.Texture(entity) is { } avatar)
        {
            var size = (int)(viewHeight * HudPlacement.PlayerPortraitSize);
            joystickBatch.Begin();
            joystickBatch.Draw(avatar, new Rectangle((int)(viewHeight * (HudPlacement.PlayerPortraitX + HudPlacement.PlayerShiftX)),
                (int)(viewHeight * (HudPlacement.PlayerPortraitY + HudPlacement.PlayerShiftY)), size, size), Color.White);
            joystickBatch.End();
        }
        if (framing && targetPortrait.Texture(target.Id) is { } enemy)
        {
            var size = (int)(viewHeight * HudPlacement.TargetPortraitSize);
            joystickBatch.Begin();
            joystickBatch.Draw(enemy, new Rectangle((int)(HudPlacement.TargetPortraitLeft(viewport.Width, viewHeight) + viewHeight * HudPlacement.TargetShiftX),
                (int)(viewHeight * (HudPlacement.TargetPortraitY + HudPlacement.TargetShiftY)), size, size), Color.White);
            joystickBatch.End();
        }
        if (npc is not null && npcPortrait.Texture(npc.EntityId) is { } face)
        {
            var bounds = new Rectangle((int)(npc.X * viewport.Width), (int)(npc.Y * viewport.Height),
                (int)(npc.Width * viewport.Width), (int)(npc.Height * viewport.Height));
            joystickBatch.Begin();
            var scale = MathF.Min(bounds.Width / (float)face.Width, bounds.Height / (float)face.Height);
            var width = (int)(face.Width * scale);
            var height = (int)(face.Height * scale);
            joystickBatch.Draw(face, new Rectangle(bounds.Center.X - width / 2, bounds.Center.Y - height / 2, width, height), Color.White);
            joystickBatch.End();
        }

        if (orbit.JoystickActive)
        {
            var center = orbit.JoystickOrigin * new Vector2(viewport.Width, viewport.Height);
            var radius = viewport.Height * .095f;
            joystickBatch.Begin();
            DrawJoystickDisc(center, radius, new Color(193, 204, 212) * .62f);
            DrawJoystickDisc(center, radius * .94f, new Color(19, 25, 33) * .75f);
            DrawJoystickDisc(center + orbit.Movement * (radius * .65f), radius * .31f, new Color(222, 206, 162) * .86f);
            joystickBatch.End();
        }
        if (++frames == 2)
        {
            Log.Info(Tag, $"Desktop GameScene reached Ricarten: {scene.Status}");
        }
    }

    // Exposure compensation, in stops, the world is drawn with on the phone.
    private const float WorldExposure = .45f;

    private void DrawJoystickDisc(Vector2 center, float radius, Color tint)
    {
        var bounds = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2f), (int)(radius * 2f));
        joystickBatch.Draw(joystickCircle, bounds, tint);
    }

    protected override void UnloadContent()
    {
        opening?.Dispose();
        loginAtlas?.Dispose();
        selection?.Dispose();
        namePainter?.Dispose();
        nameSprites?.Dispose();
        selectionBatch?.Dispose();
        joystickCircle?.Dispose();
        joystickBatch?.Dispose();
        portrait?.Dispose();
        npcPortrait?.Dispose();
        targetPortrait?.Dispose();
        if (scene is not null)
        {
            scene.PresenceObserved -= session.Observe;
            scene.PresenceObserved -= namePlates.Observe;
            scene.PresenceObserved -= ObserveChat;
            scene.Dispose();
            session.SetClosing(scene.Closing);
        }
        sky?.Dispose();
        effects?.Dispose();
        audio?.Dispose();
    }

    private sealed class FileContentManager(IServiceProvider services, string root) : ContentManager(services, root)
    {
        protected override Stream OpenStream(string assetName) =>
            File.OpenRead(Path.Combine(RootDirectory, assetName.Replace('\\', Path.DirectorySeparatorChar) + ".xnb"));
    }
}
