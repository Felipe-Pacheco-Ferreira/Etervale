using System.Text.Json.Serialization;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Engine3D.Rendering;

public enum CameraMouseAction
{
    Default, Disabled, RotateLeft, RotateRight, ZoomIn, ZoomOut, TiltUp, TiltDown
}

[JsonConverter(typeof(JsonStringEnumConverter<WindowMode>))]
public enum WindowMode
{
    Windowed,
    Borderless,
    Fullscreen
}

// The HUD for mouse and keyboard (Pc) or for touch (Touch); Auto picks by the device (a browser without a fine pointer
// is a phone or tablet).
[JsonConverter(typeof(JsonStringEnumConverter<InterfaceMode>))]
public enum InterfaceMode
{
    Auto,
    Pc,
    Touch
}

public sealed record ClientSettings
{
    // MSAA takes 2 or 4 samples.
    public const int MostMultiSamples = 4;
    public const int MostAnisotropy = 16;
    public const float MostPixelRatio = 2f;

    public const int CurrentVersion = 1;
    public const string EnglishLanguage = "en";
    public const string BrazilianPortugueseLanguage = "pt-BR";
    public static IReadOnlyList<string> SupportedLanguages { get; } = [EnglishLanguage, BrazilianPortugueseLanguage];

    public static string NormalizeLanguage(string language)
    {
        foreach (var supported in SupportedLanguages)
        {
            if (string.Equals(language, supported, StringComparison.OrdinalIgnoreCase))
            {
                return supported;
            }
        }

        return EnglishLanguage;
    }

    public int Version { get; init; } = CurrentVersion;
    // The game server: host, host:port or tcp://host:port, optionally with ?cert=<sha-256 hex> (TlsTransport.ParseAddress).
    public string ServerAddress { get; init; } = "localhost";
    public string Language { get; init; } = EnglishLanguage;
    public int Monitor { get; init; } = -1;
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 720;
    public WindowMode WindowMode { get; init; }
    // Where the window was left in windowed mode: its client area's top-left in desktop coordinates (Window.Position)
    // while not maximized, and whether it was maximized. Without a position the window opens centred. Not part of the
    // display: moving or maximizing the window applies nothing.
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public bool WindowMaximized { get; init; }
    public int RefreshRate { get; init; }
    public bool VerticalSync { get; init; }
    public bool MusicEnabled { get; init; } = true;
    public bool SoundEnabled { get; init; } = true;
    public int MusicVolumePercent { get; init; } = 100;
    public int SoundVolumePercent { get; init; } = 100;
    public int MaxFps { get; init; }
    public int BackgroundMaxFps { get; init; } = 30;
    public float UiScale { get; init; } = 1f;
    public float RenderScale { get; init; } = 1f;
    // Browser only: the most drawing pixels per CSS pixel (1, 1.5 or 2); the canvas uses the lower of this and the
    // display's density, so it changes nothing on a display at 100 % scaling. The desktop renderer ignores it.
    public float MaxPixelRatio { get; init; } = MostPixelRatio;
    public bool DynamicResolution { get; init; } = true;
    public int[] ShortcutKeys { get; init; } = [];
    public string[] GameServers { get; init; } = [];
    public bool VoiceEnabled { get; init; }
    public int VoiceReceiveMask { get; init; } = 15;
    public int VoiceVolumePercent { get; init; } = 80;
    public int VoiceTransmitChannel { get; init; }
    public string VoiceMicrophone { get; init; } = "";
    public const string CommunityDiscordUrl = "https://discord.com/invite/4FdF7Mes2B";
    public const string CommunityInstagramUrl = "https://www.instagram.com/noxiustale";
    public string DiscordUrl { get; init; } = CommunityDiscordUrl;
    public string InstagramUrl { get; init; } = CommunityInstagramUrl;
    public string XUrl { get; init; } = "";
    public string ItemShopUrl { get; init; } = "";
    public string AuctionUrl { get; init; } = "";
    public string CoinHistoryUrl { get; init; } = "";
    public string EragonUrl { get; init; } = "";
    public string CommunityBuffsUrl { get; init; } = "";
    public string NewsEndpoint { get; init; } = "";

    // The newest news post the player has seen and the answered tickets the player has read ("id:updated"), so the
    // News & Support plaque marks only what is new.
    public string NewsSeen { get; init; } = "";
    public string[] TicketsSeen { get; init; } = [];
    public bool DisableGroundLootFilter { get; init; }
    public bool HideGroundGold { get; init; }
    public bool HideGroundPotions { get; init; }
    public bool HideGroundWeapons { get; init; }
    public int GroundMinimumEquipmentLevel { get; init; }
    public int GroundMaximumEquipmentLevel { get; init; }
    public uint HiddenGroundSpecJobs { get; init; }
    public bool HideGroundEquipmentWithoutSpec { get; init; }
    public bool AlwaysShowRareGroundEquipment { get; init; }
    public string[] AlwaysShowGroundNames { get; init; } = [];
    public ulong HiddenGroundCategories { get; init; }
    public bool HideTradeChat { get; init; }
    public bool HideClanMembershipNotices { get; init; }
    public bool ShowExperienceNotices { get; init; }
    public bool HideCalendarNotices { get; init; }
    public bool HideGlobalBuffNotices { get; init; }
    public bool HideEventRewardNotices { get; init; }
    public bool ShowPremiumBuffNotices { get; init; }
    public bool HideBellatraResults { get; init; }
    public bool HideBellatraBossNotices { get; init; }
    public bool HideBellatraTeamNotices { get; init; }
    public bool HideCastleNotices { get; init; }
    public CameraMouseAction CameraMouse4 { get; init; }
    public CameraMouseAction CameraMouse5 { get; init; }
    public bool InvertCameraOrbit { get; init; }
    public bool InvertCameraScroll { get; init; }
    public int CameraMouseButtons { get; init; }
    public bool HideQuestTracker { get; init; }
    public bool HideOnlineCount { get; init; }
    public bool ShowMinimap { get; init; } = true;
    public InterfaceMode InterfaceMode { get; init; }
    public TextureQuality TextureQuality { get; init; }
    // Browser page: the prompt before leaving while in the world, the grey screen while dead, the pulsing red border
    // under 30 % of life (settings-effects.js), and a system notification of the death while the tab is hidden.
    public bool ConfirmClose { get; init; } = true;
    public bool GrayscaleOnDeath { get; init; } = true;
    public bool LowHealthWarning { get; init; } = true;
    public bool NotifyOnDeath { get; init; }
    public bool HideOtherPlayers { get; init; }
    public bool HidePlayerNames { get; init; }
    public bool HideNpcNames { get; init; }
    public bool HideMonsterNames { get; init; }
    public bool ShowOwnName { get; init; }
    public NameColor OwnNameColor { get; init; }
    public DamageNumberSize DamageNumberSize { get; init; } = DamageNumberSize.Normal;
    // Hits between other entities (a party member's, another player's) also float their numbers, dimmer than yours.
    public bool ShowOtherPlayersDamage { get; init; } = true;
    // Every player's name over the character, not only the hovered one's; Backspace switches it in game.
    public bool PersistentCharacterInfo { get; init; }
    // The game starts each world entry running (R switches to walking until the next entry).
    public bool RunByDefault { get; init; } = true;
    // Browser only: the bottom bar centred under a wide screen instead of at the left.
    public bool CenteredInterface { get; init; } = true;
    // Browser only: the scenery casts no sun shadow, characters still do (the panel's "Simples" shadows).
    public bool ActorShadowsOnly { get; init; }
    public bool SpreadDamageNumbers { get; init; }
    public bool HidePotionHeals { get; init; }
    public bool HideSkillEffects { get; init; }
    public bool HideStealthEffect { get; init; }
    public bool MonsterDissolve { get; init; } = true;
    // Where the distance fog starts, in world units (PristonDistanceFog.For).
    public int FogDistance { get; init; } = DefaultFogDistance;
    public const int DefaultFogDistance = (int)PristonDistanceFog.OriginalStartDistance;
    public const int NearestFogDistance = 1000;
    public const int FarthestFogDistance = 4000;
    public bool ChatFilter { get; init; }
    public bool StreamerMode { get; init; }
    public bool StreamerHidesWholeChat { get; init; }
    public bool PrioritizePlayers { get; init; }
    public bool HideBossNotices { get; init; }
    public bool HideHudIcons { get; init; }
    // The damage per second the local player and its party deal to the target, under the target frame.
    public bool ShowPartyDps { get; init; } = true;
    // An arrow on the minimap toward the destination of the quest the player follows.
    public bool MinimapGuide { get; init; } = true;
    public int MultiSampleCount { get; init; } = 4;
    // Browser only: texture samples along a slanted surface (1 is off). The desktop renderer always uses MostAnisotropy.
    public int AnisotropicFilter { get; init; } = MostAnisotropy;
    public int ShadowMapSize { get; init; } = 4096;
    // Cube faces of the lamp shadows (the nearest LampShadowCount lamps, 0, 2 or 4), in pixels.
    public int LampShadowTileSize { get; init; } = 512;
    public int LampShadowCount { get; init; } = 4;
    // Image-based lighting from the sky dome (irradiance and prefiltered reflections); off: a hemispherical ambient.
    public bool ImageBasedLighting { get; init; } = true;
    // Browser only: the original look, the texture times the stage's baked vertex light under an even, bright ambient
    // with no display transform, instead of the physically based light. The desktop renderer ignores it.
    public bool ClassicLighting { get; init; }
    // Screen-space reflections of smooth surfaces (water, ice) over the image-based reflection; needs the depth pre-pass.
    public bool ScreenSpaceReflections { get; init; }
    // Browser only: the screen-space reflections show the scenery and leave characters and monsters out.
    public bool SceneryOnlyReflections { get; init; }
    // Browser only: the screen-space reflections are traced on alternate frames.
    public bool ReflectionsEveryOtherFrame { get; init; } = true;
    // How smoothly the crowd's characters animate with their distance (CrowdAnimationLod).
    public CharacterQuality CharacterQuality { get; init; } = CharacterQuality.High;
    // The hover outline around the character, monster or NPC under the cursor.
    public bool OutlineGlow { get; init; } = true;
    // The coloured glow and mix overlay of held weapons and shields with aging or mix.
    public bool AgingMixGlow { get; init; } = true;
    // Other players' skill effects far from the camera, or beyond a count near it, are not drawn (SkillPresentation).
    public bool LimitOtherPlayerEffects { get; init; } = true;
    // Look development: an 18 % grey and a chrome sphere beside the camera focus.
    public bool CalibrationSpheres { get; init; }
    // Sunlight scattered by a height fog, with the shafts the shadows cut (needs the depth pre-pass).
    public bool VolumetricLight { get; init; } = true;
    public int ShadowUpdateRate { get; init; } = 60;
    public bool ShadowsEnabled { get; init; } = true;
    // Classic: the target HP bar appears only after you attack a monster at full HP, without numbers.
    public bool ClassicTargetHealthBar { get; init; }
    // Classic: the name boxes over monsters and NPCs use the speech bubble frame and the HUD text size, and a monster's
    // life bar shows while the cursor is on it; otherwise a compact box, the bar once the monster was clicked or attacked.
    public bool HideUnfocusedNames { get; init; }
    public bool HideDamageNumbers { get; init; }
    public bool AlwaysShowGroundLabels { get; init; }
    public bool AlwaysShowRareGroundLabels { get; init; }
    public bool ClassicNamePlates { get; init; }
    // The level guide's notice on reaching a level it has a guide for (its On/Off switch in the guide window).
    public bool LevelGuide { get; init; } = true;
    // The weather the server sends: clouds, rain, lightning and their sounds; off keeps the sky clear.
    public bool WeatherEnabled { get; init; } = true;
    // The physical sky (sun, moon, stars, volumetric clouds) instead of the original's painted domes, and its clouds.
    public bool RealisticSky { get; init; } = true;
    public CloudQuality CloudQuality { get; init; } = CloudQuality.Medium;
    // Atmospheric fades distant geometry toward the captured sky up to a capped opacity; Classic is still accepted but
    // is drawn as Atmospheric; Off disables it.
    public DistanceFogMode DistanceFog { get; init; } = DistanceFogMode.Atmospheric;
    // Development view of the surface shader; also cycled in game with Ctrl+F10.
    public PristonDebugView DebugView { get; init; }
    // The preset the rendering options below came from (WithQuality); Custom once one of them is changed by hand.
    public GraphicsQuality Quality { get; init; } = GraphicsQuality.High;
    public AntiAliasingMode AntiAliasing { get; init; } = AntiAliasingMode.Taa;
    // Automatic exposure (luminance histogram, adaptation over time); off uses the light model's incident reading.
    public bool AutoExposure { get; init; } = true;
    // Exposure compensation in stops, -3..3, added to the automatic or fixed exposure.
    public float ExposureCompensation { get; init; }
    public TonemapperMode Tonemapper { get; init; } = TonemapperMode.PbrNeutral;
    public BloomQuality Bloom { get; init; } = BloomQuality.High;
    // Browser only, the panel's "Gráficos aprimorados" under the default light (the classic light turns them all off):
    // the tone curve, darker corners, a faint moving grain, and the glow's strength in percent (0-200).
    public bool ToneMapping { get; init; } = true;
    public bool Vignette { get; init; } = true;
    public bool FilmGrain { get; init; }
    public int BloomIntensityPercent { get; init; } = 100;
    // The built-in colour grade (a 3D LUT after the display transform), or the .cube file ColorGradingLut names.
    public bool ColorGrading { get; init; } = true;
    public string ColorGradingLut { get; init; } = string.Empty;
    // Contrast-adaptive sharpening of the final image, 0..1.
    public float Sharpening { get; init; } = 0.6f;
    // Stage lamps and combat lights shaded per pixel: 8, 16 or 32, the nearest to the camera focus.
    public int LocalLightLimit { get; init; } = PristonShaderLibrary.MaxLocalLightCount;
    // Cascades of the sun's shadow map (1-4) in an atlas of ShadowMapSize, and their filter.
    public int ShadowCascades { get; init; } = 4;
    public ShadowFilterMode ShadowFilter { get; init; } = ShadowFilterMode.Pcss;
    // Ground-truth ambient occlusion and screen-space contact shadows from a depth pre-pass (not with MSAA).
    public bool AmbientOcclusion { get; init; } = true;
    public bool ContactShadows { get; init; } = true;

    public ClientSettings Validate(IClientLog log)
    {
        var defaults = new ClientSettings();
        var valid = this with
        {
            ServerAddress = string.IsNullOrWhiteSpace(ServerAddress) ? defaults.ServerAddress : ServerAddress,
            Language = NormalizeLanguage(Language),
            Monitor = Monitor >= -1 ? Monitor : defaults.Monitor,
            WindowWidth = WindowWidth is >= 800 and <= 16384 ? WindowWidth : defaults.WindowWidth,
            WindowHeight = WindowHeight is >= 600 and <= 16384 ? WindowHeight : defaults.WindowHeight,
            WindowMode = Enum.IsDefined(WindowMode) ? WindowMode : defaults.WindowMode,
            InterfaceMode = Enum.IsDefined(InterfaceMode) ? InterfaceMode : defaults.InterfaceMode,
            TextureQuality = Enum.IsDefined(TextureQuality) ? TextureQuality : defaults.TextureQuality,
            OwnNameColor = Enum.IsDefined(OwnNameColor) ? OwnNameColor : defaults.OwnNameColor,
            DamageNumberSize = Enum.IsDefined(DamageNumberSize) ? DamageNumberSize : defaults.DamageNumberSize,
            FogDistance = FogDistance is >= NearestFogDistance and <= FarthestFogDistance ? FogDistance : defaults.FogDistance,
            RefreshRate = RefreshRate is >= 0 and <= 1000 ? RefreshRate : defaults.RefreshRate,
            MaxFps = IsValidFps(MaxFps) ? MaxFps : defaults.MaxFps,
            BackgroundMaxFps = IsValidFps(BackgroundMaxFps) ? BackgroundMaxFps : defaults.BackgroundMaxFps,
            UiScale = float.IsFinite(UiScale) && UiScale is >= 0.75f and <= 1.5f ? UiScale : defaults.UiScale,
            RenderScale = float.IsFinite(RenderScale) && RenderScale is >= 0.5f and <= 1f ? RenderScale : defaults.RenderScale,
            MaxPixelRatio = MaxPixelRatio is 1f or 1.5f or MostPixelRatio ? MaxPixelRatio : defaults.MaxPixelRatio,
            MusicVolumePercent = Math.Clamp(MusicVolumePercent, 0, 100),
            SoundVolumePercent = Math.Clamp(SoundVolumePercent, 0, 100),
            MultiSampleCount = MultiSampleCount is 0 or 2 or MostMultiSamples ? MultiSampleCount : defaults.MultiSampleCount,
            AnisotropicFilter = AnisotropicFilter is 1 or 2 or 4 or 8 or MostAnisotropy ? AnisotropicFilter : defaults.AnisotropicFilter,
            ShadowMapSize = ShadowMapSize is 1024 or 2048 or 4096 or 8192 ? ShadowMapSize : defaults.ShadowMapSize,
            LampShadowTileSize = LampShadowTileSize is 256 or 512 or 1024 ? LampShadowTileSize : defaults.LampShadowTileSize,
            ShadowUpdateRate = ShadowUpdateRate is 0 or 30 or 60 or 120 ? ShadowUpdateRate : defaults.ShadowUpdateRate,
            DistanceFog = Enum.IsDefined(DistanceFog) ? DistanceFog : defaults.DistanceFog,
            CloudQuality = Enum.IsDefined(CloudQuality) ? CloudQuality : defaults.CloudQuality,
            DebugView = Enum.IsDefined(DebugView) ? DebugView : defaults.DebugView,
            Quality = Enum.IsDefined(Quality) ? Quality : defaults.Quality,
            CharacterQuality = Enum.IsDefined(CharacterQuality) ? CharacterQuality : defaults.CharacterQuality,
            AntiAliasing = Enum.IsDefined(AntiAliasing) ? AntiAliasing : defaults.AntiAliasing,
            ExposureCompensation = float.IsFinite(ExposureCompensation) && ExposureCompensation is >= -3f and <= 3f
                ? ExposureCompensation
                : defaults.ExposureCompensation,
            Tonemapper = Enum.IsDefined(Tonemapper) ? Tonemapper : defaults.Tonemapper,
            Bloom = Enum.IsDefined(Bloom) ? Bloom : defaults.Bloom,
            BloomIntensityPercent = BloomIntensityPercent is >= 0 and <= 200 ? BloomIntensityPercent : defaults.BloomIntensityPercent,
            ColorGradingLut = ColorGradingLut ?? defaults.ColorGradingLut,
            Sharpening = float.IsFinite(Sharpening) && Sharpening is >= 0f and <= 1f ? Sharpening : defaults.Sharpening,
            LocalLightLimit = LocalLightLimit is 8 or 16 or PristonShaderLibrary.MaxLocalLightCount ? LocalLightLimit : defaults.LocalLightLimit,
            ShadowCascades = ShadowCascades is >= 1 and <= PristonShadowCascades.MaxCascades ? ShadowCascades : defaults.ShadowCascades,
            ShadowFilter = Enum.IsDefined(ShadowFilter) ? ShadowFilter : defaults.ShadowFilter,
            LampShadowCount = LampShadowCount is 0 or 2 or PristonLampShadows.MaxLamps ? LampShadowCount : defaults.LampShadowCount
        };

        if (valid != this)
        {
            log.Warn("[Settings] Invalid presentation values replaced with defaults.");
        }

        return valid;
    }

    // The rendering options of a preset; display, audio and gameplay options are kept. Measured GPU frame times per
    // preset are listed in docs/client-settings.md.
    public ClientSettings WithQuality(GraphicsQuality quality) => quality switch
    {
        GraphicsQuality.Low => this with
        {
            Quality = quality,
            CharacterQuality = CharacterQuality.Low,
            VolumetricLight = false,
            ScreenSpaceReflections = false,
            LampShadowCount = 0,
            LampShadowTileSize = 512,
            ShadowCascades = 2,
            ShadowFilter = ShadowFilterMode.Pcf,
            AmbientOcclusion = false,
            ContactShadows = false,
            LocalLightLimit = 8,
            AntiAliasing = AntiAliasingMode.Off,
            AnisotropicFilter = 4,
            MaxPixelRatio = 1f,
            ShadowMapSize = 2048,
            ShadowUpdateRate = 30,
            CloudQuality = CloudQuality.Low,
            ShadowsEnabled = true,
            ActorShadowsOnly = false,
            Bloom = BloomQuality.Off,
            AutoExposure = true,
            ColorGrading = true,
            Sharpening = 0f
        },
        GraphicsQuality.Medium => this with
        {
            Quality = quality,
            CharacterQuality = CharacterQuality.Medium,
            VolumetricLight = false,
            ScreenSpaceReflections = false,
            LampShadowCount = 2,
            LampShadowTileSize = 512,
            ShadowCascades = 3,
            ShadowFilter = ShadowFilterMode.Pcf,
            AmbientOcclusion = true,
            ContactShadows = false,
            LocalLightLimit = 16,
            AntiAliasing = AntiAliasingMode.Smaa,
            AnisotropicFilter = 8,
            MaxPixelRatio = 1.5f,
            ShadowMapSize = 2048,
            ShadowUpdateRate = 60,
            CloudQuality = CloudQuality.Low,
            ShadowsEnabled = true,
            ActorShadowsOnly = false,
            Bloom = BloomQuality.High,
            AutoExposure = true,
            ColorGrading = true,
            Sharpening = 0.4f
        },
        GraphicsQuality.High => this with
        {
            Quality = quality,
            CharacterQuality = CharacterQuality.High,
            VolumetricLight = true,
            ScreenSpaceReflections = false,
            LampShadowCount = 4,
            LampShadowTileSize = 512,
            ShadowCascades = 4,
            ShadowFilter = ShadowFilterMode.Pcss,
            AmbientOcclusion = true,
            ContactShadows = true,
            LocalLightLimit = 32,
            AntiAliasing = AntiAliasingMode.Taa,
            AnisotropicFilter = MostAnisotropy,
            MaxPixelRatio = MostPixelRatio,
            ShadowMapSize = 4096,
            ShadowUpdateRate = 60,
            CloudQuality = CloudQuality.Medium,
            ShadowsEnabled = true,
            ActorShadowsOnly = false,
            Bloom = BloomQuality.High,
            AutoExposure = true,
            ColorGrading = true,
            Sharpening = 0.6f
        },
        GraphicsQuality.Ultra => this with
        {
            Quality = quality,
            CharacterQuality = CharacterQuality.VeryHigh,
            VolumetricLight = true,
            ScreenSpaceReflections = true,
            LampShadowCount = 4,
            LampShadowTileSize = 1024,
            ShadowCascades = 4,
            ShadowFilter = ShadowFilterMode.Pcss,
            AmbientOcclusion = true,
            ContactShadows = true,
            LocalLightLimit = 32,
            AntiAliasing = AntiAliasingMode.Taa,
            AnisotropicFilter = MostAnisotropy,
            MaxPixelRatio = MostPixelRatio,
            ShadowMapSize = 4096,
            ShadowUpdateRate = 0,
            CloudQuality = CloudQuality.High,
            ShadowsEnabled = true,
            ActorShadowsOnly = false,
            Bloom = BloomQuality.High,
            AutoExposure = true,
            ColorGrading = true,
            Sharpening = 0.6f
        },
        _ => this with { Quality = GraphicsQuality.Custom }
    };

    // The options PristonRenderer reads (PresentationSettings.ApplyRendering).
    public bool HasSameRendering(ClientSettings other)
    {
        return RenderScale == other.RenderScale
            && DynamicResolution == other.DynamicResolution
            && ShadowMapSize == other.ShadowMapSize
            && LampShadowTileSize == other.LampShadowTileSize
            && ShadowUpdateRate == other.ShadowUpdateRate
            && ShadowsEnabled == other.ShadowsEnabled
            && DistanceFog == other.DistanceFog
            && DebugView == other.DebugView
            && MultiSampleCount == other.MultiSampleCount
            && AntiAliasing == other.AntiAliasing
            && AutoExposure == other.AutoExposure
            && ExposureCompensation == other.ExposureCompensation
            && Tonemapper == other.Tonemapper
            && Bloom == other.Bloom
            && ColorGrading == other.ColorGrading
            && ToneMapping == other.ToneMapping
            && Vignette == other.Vignette
            && FilmGrain == other.FilmGrain
            && BloomIntensityPercent == other.BloomIntensityPercent
            && AnisotropicFilter == other.AnisotropicFilter
            && ColorGradingLut == other.ColorGradingLut
            && Sharpening == other.Sharpening
            && LocalLightLimit == other.LocalLightLimit
            && ShadowCascades == other.ShadowCascades
            && ShadowFilter == other.ShadowFilter
            && AmbientOcclusion == other.AmbientOcclusion
            && ContactShadows == other.ContactShadows
            && LampShadowCount == other.LampShadowCount
            && ImageBasedLighting == other.ImageBasedLighting
            && ClassicLighting == other.ClassicLighting
            && ScreenSpaceReflections == other.ScreenSpaceReflections
            && ActorShadowsOnly == other.ActorShadowsOnly
            && FogDistance == other.FogDistance
            && SceneryOnlyReflections == other.SceneryOnlyReflections
            && ReflectionsEveryOtherFrame == other.ReflectionsEveryOtherFrame
            && CharacterQuality == other.CharacterQuality
            && CalibrationSpheres == other.CalibrationSpheres
            && VolumetricLight == other.VolumetricLight
            && RealisticSky == other.RealisticSky
            && CloudQuality == other.CloudQuality;
    }

    public bool HasSameDisplay(ClientSettings other)
    {
        return Monitor == other.Monitor
            && WindowWidth == other.WindowWidth
            && WindowHeight == other.WindowHeight
            && WindowMode == other.WindowMode
            && RefreshRate == other.RefreshRate
            && MultiSampleCount == other.MultiSampleCount;
    }

    public int FrameLimit(bool active)
    {
        if (active || BackgroundMaxFps == 0)
        {
            return MaxFps;
        }

        return MaxFps == 0 ? BackgroundMaxFps : Math.Min(MaxFps, BackgroundMaxFps);
    }

    // Whether the touch HUD is used on a device that is (true) or is not a touch-only one.
    public bool UsesTouch(bool touchDevice) => InterfaceMode == InterfaceMode.Touch || InterfaceMode == InterfaceMode.Auto && touchDevice;

    private static bool IsValidFps(int value) => value == 0 || value is >= 15 and <= 1000;
}
