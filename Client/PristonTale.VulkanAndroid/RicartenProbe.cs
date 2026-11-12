using PristonTale.Gameplay.World;
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
using PristonTale.Simulation.World;
using PristonTale.Contracts.Motion;
using PristonTale.Simulation.Characters;
using System;
using System.IO;
using System.IO.Compression;
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
    private readonly GraphicsDeviceManager graphics;
    private readonly TouchOrbit orbit = new();
    private GameScene scene;
    private EffectLibrary effects;
    private PristonSkyLibrary sky;
    private PristonAudioLibrary audio;
    private SpriteBatch joystickBatch;
    private Texture2D joystickCircle;
    private int frames;
    private readonly GameServerConnection connection;
    private VulkanWorldSession session;
    private CharacterSelectScreen selection;
    private SpriteBatch selectionBatch;
    private string assetRoot;
    private PresentationSettings presentation;
    private bool keyboardShown;

    [DllImport("SDL2")]
    private static extern void SDL_StartTextInput();

    [DllImport("SDL2")]
    private static extern void SDL_StopTextInput();

    public RicartenProbe(GameServerConnection connection)
    {
        this.connection = connection;
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
        var context = Android.App.Application.Context;
        var root = Path.Combine(context.CacheDir!.AbsolutePath, "ricarten-desktop-11");
        Directory.CreateDirectory(root);
        var ready = Path.Combine(root, "extracted");
        if (!File.Exists(ready))
        {
            var package = Path.Combine(root, "assets.zip");
            try
            {
                using (var input = context.Assets!.Open("ricarten-desktop.zip"))
                using (var output = File.Create(package))
                {
                    input.CopyTo(output);
                }
                ZipFile.ExtractToDirectory(package, root, overwriteFiles: true);
                File.WriteAllText(ready, string.Empty);
            }
            finally
            {
                File.Delete(package);
            }
        }

        Log.Info(Tag, "Assets extracted.");
        Content.Dispose();
        Content = new FileContentManager(Services, Path.Combine(root, "Content"));
        assetRoot = root;
        presentation = new PresentationSettings();
        presentation.ApplyRendering(new ClientSettings().WithQuality(GraphicsQuality.Medium));
        PristonTale.Gameplay.Localization.ClientText.Use("pt-BR");
        effects = EffectLibrary.LoadBootstrap(GraphicsDevice, Content, assetRoot, ConsoleClientLog.Instance);
        audio = PristonAudioLibrary.LoadBootstrap(assetRoot, ConsoleClientLog.Instance);
        selectionBatch = new SpriteBatch(GraphicsDevice);
        var screen = new ScreenContext(GraphicsDevice, Content, selectionBatch, Content.Load<SpriteFont>("Fonts/UI"),
            assetRoot, connection, null, _ => Exit(), presentation, effects: effects, audio: audio);
        Window.TextInput += (_, args) => screen.TypedText.Push(args.Character);
        selection = new CharacterSelectScreen(screen)
        {
            PointerInput = ReadSelectionPointer,
            WorldEntered = (character, entered) =>
            {
                if (entered.FieldId != FieldCodes.Ricarten)
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

    private void LoadWorld(string root)
    {
        sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, root);
        presentation.ApplyRendering(new ClientSettings().WithQuality(GraphicsQuality.High));
        scene = new GameScene(GraphicsDevice, Content, sky: sky, effects: effects, presentation: presentation, audio: audio);
        var character = session.Character;
        var outcome = session.Entered;
        var entered = new EnteredCharacter(character.Class, character.Name, character.Level, character.FaceIndex,
            outcome.FieldId, outcome.SpawnX, outcome.SpawnZ, character.Tier, character.HairIndex);
        scene.LoadEntered(root, entered);
        scene.PresenceObserved += session.Observe;
        scene.BeginRemotePresence(connection.OpenPlayChannel(), entered);
        _ = FetchCatalogAsync(scene);
        session.Bind(scene);
        joystickBatch = new SpriteBatch(GraphicsDevice);
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
        if (selection is not null)
        {
            selection.Update(gameTime);
            if (session is null)
            {
                if (selection.IsNameInputActive != keyboardShown)
                {
                    keyboardShown = selection.IsNameInputActive;
                    if (keyboardShown)
                        SDL_StartTextInput();
                    else
                        SDL_StopTextInput();
                }
                return;
            }

            if (keyboardShown)
                SDL_StopTextInput();
            selection.Dispose();
            selection = null;
            LoadWorld(assetRoot);
        }
        if (scene is null)
        {
            return;
        }
        orbit.Update();
        scene.PointerCaptured = orbit.JoystickActive || orbit.CameraActive;
        scene.TouchMovement = orbit.Movement;
        scene.TouchCameraOffset = orbit.Offset;
        if (session.TakeRunToggle())
        {
            scene.ToggleRun();
        }
        scene.Update(gameTime);
        session.PublishOwner(scene.Owner);
    }

    private async Task FetchCatalogAsync(GameScene world)
    {
        try
        {
            world.SetWorldRenderCatalog(await connection.GetWorldCatalogAsync());
        }
        catch (Exception exception)
        {
            Log.Warn(Tag, exception.Message);
        }
    }
    protected override void Draw(GameTime gameTime)
    {
        if (selection is not null)
        {
            selection.Draw();
            return;
        }

        if (scene is null)
        {
            return;
        }
        GraphicsDevice.Clear(scene.SkyColor);
        scene.Draw();
        if (orbit.JoystickActive)
        {
            var viewport = GraphicsDevice.Viewport;
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

    private void DrawJoystickDisc(Vector2 center, float radius, Color tint)
    {
        var bounds = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2f), (int)(radius * 2f));
        joystickBatch.Draw(joystickCircle, bounds, tint);
    }

    protected override void UnloadContent()
    {
        selection?.Dispose();
        selectionBatch?.Dispose();
        joystickCircle?.Dispose();
        joystickBatch?.Dispose();
        if (scene is not null)
        {
            scene.PresenceObserved -= session.Observe;
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
