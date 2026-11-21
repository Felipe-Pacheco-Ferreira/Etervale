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
using PristonTale.Gameplay.UI.Help;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// The Settings panel's options and the rules behind them: every row of the browser's panel edits a real setting, and
// each setting changes what the game does.
internal static class SettingsOptionsTests
{
    private static Task SplitOptionAsync()
    {
        Assert.True(Gameplay.UI.Widgets.HudSettingsPanel.SplitOption("Clouds: Low: On") == ("Clouds: Low", "On")
            && Gameplay.UI.Widgets.HudSettingsPanel.SplitOption("泛光：开") == ("泛光", "开")
            && Gameplay.UI.Widgets.HudSettingsPanel.SplitOption("Reset the interface") is null, "a settings row was split wrongly");
        return Task.CompletedTask;
    }

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("every row of the browser's Settings panel edits a setting of the client document", PanelFieldsAsync);
        yield return ("a settings row splits its value at the last colon, the full-width one of Chinese and Japanese too", SplitOptionAsync);
        yield return ("the new settings validate, round-trip and reach the renderer only when they change it", ValidationAsync);
        yield return ("name plates follow the per-kind switches, the own name and hide other players", NamePlatesAsync);
        yield return ("the chat filter hides boss notices and streamer lines and masks swear words whole", ChatAsync);
        yield return ("the server notice options hide aging and level notices, the party's loot by rarity and Bellatra's opening, or keep it off the screen",
            ServerNoticesAsync);
        yield return ("a potion's heal is the life gained after it, once, within its window", PotionHealAsync);
        yield return ("the party's damage per second counts the last five seconds on the target", PartyDpsAsync);
        yield return ("the skill effect switch hides other players' effects except those aimed at the player", SkillEffectsAsync);
        yield return ("the dissolve effect fades every corpse after it dims, without it only bodies of light fade", DissolveAsync);
        yield return ("the HUD scale multiplies the integer scale and the bottom bar can stay at the left", HudScaleAsync);
        yield return ("lighter textures halve their sides down to the smallest kept, weighting colour by alpha", TexturesAsync);
        yield return ("the minimap guide holds a far destination to the box's edge", MinimapGuideAsync);
        yield return ("damage numbers scale with the chosen size", DamageSizeAsync);
        yield return ("an element hit's number takes its icon and colour as the options say, a physical hit keeps its look", DamageElementAsync);
        yield return ("the chat is dragged only with move chat on, stays on the screen, keeps its place and Reset interface brings it back", MoveChatAsync);
        yield return ("a tutorial tip shows to each character until closed, never with tips off or past their last level", TutorialTipsAsync);
        yield return ("the classic interface draws the browser's HUD as on the desktop", ClassicInterfaceAsync);
    }

    private static Task MoveChatAsync()
    {
        var layout = HudLayout.Create(1280, 720);
        var window = new HudChatWindow();
        window.OpenInput();
        var onLog = new Point(150, 450 + layout.BottomOffset);
        Assert.True(window.Click(layout, onLog, side: false) && !window.Dragging, "the chat was picked up with move chat off");
        window.Movable = true;
        Assert.True(window.Click(layout, onLog, side: false) && window.Dragging, "a press on the log did not pick the chat up");
        Assert.True(!window.UpdateDrag(layout, onLog + new Point(400, -200), pressed: true) && window.Offset == new Point(400, -200),
            "the chat did not follow the pointer");
        Assert.True(window.UpdateDrag(layout, onLog + new Point(400, -200), pressed: false) && !window.Dragging, "letting go did not drop the chat");
        Assert.True(window.Contains(layout, onLog + new Point(400, -200), side: false) && !window.Contains(layout, onLog, side: false),
            "the chat was not hit where it was dropped");
        Assert.True(HudChatWindow.ClampOffset(layout, new Point(5000, -5000)) is { X: > 0, Y: < 0 } far
            && HudChatWindow.Box(layout, false) is var box && box.Right + far.X <= layout.Width && HudChatWindow.Box(layout, true).Top + far.Y >= 0,
            "the chat left the screen");

        var moved = new ClientSettings { MoveChat = true, ChatOffsetX = 400, ChatOffsetY = -200, ClassicInterface = true, TutorialTips = false, UiScale = 1.25f };
        var reloaded = ClientSettingsStore.Parse(ClientSettingsStore.Serialize(moved), ConsoleClientLog.Instance);
        Assert.True(reloaded is { ChatOffsetX: 400, ChatOffsetY: -200, MoveChat: true }, "the chat's place was not kept");
        Assert.True((moved with { ChatOffsetX = int.MaxValue }).Validate(ConsoleClientLog.Instance).ChatOffsetX == ClientSettings.MostChatOffset,
            "an out-of-range chat place was kept");
        var reset = moved.WithInterfaceReset();
        Assert.True(reset is { ChatOffsetX: 0, ChatOffsetY: 0, MoveChat: false, ClassicInterface: false, TutorialTips: true, UiScale: 1f },
            "Reset interface did not bring the interface back");
        return Task.CompletedTask;
    }

    private static Task TutorialTipsAsync()
    {
        var settings = new ClientSettings();
        Assert.True(HudTutorialTips.Current(settings, "Ann", 1) == 0, "a new character did not see the first tip");
        var closed = HudTutorialTips.Closed(settings, "Ann", HudTutorialTips.Tips[0].Id);
        Assert.True(HudTutorialTips.Current(closed, "Ann", 1) == 1 && HudTutorialTips.Current(closed, "Bob", 1) == 0,
            "a closed tip came back, or closed for another character");
        Assert.True(ReferenceEquals(HudTutorialTips.Closed(closed, "Ann", HudTutorialTips.Tips[0].Id), closed), "a tip was closed twice");
        Assert.True(HudTutorialTips.Current(closed with { TutorialTips = false }, "Ann", 1) < 0 && HudTutorialTips.Current(closed, "Ann", HudTutorialTips.MostLevel + 1) < 0,
            "a tip showed with tips off or past the last level");
        var all = settings;
        foreach (var tip in HudTutorialTips.Tips)
        {
            all = HudTutorialTips.Closed(all, "Ann", tip.Id);
        }

        Assert.True(HudTutorialTips.Current(all, "Ann", 1) < 0, "a tip showed after all were closed");
        var full = settings with { TutorialTipsClosed = [.. Enumerable.Range(0, HudTutorialTips.MostClosed).Select(index => $"Old{index}/Move")] };
        Assert.True(HudTutorialTips.Closed(full, "Ann", "Move").TutorialTipsClosed is { Length: HudTutorialTips.MostClosed } kept && kept[^1] == "Ann/Move" && kept[0] == "Old1/Move",
            "the closed tips grew past their limit or dropped the newest");
        return Task.CompletedTask;
    }

    private static Task ClassicInterfaceAsync()
    {
        var classic = new ClientSettings { ClassicInterface = true };
        Assert.True(HudLayout.BrowserLook(true, new ClientSettings()) && !HudLayout.BrowserLook(true, classic) && !HudLayout.BrowserLook(false, new ClientSettings()),
            "the classic interface did not switch the browser's look off");
        return Task.CompletedTask;
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

    private static Task ServerNoticesAsync()
    {
        var aging = new ChatLineInfo(ChatKind.Notice, 0, "", GameText.Of(GameTextId.AgingNotice, "Ann", "Axe", "12"));
        var level = new ChatLineInfo(ChatKind.Notice, 0, "", GameText.Of(GameTextId.LevelNotice, "Ann", "100"));
        var loot = new ChatLineInfo(ChatKind.Party, 0, "", GameText.Of(GameTextId.PartyLoot, "Ann", "Axe"));
        var rare = new ChatLineInfo(ChatKind.Party, 0, "", GameText.Of(GameTextId.PartyRareLoot, "Ann", "Axe"));
        var bellatra = new ChatLineInfo(ChatKind.Notice, 0, "", GameText.Of(GameTextId.BellatraOpening));
        var defaults = new ClientSettings();
        Assert.True(!ChatLineFilter.Hides(defaults, aging) && !ChatLineFilter.Hides(defaults, level) && ChatLineFilter.Hides(defaults, loot)
            && !ChatLineFilter.Hides(defaults, rare) && !ChatLineFilter.Hides(defaults, bellatra) && ChatLineFilter.OnScreen(defaults, bellatra)
            && !ChatLineFilter.OnScreen(defaults, aging), "the default notices changed");
        var hidden = defaults with { HideAgingNotices = true, HideLevelUpNotices = true, PartyLootAlerts = PartyLootAlerts.Off, BellatraOpeningNotice = BellatraOpeningNotice.Off };
        Assert.True(ChatLineFilter.Hides(hidden, aging) && ChatLineFilter.Hides(hidden, level) && ChatLineFilter.Hides(hidden, loot)
            && ChatLineFilter.Hides(hidden, rare) && ChatLineFilter.Hides(hidden, bellatra), "a notice switched off still showed");
        var chatOnly = defaults with { PartyLootAlerts = PartyLootAlerts.All, BellatraOpeningNotice = BellatraOpeningNotice.Chat };
        Assert.True(!ChatLineFilter.Hides(chatOnly, loot) && !ChatLineFilter.Hides(chatOnly, bellatra) && !ChatLineFilter.OnScreen(chatOnly, bellatra),
            "every item's loot was hidden, or Bellatra's opening reached the screen in the chat-only choice");
        var streamer = defaults with { StreamerMode = true, StreamerHidesWholeChat = true };
        Assert.True(!ChatLineFilter.Hides(streamer, rare), "the whole-chat streamer mode hid the party's loot notice");

        var read = ClientSettingsStore.Parse(ClientSettingsStore.Serialize(hidden), NullClientLog.Instance);
        Assert.True(read is { HideAgingNotices: true, HideLevelUpNotices: true, PartyLootAlerts: PartyLootAlerts.Off, BellatraOpeningNotice: BellatraOpeningNotice.Off },
            "the notice options did not round-trip");
        Assert.True(hidden with { PartyLootAlerts = (PartyLootAlerts)9, BellatraOpeningNotice = (BellatraOpeningNotice)9 } is var invalid
            && invalid.Validate(NullClientLog.Instance) is { PartyLootAlerts: PartyLootAlerts.Rare, BellatraOpeningNotice: BellatraOpeningNotice.Both },
            "an unknown notice choice was kept");
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

    private static Task DamageElementAsync()
    {
        var defaults = new ClientSettings();
        Assert.True(defaults is { ShowElementIcon: true, ColorDamageByElement: false }, "the element options do not default to icon on, colour off");
        var read = ClientSettingsStore.Parse(ClientSettingsStore.Serialize(defaults with { ShowElementIcon = false, ColorDamageByElement = true }),
            NullClientLog.Instance);
        Assert.True(read is { ShowElementIcon: false, ColorDamageByElement: true }, "the element options did not round-trip");

        var white = Color.White;
        var fire = DamageElementStyle.NumberColor(white, Element.Fire, colorByElement: true);
        Assert.True(fire != white && fire != DamageElementStyle.NumberColor(white, Element.Ice, colorByElement: true),
            "fire and ice numbers do not take their own colours");
        Assert.True(DamageElementStyle.NumberColor(white, Element.Fire, colorByElement: false) == white
            && DamageElementStyle.NumberColor(white, Element.Bionic, colorByElement: true) == white, "a number took a colour the options or the hit do not give");

        Element[] withIcon = [Element.Fire, Element.Ice, Element.Lighting, Element.Poison];
        var icons = withIcon.Select(element => DamageElementStyle.IconOf(element, showIcon: true)).ToArray();
        Assert.True(icons.All(icon => icon.Width == DamageElementStyle.IconPixels && icon.Height == DamageElementStyle.IconPixels)
            && icons.Distinct().Count() == withIcon.Length, "fire, ice, lightning and poison do not each have their own icon");
        Assert.True(withIcon.All(element => DamageElementStyle.IconOf(element, showIcon: false).IsEmpty)
            && DamageElementStyle.IconOf(Element.Bionic, showIcon: true).IsEmpty, "an icon showed with the option off or on a physical hit");
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
