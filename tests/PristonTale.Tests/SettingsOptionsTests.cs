using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Contracts.World;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// The Settings panel's options and the rules behind them: every row of the browser's panel edits a real setting, and
// each setting changes what the game does.
internal static class SettingsOptionsTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("every row of the browser's Settings panel edits a setting of the client document", PanelFieldsAsync);
        yield return ("the new settings validate, round-trip and reach the renderer only when they change it", ValidationAsync);
        yield return ("name plates follow the per-kind switches, the own name and hide other players", NamePlatesAsync);
        yield return ("the chat filter hides boss notices and streamer lines and masks swear words whole", ChatAsync);
        yield return ("a potion's heal is the life gained after it, once, within its window", PotionHealAsync);
        yield return ("the party's damage per second counts the last five seconds on the target", PartyDpsAsync);
        yield return ("the skill effect switch hides other players' effects except those aimed at the player", SkillEffectsAsync);
        yield return ("the dissolve effect fades every corpse after it dims, without it only bodies of light fade", DissolveAsync);
        yield return ("the HUD scale multiplies the integer scale and the bottom bar can stay at the left", HudScaleAsync);
        yield return ("lighter textures halve their sides down to the smallest kept, weighting colour by alpha", TexturesAsync);
        yield return ("the minimap guide holds a far destination to the box's edge", MinimapGuideAsync);
        yield return ("damage numbers scale with the chosen size", DamageSizeAsync);
    }

    private static readonly ChatLineInfo Whisper = new(ChatKind.Whisper, 0, "From> Ann: hi");
    private static readonly ChatLineInfo Spoken = new(ChatKind.Player, 42, "Ann: hello");
    private static readonly ChatLineInfo Boss = new(ChatKind.Notice, 0, "", GameText.Of(GameTextId.BossTime, 30));

    private static Task PanelFieldsAsync()
    {
        var path = Path.Combine(LocalizationTests.RepositoryRoot(), "Client", "PristonTale.Browser", "wwwroot", "settings-options.js");
        var source = File.ReadAllText(path);
        var fields = Regex.Matches(source, @"(?:toggle|select|volume)\('([A-Za-z0-9]+)'|field: '([A-Za-z0-9]+)'")
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .ToHashSet(StringComparer.Ordinal);
        var document = JsonNode.Parse(ClientSettingsStore.Serialize(new ClientSettings()))!.AsObject();
        var missing = fields.Where(field => !document.ContainsKey(field)).ToArray();
        Assert.True(fields.Count > 60, "the panel's rows were not found");
        Assert.True(missing.Length == 0, "panel rows edit settings the client does not have: " + string.Join(", ", missing));
        Assert.True(!Regex.IsMatch(source, @"(?:toggle|select)\(null"), "a panel row edits no setting");
        return Task.CompletedTask;
    }

    private static Task ValidationAsync()
    {
        var invalid = ClientSettingsStore.Parse("{\"version\":1,\"fogDistance\":50,\"uiScale\":3}", NullClientLog.Instance);
        Assert.True(invalid is { FogDistance: ClientSettings.DefaultFogDistance, UiScale: 1f }, "an invalid fog distance or HUD scale was kept");
        var chosen = new ClientSettings
        {
            HideOtherPlayers = true, OwnNameColor = NameColor.Pink, DamageNumberSize = DamageNumberSize.Huge, TextureQuality = TextureQuality.VeryLight,
            StreamerMode = true, FogDistance = 3000, ActorShadowsOnly = true, UiScale = 1.25f, ConfirmClose = false
        };
        var read = ClientSettingsStore.Parse(ClientSettingsStore.Serialize(chosen), NullClientLog.Instance);
        Assert.True(read is { HideOtherPlayers: true, OwnNameColor: NameColor.Pink, DamageNumberSize: DamageNumberSize.Huge, TextureQuality: TextureQuality.VeryLight,
            StreamerMode: true, FogDistance: 3000, ActorShadowsOnly: true, UiScale: 1.25f, ConfirmClose: false }, "the new settings did not round-trip");
        var defaults = new ClientSettings();
        Assert.True(!defaults.HasSameRendering(defaults with { FogDistance = 3000 }) && !defaults.HasSameRendering(defaults with { ActorShadowsOnly = true })
            && defaults.HasSameRendering(defaults with { HideOtherPlayers = true }), "the renderer was not told of a rendering setting, or was of a gameplay one");
        Assert.True(!chosen.WithQuality(GraphicsQuality.Medium).ActorShadowsOnly, "a preset kept the characters-only shadows");
        Assert.True(PristonDistanceFog.For(DistanceFogMode.Atmospheric, 3000f).StartDistance == 3000f
            && !PristonDistanceFog.For(DistanceFogMode.Off, 3000f).Enabled, "the fog did not start at the chosen distance");
        Assert.True(ClientSettings.DefaultFogDistance == (int)PristonDistanceFog.OriginalStartDistance, "the default fog moved");
        return Task.CompletedTask;
    }

    private static Task NamePlatesAsync()
    {
        var all = NamePlateFilter.From(new ClientSettings());
        Assert.True(all.Allows(EntityKind.Npc, false, false, false) && all.Allows(EntityKind.Player, false, false, false)
            && !all.Allows(EntityKind.Player, false, false, true), "the default plates changed or the own name shows by default");
        var noMonsters = NamePlateFilter.From(new ClientSettings { HideMonsterNames = true, HideNpcNames = true });
        Assert.True(!noMonsters.Allows(EntityKind.Monster, false, false, false) && noMonsters.Allows(EntityKind.Monster, true, false, false)
            && !noMonsters.Allows(EntityKind.Npc, false, false, false) && noMonsters.Allows(EntityKind.Player, false, false, false),
            "a kind switched off still showed, or lost its name under the cursor");
        var hidden = NamePlateFilter.From(new ClientSettings { HideOtherPlayers = true, ShowOwnName = true, OwnNameColor = NameColor.Gold });
        Assert.True(!hidden.Allows(EntityKind.Player, true, false, false) && hidden.Allows(EntityKind.Player, false, true, false)
            && hidden.Allows(EntityKind.Player, false, false, true), "hidden players kept a name, or the party or own name was hidden");
        Assert.True(NamePlateFilter.ColorOf(NameColor.Gold) != NamePlateFilter.ColorOf(NameColor.White), "the own name colour is ignored");
        Assert.True(NamePlateFilter.From(new ClientSettings { PersistentCharacterInfo = true }).AllPlayers, "Backspace's information did not reach the plates");
        return Task.CompletedTask;
    }

    private static Task ChatAsync()
    {
        var defaults = new ClientSettings();
        Assert.True(!ChatLineFilter.Hides(defaults, Whisper) && !ChatLineFilter.Hides(defaults, Spoken) && !ChatLineFilter.Hides(defaults, Boss),
            "the default chat hides a line");
        Assert.True(ChatLineFilter.Hides(defaults with { HideBossNotices = true }, Boss), "the boss notice was not hidden");
        var streamer = defaults with { StreamerMode = true };
        Assert.True(ChatLineFilter.Hides(streamer, Whisper) && !ChatLineFilter.Hides(streamer, Spoken) && !ChatLineFilter.Hides(streamer, Boss),
            "streamer mode hid the wrong lines");
        var whole = streamer with { StreamerHidesWholeChat = true };
        Assert.True(ChatLineFilter.Hides(whole, Spoken) && ChatLineFilter.Hides(whole, new ChatLineInfo(ChatKind.Party, 0, "hi"))
            && !ChatLineFilter.Hides(whole, Boss) && !ChatLineFilter.Hides(whole, new ChatLineInfo(ChatKind.Server, 0, "Maintenance")),
            "the whole-chat streamer mode hid a notice or kept a written line");
        Assert.True(ChatCensor.Mask("Ann: vai PUTÁ merda!") == "Ann: vai **** *****!" && ChatCensor.Mask("o computador") == "o computador",
            "the censor missed a word or masked part of one");
        Assert.True(ChatCensor.MaskMessage("From> Merda: oi merda") == "From> Merda: oi *****", "the censor masked the speaker's name");
        return Task.CompletedTask;
    }

    private static Task PotionHealAsync()
    {
        var watch = new PotionHealWatch();
        Assert.True(watch.Gained(500, 0) == 0, "a heal was reported without a potion");
        watch.Drank(400, 1000);
        Assert.True(watch.Gained(400, 1100) == 0 && watch.Gained(520, 1200) == 120 && watch.Gained(600, 1300) == 0,
            "the potion's heal was not reported once");
        watch.Drank(400, 2000);
        Assert.True(watch.Gained(700, 2000 + PotionHealWatch.WindowMs + 1) == 0, "a gain after the window was taken for the potion");
        return Task.CompletedTask;
    }

    private static Task PartyDpsAsync()
    {
        var meter = new PartyDpsMeter();
        meter.Record(7, 1000, 0);
        Assert.True(meter.Dps(7, 0) == 1000 && meter.Dps(8, 0) == 0, "one hit did not read as a second's damage");
        meter.Record(7, 1000, 1000);
        meter.Record(8, 500, 1000);
        Assert.True(meter.Dps(7, 2000) == 1000, "two hits over two seconds did not average");
        Assert.True(meter.Dps(7, 1000 + PartyDpsMeter.WindowMs + 1) == 0, "old hits stayed counted");
        return Task.CompletedTask;
    }

    private static Task SkillEffectsAsync()
    {
        const ulong self = 1, other = 2, monster = 3;
        Assert.True(OtherPlayerEffects.HiddenBySwitch(true, other, monster, self, true)
            && !OtherPlayerEffects.HiddenBySwitch(true, other, self, self, true)
            && !OtherPlayerEffects.HiddenBySwitch(true, self, monster, self, true)
            && !OtherPlayerEffects.HiddenBySwitch(true, monster, other, self, false)
            && !OtherPlayerEffects.HiddenBySwitch(false, other, monster, self, true), "the skill effect switch hid the wrong effects");
        return Task.CompletedTask;
    }

    private static Task DissolveAsync()
    {
        var ordinary = CharacterSoundCodes.Resolve("DEADZONE");
        Assert.True(MonsterPresentation.CorpseFade(ordinary, 10_000f) == 1f && MonsterPresentation.CorpseFade(ordinary, 100f, true) == 1f
            && MonsterPresentation.CorpseFade(ordinary, 10_000f, true) == 0f, "a corpse dissolved without the switch or before it dimmed");
        Assert.True(MonsterPresentation.CorpseFade(CharacterSoundCodes.Figon, 64f) == MonsterPresentation.CorpseFade(CharacterSoundCodes.Figon, 64f, true),
            "a body of light faded differently with the switch");
        return Task.CompletedTask;
    }

    private static Task HudScaleAsync()
    {
        Assert.True(HudLayout.Create(1920, 1080).Scale == 1f && HudLayout.Create(1920, 1080, 1.5f) is { Scale: 1.5f, Width: 1280, Height: 720 },
            "the HUD scale did not multiply the integer scale");
        Assert.True(HudLayout.Create(800, 600, 1.5f).Scale == 1f, "a larger HUD scale pushed the design off the screen");
        Assert.True(HudSettingsPanel.NextUiScale(1f, 1) == 1.1f && HudSettingsPanel.NextUiScale(0.75f, -1) == 1.5f && HudSettingsPanel.NextUiScale(1.2f, 1) == 1.5f,
            "the desktop panel's HUD scale did not cycle its steps");
        var small = HudLayout.Create(1920, 1080, 0.75f);
        Assert.True(small.Scale == 0.75f && small.ToLogical(new Point(300, 150)) == new Point(400, 200) && !small.WholeScale,
            "a smaller HUD scale did not map the pointer");
        var wide = HudLayout.Create(1920, 1080) with { Browser = true };
        Assert.True(wide.BottomBarOffset > 0 && (wide with { CenteredBar = false }).BottomBarOffset == 0, "the bottom bar did not follow the centring switch");
        return Task.CompletedTask;
    }

    private static Task TexturesAsync()
    {
        var rgba = new byte[64 * 64 * 4];
        for (var index = 0; index < 64 * 64; index++)
        {
            var opaque = index % 2 == 0;
            rgba[index * 4] = opaque ? (byte)200 : (byte)0;
            rgba[index * 4 + 3] = opaque ? (byte)255 : (byte)0;
        }

        var (width, height, half) = TextureDownscale.Shrink(64, 64, rgba, TextureDownscale.Divisor(TextureQuality.Light));
        Assert.True(width == 32 && height == 32 && half[0] == 200 && half[3] == 127, "a lighter texture did not halve or let clear pixels darken it");
        Assert.True(TextureDownscale.Shrink(64, 64, rgba, TextureDownscale.Divisor(TextureQuality.VeryLight)).Width == 32,
            "a texture shrank below the smallest side");
        Assert.True(ReferenceEquals(TextureDownscale.Shrink(64, 64, rgba, TextureDownscale.Divisor(TextureQuality.Original)).Rgba, rgba),
            "the original textures were copied");
        return Task.CompletedTask;
    }

    private static Task MinimapGuideAsync()
    {
        var player = new Vector3(1000f, 0f, 1000f);
        Assert.True(HudMinimap.GuideOffset(player, new Vector3(1010f, 0f, 1000f), 640f) == HudMinimap.MarkerOffset(player, new Vector3(1010f, 0f, 1000f), 640f),
            "a near destination was moved");
        var far = HudMinimap.GuideOffset(player, new Vector3(1000f, 0f, 90_000f), 640f);
        Assert.True(far.X == 0f && far.Y == 58f, "a far destination was not held to the edge on its bearing");
        return Task.CompletedTask;
    }

    private static Task DamageSizeAsync()
    {
        Assert.True(FloatingCombatText.ScaleOf(DamageNumberSize.Small) < FloatingCombatText.ScaleOf(DamageNumberSize.Normal)
            && FloatingCombatText.ScaleOf(DamageNumberSize.Normal) == 1f
            && FloatingCombatText.ScaleOf(DamageNumberSize.Large) < FloatingCombatText.ScaleOf(DamageNumberSize.Huge), "the number sizes are out of order");
        return Task.CompletedTask;
    }
}
