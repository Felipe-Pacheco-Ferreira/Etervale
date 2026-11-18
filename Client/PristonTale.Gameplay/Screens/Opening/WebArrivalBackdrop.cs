using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Assets;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;

namespace PristonTale.Gameplay.Screens.Opening;

internal sealed class WebArrivalBackdrop : IDisposable
{
    private const float ApproachSeconds = 4.8f;
    private const float EnterSeconds = 2f;
    private const float NightHour = 23.4f;
    private const string BrazierEffect = "vfx.webarrival.brazier";
    private const string CharacterArrivalEffect = "vfx.webarrival.character";
    private const string ClassSelectionEffect = "vfx.webarrival.class-selection";

    private readonly record struct CameraPose(Vector3 Eye, Vector3 Target, float FieldOfView)
    {
        internal static CameraPose Blend(CameraPose start, CameraPose end, float amount) =>
            new(Vector3.Lerp(start.Eye, end.Eye, amount), Vector3.Lerp(start.Target, end.Target, amount),
                MathHelper.Lerp(start.FieldOfView, end.FieldOfView, amount));
    }

    private static readonly CameraPose Overlook =
        new(new Vector3(400f, 380f, 950f), new Vector3(0f, 75f, 90f), MathHelper.ToRadians(53f));
    private static readonly CameraPose Gate =
        new(new Vector3(-105f, 142f, 675f), new Vector3(-130f, 68f, 75f), MathHelper.ToRadians(43f));

    private static readonly DayNightKey Moonlight =
        new(0f, new Vector3(0.38f, 0.46f, 0.56f), new Vector3(0.22f, 0.28f, 0.37f), new Vector3(0.055f, 0.09f, 0.166f));

    private static readonly DayNightSettings CourtyardLighting = new()
    {
        DayLengthSeconds = 0f,
        Gradient = [Moonlight, Moonlight with { Hour = 24f }]
    };

    private static readonly PristonDistanceFog CourtyardFog = PristonDistanceFog.Atmospheric with
    {
        StartDistance = 490f,
        Density = 0.00115f,
        MaxOpacity = 0.55f,
        InScatter = 0f
    };

    private static readonly Vector3[] Braziers = [new(-120f, 32f, 65f), new(120f, 32f, 65f)];

    private readonly GraphicsDevice _device;
    private readonly ScreenContext _context;
    private readonly VfxSystem _vfx;
    private readonly VfxLibrary _vfxLibrary;
    private CompiledVfx _characterArrival;
    private CompiledVfx _classSelectionEffect;
    private VfxHandle _classSelection;
    private readonly VfxRenderer _vfxRenderer;
    private readonly CharSelectFlames _flames;
    private readonly CameraPose _roster;
    private CameraPose _pose;
    private CameraPose _entryStart;
    private float _flightSeconds;
    private float _enterSeconds;

    public WebArrivalBackdrop(ScreenContext context)
    {
        _context = context;
        _device = context.GraphicsDevice;
        Stage = WebArrivalLandscape.Build(context.AssetRoot);
        var night = PristonSkyProfiles.Forest.Night;
        Renderer = new PristonRenderer(_device, context.Content, context.Log, context.Presentation)
        {
            Sky = context.Sky,
            SkyProfiles = new PristonSkyProfiles(night, night, night),
            ActorFloorLight = false
        };
        try
        {
            ApplySceneSettings(context.Presentation.Rendering);
            // The renderer applies the global options first; keep the courtyard's authored atmospheric profile.
            context.Presentation.RenderingChanged += ApplySceneSettings;
            Renderer.ConfigureDayNight(CourtyardLighting);
            Renderer.SetTimeOfDay(NightHour);
            Renderer.ConfigureFieldLighting(intensity: 0.5f, rangeScale: 1f, overrideLights: null, glowIntensity: 0.6f, glowScale: 0.85f);
            Renderer.AddStage(Stage, Array.Empty<PristonModel>());

            var (eye, target) = CharacterSelectCamera.Pose(CharacterSelectStage.RailDistance(CharacterSelectStage.RosterStep), 0f, webBackdrop: true);
            _roster = new CameraPose(eye, target, CharacterSelectStage.CameraFieldOfView);
            Camera = new PristonCamera(Overlook.Eye, Vector3.Forward);
            SetPose(Overlook);

            _vfxLibrary = context.Effects?.Vfx.Library;
            if (_vfxLibrary is not null)
            {
                if (!_vfxLibrary.TryGet(BrazierEffect, out var flame))
                {
                    flame = _vfxLibrary.Register(CreateBrazierEffect());
                }

                _vfx = new VfxSystem(VfxNoAnchors.Instance, new TransientLightServices(Renderer.TransientLights), _vfxLibrary);
                _vfxRenderer = new VfxRenderer(_device, context.Content);
                _flames = new CharSelectFlames(_vfx, flame, Braziers);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public PristonStage Stage { get; }
    public PristonRenderer Renderer { get; }
    public PristonCamera Camera { get; }
    public bool Entering { get; private set; }
    internal WebClassPreviewWarmup PreviewWarmup { get; private set; }
    public bool AtRoster => _enterSeconds >= EnterSeconds;

    internal static WebArrivalBackdrop CreateAtRoster(ScreenContext context)
    {
        var backdrop = new WebArrivalBackdrop(context);
        try
        {
            backdrop.BeginEntry();
            backdrop._enterSeconds = EnterSeconds;
            backdrop.SetPose(backdrop._roster);
            return backdrop;
        }
        catch
        {
            backdrop.Dispose();
            throw;
        }
    }

    public void BeginEntry()
    {
        Entering = true;
        _entryStart = _pose;
        if (_context.CharacterAssetsReady is not null)
        {
            PreviewWarmup = new WebClassPreviewWarmup(Renderer, _context);
        }
    }

    public void UpdateFlight(GameTime gameTime)
    {
        var seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (Entering)
        {
            _enterSeconds = Math.Min(EnterSeconds, _enterSeconds + seconds);
            SetPose(CameraPose.Blend(_entryStart, _roster, MathHelper.SmoothStep(0f, 1f, _enterSeconds / EnterSeconds)));
        }
        else
        {
            if (WebOpeningClock.TryTakeRequest(out var seek))
            {
                _flightSeconds = (float)Math.Clamp(seek, 0d, ApproachSeconds);
            }

            _flightSeconds += seconds;
            var travel = MathHelper.SmoothStep(0f, 1f, _flightSeconds / ApproachSeconds);
            var sway = MathF.Sin(Math.Max(0f, _flightSeconds - ApproachSeconds) * 0.55f);
            var gate = Gate with { Eye = Gate.Eye + new Vector3(17f, 4f, 0f) * sway };
            SetPose(CameraPose.Blend(Overlook, gate, travel));
            WebOpeningClock.Seconds = _flightSeconds;
        }

        UpdateScene(gameTime);
    }

    public void SpawnCharacter(Vector3 position)
    {
        if (_vfx is null)
        {
            return;
        }

        if (_characterArrival is null && !_vfxLibrary.TryGet(CharacterArrivalEffect, out _characterArrival))
        {
            _characterArrival = _vfxLibrary.Register(CreateCharacterArrival());
        }

        _vfx.Spawn(_characterArrival, new VfxSpawn(0, 0, position, 0f));
    }

    public void SetClassSelection(Vector3? position)
    {
        if (_vfx is null)
        {
            return;
        }

        if (position is not { } point)
        {
            _classSelection.StopNow();
            _classSelection = default;
            return;
        }

        if (_classSelection.Alive)
        {
            _classSelection.MoveTo(point);
            return;
        }

        _classSelectionEffect ??= _vfxLibrary.TryGet(ClassSelectionEffect, out var existing)
            ? existing : _vfxLibrary.Register(CreateClassSelection());
        _classSelection = _vfx.Spawn(_classSelectionEffect, new VfxSpawn(0, 0, point, 0f));
    }

    private static VfxDefinition CreateClassSelection() => new()
    {
        Name = ClassSelectionEffect,
        Duration = VfxDefinition.MaxDuration,
        Components =
        [
            new VfxComponentDefinition
            {
                Kind = VfxComponentKind.Ground,
                Follow = true,
                Texture = "Effect/Particle/warp_magicsquare01a.tga",
                Offset = [0f, 1.7f, 0f],
                SizeMin = [48f, 48f],
                SizeMax = [48f, 48f],
                Color = [255f, 192f, 73f, 195f],
                EndTime = [1f, 1f]
            },
            new VfxComponentDefinition
            {
                Kind = VfxComponentKind.Billboard,
                Follow = true,
                Texture = "Effect/Particle/flare.tga",
                Offset = [0f, 25f, 0f],
                SizeMin = [34f, 65f],
                SizeMax = [34f, 65f],
                Color = [255f, 200f, 96f, 36f],
                EndTime = [1f, 1f]
            }
        ]
    };

    public void UpdateScene(GameTime gameTime)
    {
        _flames?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        Renderer.Update(gameTime);
    }

    public void Draw()
    {
        _device.Clear(Renderer.SkyColor);
        Renderer.Draw(Camera);
        _vfxRenderer?.Draw(_vfx, Camera.View, Camera.Projection, Camera.Position, Renderer.SceneLinearDepth);
        Renderer.ComposeScene();
    }

    private static VfxDefinition CreateCharacterArrival() => new()
    {
        Name = CharacterArrivalEffect,
        Duration = 1.25f,
        Parts =
        [
            new VfxPartDefinition
            {
                Shape = VfxPartShape.Ground,
                Texture = "Effect/Particle/warp_magicsquare01a.tga",
                Lifetime = [1.05f, 1.05f],
                Color = [255f, 187f, 55f, 205f],
                Size = [52f, 52f],
                Offset = [0f, 1.3f, 0f],
                Spin = 23f,
                Fade = VfxPartFade.InOut,
                FadeIn = 0.14f,
                FadeOut = 0.54f
            },
            new VfxPartDefinition
            {
                Texture = "Effect/Particle/flare.tga",
                Lifetime = [0.65f, 0.65f],
                Color = [255f, 212f, 139f, 100f],
                Size = [32f, 32f],
                Height = [67f, 67f],
                Offset = [0f, 28f, 0f],
                Fade = VfxPartFade.InOut,
                FadeIn = 0.13f,
                FadeOut = 0.45f
            },
            new VfxPartDefinition
            {
                Texture = "Effect/Particle/flare.tga",
                Count = 14,
                DelayStep = 0.025f,
                Lifetime = [0.32f, 0.64f],
                Color = [255f, 213f, 133f, 205f],
                Size = [1.4f, 2.8f],
                Offset = [0f, 12f, 0f],
                Shell = [19f, 10f, 19f],
                VelocityMin = [-5f, 17f, -5f],
                VelocityMax = [5f, 33f, 5f],
                Fade = VfxPartFade.Fall
            }
        ],
        Lights =
        [
            new VfxLightDefinition { Offset = [0f, 31f, 10f], Color = [1f, 0.76f, 0.49f], Power = 114, Decay = 5, Delay = 0.08f }
        ]
    };

    private static VfxDefinition CreateBrazierEffect()
    {
        var flame = CharSelectEffects.GreenBeamDefinition().Parts[0] with
        {
            Color = [255f, 170f, 88f, 168f],
            Lifetime = [0.48f, 0.8f],
            Size = [19f, 23f],
            Growth = 2f,
            FadeOut = 0.55f,
            VelocityMin = [-6f, 18f, -4f],
            VelocityMax = [5f, 30f, 5f]
        };
        var ember = flame with
        {
            Count = 7,
            DelayStep = 0.26f,
            DelayRandom = 0.16f,
            Lifetime = [0.75f, 1.3f],
            Size = [2.8f, 2.8f],
            Color = [255f, 198f, 105f, 220f],
            Growth = 0f,
            VelocityMin = [-13f, 24f, -12f],
            VelocityMax = [14f, 42f, 12f]
        };
        return new VfxDefinition
        {
            Name = BrazierEffect,
            Duration = CharSelectEffects.BeamPeriod + 1.3f,
            Parts = [flame, ember]
        };
    }

    private void SetPose(CameraPose pose)
    {
        _pose = pose;
        Camera.SetScriptedView(_device, pose.Eye, pose.Target, Vector3.Up, pose.FieldOfView);
    }

    private void ApplySceneSettings(ClientSettings settings)
    {
        Renderer.DistanceFog = settings.DistanceFog == DistanceFogMode.Off ? PristonDistanceFog.Disabled : CourtyardFog;
        Renderer.ExposureCompensation = 0.2f + settings.ExposureCompensation;
    }

    public void Dispose()
    {
        _context.Presentation.RenderingChanged -= ApplySceneSettings;
        _vfxRenderer?.Dispose();
        Renderer.Dispose();
    }
}
