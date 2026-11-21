using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Gameplay.Audio;
using Microsoft.Xna.Framework.Audio;

namespace PristonTale.Gameplay.UI.Widgets;

// Display and audio settings are applied only when the player saves the draft.
// The interface scale (uiScale) cycles through UiScales; the browser's page panel takes any step between them.
internal sealed partial class HudSettingsPanel
{
    private const int FirstSpecOption = ChatTranslateOption + 1;
    private const int DungeonCameraOption = 214;
    private const int MoveChatOption = 215;
    private const int TutorialTipsOption = 216;
    private const int ResetInterfaceOption = 217;
    // Shown only while the server translates the chat (ChatTranslationAvailable).
    private const int ChatTranslateOption = 218;
    // The HUD scale's row, between the voice rows and the loot level rows.
    private const int UiScaleOption = 115;
    private static readonly float[] UiScales = [0.75f, 0.9f, 1f, 1.1f, 1.25f, 1.5f];
    private static readonly int[] MouseActions = Enum.GetValues<CameraMouseAction>().Select(action => (int)action).ToArray();
    private static readonly (JobBitCode Job, string Label)[] LootJobs =
    [
        (JobBitCode.Mechanician, "LootFilter.Job.Mechanician"), (JobBitCode.Fighter, "LootFilter.Job.Fighter"),
        (JobBitCode.Pikeman, "LootFilter.Job.Pikeman"), (JobBitCode.Archer, "LootFilter.Job.Archer"),
        (JobBitCode.Knight, "LootFilter.Job.Knight"), (JobBitCode.Atalanta, "LootFilter.Job.Atalanta"),
        (JobBitCode.Priestess, "LootFilter.Job.Priestess"), (JobBitCode.Magician, "LootFilter.Job.Magician"),
        (JobBitCode.Assassin, "LootFilter.Job.Assassin"), (JobBitCode.MartialArtist, "LootFilter.Job.MartialArtist"),
        (JobBitCode.Shaman, "LootFilter.Job.Shaman")
    ];
    private static readonly int[] EquipmentLevels = [.. Enumerable.Range(0, 256)];
    private const int RowHeight = 22;
    private static readonly int[][] Tabs =
    [
        Columns([11, -2, 9, 13, 10, 12, 21, -3, 24, 25, 26, 23, 22, -4, 0, 1, 2, 3, 4, 7, 8, UiScaleOption],
            [209, 210, 211, 17, 18, MoveChatOption, ResetInterfaceOption]),
        [5, 6, 100, 101, 110, 111, 103, 108, 102, 109, 104, 112, 105, 113, 106, 114, 107, 114],
        [],
        Columns([19, -5, 14, 20, TutorialTipsOption, 15, 16, 32, 30, 31, 33, -6, 27, 28, 29,
            .. Enumerable.Range(34, GroundLootFilter.Labels.Length), 190, 191, 192, 193, 194, 196, .. Enumerable.Range(FirstSpecOption, LootJobs.Length)],
            [195, -7, DungeonCameraOption, 197, 200, 201, -8, 198, 199, 202, 203, 204, 205, 206, 207, 208, 212, 213, ChatTranslateOption])
    ];

    private static int[] Columns(int[] left, int[] right)
    {
        var rows = new int[Math.Max(left.Length, right.Length) * 2];
        Array.Fill(rows, -1);
        for (var index = 0; index < left.Length; index++)
        {
            rows[index * 2] = left[index];
        }

        for (var index = 0; index < right.Length; index++)
        {
            rows[index * 2 + 1] = right[index];
        }

        return rows;
    }

    private static readonly string[] TabLabels = ["Settings.Tab.Video", "Settings.Tab.Audio", "Settings.Tab.Keys", "Settings.Tab.Other"];
    private static readonly string[] VoiceChannels = ["Settings.Voice.Nearby", "Settings.Voice.Party", "Settings.Voice.Clan", "Settings.Voice.Map"];
    public const int InventoryShortcut = 0;
    public const int CharacterShortcut = 1;
    public const int SkillsShortcut = 2;
    public const int SettingsShortcut = 7;
    public const int FirstBeltShortcut = 10;
    public const int FirstSkillShortcut = 13;
    public const int MinimapShortcut = FirstSkillShortcut + HudSkillBindings.ShortcutCount;
    public const int NewsShortcut = MinimapShortcut + 1;
    public const int GroundLabelsShortcut = NewsShortcut + 1;
    private const int FirstCameraShortcut = GroundLabelsShortcut + 1;
    public const int FirstActionBarShortcut = FirstCameraShortcut + 6;
    public const int VoiceShortcut = FirstActionBarShortcut + HudSkillBindings.ShortcutCount;
    public const int HideHudShortcut = VoiceShortcut + 1;
    public const int PersistentInfoShortcut = HideHudShortcut + 1;
    public const int DungeonCameraShortcut = PersistentInfoShortcut + 1;
    public const int PvpShortcut = DungeonCameraShortcut + 1;
    private static readonly Keys[] ActionBarKeys = [Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0, Keys.OemMinus];
    internal static Keys CameraKey(ClientSettings settings, Keys action) => action switch
    {
        Keys.Left => KeyOf(settings, FirstCameraShortcut),
        Keys.Right => KeyOf(settings, FirstCameraShortcut + 1),
        Keys.Up => KeyOf(settings, FirstCameraShortcut + 2),
        Keys.Down => KeyOf(settings, FirstCameraShortcut + 3),
        Keys.PageUp => KeyOf(settings, FirstCameraShortcut + 4),
        Keys.PageDown => KeyOf(settings, FirstCameraShortcut + 5),
        _ => action
    };
    // A shortcut's name is its phrase key; Number, when not zero, is the slot the phrase numbers.
    private static readonly (string Name, int Number, Keys Key)[] Shortcuts =
    [
        ("Shortcut.Inventory", 0, Keys.V), ("Shortcut.Character", 0, Keys.C), ("Shortcut.Skills", 0, Keys.S),
        ("Shortcut.Quests", 0, Keys.Q), ("Shortcut.Social", 0, Keys.D), ("Shortcut.SystemMenu", 0, Keys.X),
        ("Shortcut.RunWalk", 0, Keys.R), ("Shortcut.Settings", 0, Keys.F10),
        ("Shortcut.SwitchBag", 0, Keys.E), ("Shortcut.SwitchWeapons", 0, Keys.W),
        ("Shortcut.BeltPotion", 1, Keys.D1), ("Shortcut.BeltPotion", 2, Keys.D2), ("Shortcut.BeltPotion", 3, Keys.D3),
        .. Enumerable.Range(0, HudSkillBindings.ShortcutCount).Select(index => ("Shortcut.Skill", index + 1, Keys.F1 + index)),
        ("Shortcut.Minimap", 0, Keys.Tab), ("Shortcut.News", 0, Keys.H), ("Shortcut.GroundNames", 0, Keys.A),
        ("Shortcut.RotateLeft", 0, Keys.Left), ("Shortcut.RotateRight", 0, Keys.Right),
        ("Shortcut.ZoomIn", 0, Keys.Up), ("Shortcut.ZoomOut", 0, Keys.Down),
        ("Shortcut.TiltUp", 0, Keys.PageUp), ("Shortcut.TiltDown", 0, Keys.PageDown),
        .. ActionBarKeys.Select((key, index) => ("Shortcut.ActionBar", index + 1, key)),
        ("Shortcut.PushToTalk", 0, Keys.G),
        ("Shortcut.HideHud", 0, Keys.F12),
        ("Shortcut.PersistentInfo", 0, Keys.Back),
        ("Shortcut.DungeonCamera", 0, Keys.Z),
        ("Shortcut.Pvp", 0, Keys.K)
    ];

    private static readonly int[] ShortcutRows = Columns(
        [1, 0, 2, 3, 4, 5, 7, NewsShortcut, -9, MinimapShortcut, GroundLabelsShortcut, PersistentInfoShortcut, HideHudShortcut, -10, 6, 8, 9, PvpShortcut, -11,
            VoiceShortcut],
        [.. Enumerable.Range(FirstSkillShortcut, HudSkillBindings.ShortcutCount),
         .. Enumerable.Range(FirstActionBarShortcut, ActionBarKeys.Length), -12, 10, 11, 12,
         -7, .. Enumerable.Range(FirstCameraShortcut, 6), DungeonCameraShortcut]);

    private const int CloudQualities = (int)CloudQuality.High + 1;

    private static readonly int[] FrameLimits = [0, 30, 60, 90, 120, 144, 165, 240, 360];
    private static readonly int[] ShadowSizes = [1024, 2048, 4096, 8192];
    private static readonly int WindowModes = Enum.GetValues<WindowMode>().Length;
    private static readonly int AntiAliasingModes = Enum.GetValues<AntiAliasingMode>().Length;
    private static readonly GraphicsQuality[] Presets = [GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High, GraphicsQuality.Ultra];
    private static readonly int[] ShadowUpdateRates = [30, 60, 120, 0];
    private readonly PresentationSettings _settings;
    private readonly string[] _labels = new string[FirstSpecOption + LootJobs.Length];
    private readonly string[] _shortcutNames = new string[Shortcuts.Length];
    private ClientSettings _labelSettings;
    private bool _labelTranslation;

    // The server translates the chat, so its row shows (GameplayHud sets it from the connection).
    public bool ChatTranslationAvailable { get; set; }

    public HudSettingsPanel(PresentationSettings settings) => _settings = settings;

    private ClientSettings _draft;
    private int _tab;
    private int _firstRow;
    private const int VisibleRows = 24;
    private int _binding = -1;
    private string _lootNameDraft;
    public bool EditingLootNames => _lootNameDraft is not null;
    public bool CapturingKey => _binding >= 0;

    public void CancelLootNames() => _lootNameDraft = null;

    private void ApplyLootNames()
    {
        if (_lootNameDraft is not { } names)
        {
            return;
        }

        _draft = (_draft ?? _settings.Requested) with
        {
            AlwaysShowGroundNames = names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
        _lootNameDraft = null;
        _labelSettings = null;
    }

    public void TypeLootNames(char[] input, bool backspace, bool enter)
    {
        if (_lootNameDraft is not { } names)
        {
            return;
        }

        if (backspace && names.Length > 0)
        {
            names = names[..^1];
        }

        foreach (var character in input)
        {
            if (names.Length < 240 && !char.IsControl(character) && !char.IsSurrogate(character))
            {
                names += character;
            }
        }

        _lootNameDraft = names;
        if (enter)
        {
            ApplyLootNames();
        }
    }
    public void CancelKeyCapture() => _binding = -1;
    private static Rectangle RestoreKeys => new(492, 488, 190, 25);

    public static Rectangle Box => new(92, 15, 616, 570);
    public static Rectangle OkButton => new(590, 539, 96, 32);
    public static Rectangle CancelButton => new(495, 539, 87, 32);
    public static Rectangle CloseButton => new(671, 22, 24, 24);

    public void Open()
    {
        CancelLootNames();
        _draft = _settings.Requested;
        _labelSettings = null;
        _tab = 0;
        _firstRow = 0;
        _binding = -1;
    }

    public void Save()
    {
        ApplyLootNames();
        if (_draft is { } draft)
        {
            _settings.Requested = draft;
        }
    }

    private static Rectangle Tab(int index) => new(107 + index * 148, 65, 142, 43);
    private Rectangle Row(int index)
    {
        var y = 153 + index / 2 * (RowHeight + 5);
        if (_tab == 1 && index >= 6)
        {
            y += 13;
        }

        return new Rectangle(119 + index % 2 * 292, y, 264, RowHeight);
    }

    public void Scroll(int wheel)
    {
        if (wheel != 0)
        {
            var max = Math.Max(0, ((_tab == 2 ? ShortcutRows.Length : Tabs[_tab].Length) - VisibleRows + 1) / 2) * 2;
            _firstRow = Math.Clamp(_firstRow - Math.Sign(wheel) * 2, 0, max);
        }
    }

    private const int KeyMask = 0xFFFF;
    private const int ShiftMask = 1 << 16;
    private const int AltMask = 1 << 17;

    public Keys KeyFor(int index) => KeyOf(_settings.Requested, index);
    public string ShortcutLabel(int index) => BindingLabel(BindingOf(_settings.Requested, index));
    public bool DefaultInventoryKey => _settings.Requested.ShortcutKeys is not { Length: > 0 };

    private static bool SupportsModifiers(int index) => index < 10 || (index >= FirstSkillShortcut && index < MinimapShortcut) || index == MinimapShortcut
        || index == NewsShortcut || (index >= FirstActionBarShortcut && index < VoiceShortcut) || index == DungeonCameraShortcut;

    private static int DefaultBinding(int index) => (int)Shortcuts[index].Key | (index == DungeonCameraShortcut ? ShiftMask : 0);

    internal static ClientSettings WithDungeonCameraSwitched(ClientSettings settings) => settings with
    {
        DungeonCamera = settings.DungeonCamera == DungeonCameraMode.TopDown ? DungeonCameraMode.Free : DungeonCameraMode.TopDown
    };

    public bool ShortcutPressed(KeyboardState keyboard, KeyboardState previous, int index)
    {
        var binding = BindingOf(_settings.Requested, index);
        var key = (Keys)(binding & KeyMask);
        return keyboard.IsKeyDown(key) && !previous.IsKeyDown(key) &&
            (!SupportsModifiers(index) || (binding & ~KeyMask) == Modifiers(keyboard));
    }

    private static int Modifiers(KeyboardState keyboard) =>
        (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? ShiftMask : 0) |
        (keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt) ? AltMask : 0);

    private static string BindingLabel(int binding, string keyLabel = null) =>
        ((binding & ShiftMask) != 0 ? "Shift + " : "") +
        ((binding & AltMask) != 0 ? "Alt + " : "") + (keyLabel ?? ((Keys)(binding & KeyMask)).ToString());

    internal static Keys KeyOf(ClientSettings settings, int index) => (Keys)(BindingOf(settings, index) & KeyMask);

    private static int BindingOf(ClientSettings settings, int index)
    {
        if (settings.ShortcutKeys is { } bindings && index < bindings.Length && ValidKey((Keys)(bindings[index] & KeyMask)))
        {
            return bindings[index] & (SupportsModifiers(index) ? KeyMask | ShiftMask | AltMask : KeyMask);
        }

        return DefaultBinding(index);
    }

    private static bool ValidKey(Keys key) => key is Keys.V or Keys.C or Keys.S or Keys.Q or Keys.D or Keys.X or Keys.R
        or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown
        or (>= Keys.D0 and <= Keys.D9) or Keys.OemMinus or Keys.G or Keys.A or Keys.Tab or Keys.H or Keys.E or Keys.W or Keys.D1 or Keys.D2 or Keys.D3
        or (>= Keys.F1 and <= Keys.F8) or Keys.F10 or Keys.F11 or Keys.F12 or Keys.Back or Keys.Z or Keys.K or Keys.OemOpenBrackets or Keys.OemCloseBrackets or Keys.OemSemicolon;

    public void CaptureKey(KeyboardState keyboard, KeyboardState previous)
    {
        foreach (var key in keyboard.GetPressedKeys())
        {
            if (previous.IsKeyDown(key))
            {
                continue;
            }

            if (key == Keys.Escape)
            {
                _binding = -1;
                return;
            }

            if (!ValidKey(key))
            {
                continue;
            }

            var current = _draft ?? _settings.Requested;
            if (Rebind(current, _binding, key, Modifiers(keyboard)) is { } rebound)
            {
                _draft = rebound;
                _binding = -1;
            }

            return;
        }
    }

    private enum ShortcutGroup { Windows, Interface, Character, Voice, Skills, Potions, Camera }

    // Push-to-talk is shown as unavailable on the page; the browser has no voice chat.
    internal static bool OnPage(int slot) => slot >= 0 && slot < Shortcuts.Length && slot != VoiceShortcut;

    internal static IReadOnlyList<(int Slot, string Group)> PageShortcutRows { get; } = ShortcutRows
        .Where(static slot => slot >= 0)
        .Select(static slot => (Slot: slot, Group: GroupOfShortcut(slot)))
        .OrderBy(static row => row.Group)
        .Select(static row => (row.Slot, row.Group.ToString().ToLowerInvariant()))
        .ToArray();

    private static ShortcutGroup GroupOfShortcut(int slot) => slot switch
    {
        MinimapShortcut or GroundLabelsShortcut or HideHudShortcut or PersistentInfoShortcut => ShortcutGroup.Interface,
        6 or 8 or 9 or PvpShortcut => ShortcutGroup.Character,
        VoiceShortcut => ShortcutGroup.Voice,
        (>= FirstSkillShortcut and < MinimapShortcut) or (>= FirstActionBarShortcut and < VoiceShortcut) => ShortcutGroup.Skills,
        >= 10 and < FirstSkillShortcut => ShortcutGroup.Potions,
        (>= FirstCameraShortcut and < FirstActionBarShortcut) or DungeonCameraShortcut => ShortcutGroup.Camera,
        _ => ShortcutGroup.Windows
    };

    internal static string ShortcutName(int index, string language) => NumberedShortcut(index, ClientText.Get(Shortcuts[index].Name, language));

    private static string NumberedShortcut(int index, string phrase) =>
        Shortcuts[index].Number == 0 ? phrase : string.Format(CultureInfo.InvariantCulture, phrase, Shortcuts[index].Number);

    internal static string PageShortcutLabel(ClientSettings settings, int index) => PageBindingLabel(BindingOf(settings, index));

    internal static string PageShortcutDefaultLabel(int index) => PageBindingLabel(DefaultBinding(index));

    private static string PageBindingLabel(int binding)
    {
        var key = (Keys)(binding & KeyMask);
        var label = key switch
        {
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            Keys.OemMinus => "-",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemSemicolon => ";",
            Keys.Left => "←",
            Keys.Right => "→",
            Keys.Up => "↑",
            Keys.Down => "↓",
            Keys.PageUp => "Page Up",
            Keys.PageDown => "Page Down",
            Keys.Back => "Backspace",
            _ => key.ToString()
        };
        return BindingLabel(binding, label);
    }

    internal static ClientSettings ResetShortcut(ClientSettings settings, int slot) =>
        slot >= 0 && slot < Shortcuts.Length ? Rebind(settings, slot, Shortcuts[slot].Key, DefaultBinding(slot) & ~KeyMask) : null;

    internal static ClientSettings Rebind(ClientSettings current, int slot, Keys key, bool shift, bool alt) =>
        Rebind(current, slot, key, (shift ? ShiftMask : 0) | (alt ? AltMask : 0));

    // Null when the key cannot take the shortcut; a key another shortcut holds swaps with this one's.
    private static ClientSettings Rebind(ClientSettings current, int slot, Keys key, int modifiers)
    {
        if (slot < 0 || slot >= Shortcuts.Length || !ValidKey(key))
        {
            return null;
        }

        var bindings = Enumerable.Range(0, Shortcuts.Length).Select(index => BindingOf(current, index)).ToArray();
        var binding = (int)key | (SupportsModifiers(slot) ? modifiers : 0);
        var duplicate = Array.IndexOf(bindings, binding);
        if (duplicate >= 0 && !SupportsModifiers(duplicate) && (bindings[slot] & ~KeyMask) != 0)
        {
            return null;
        }

        if (duplicate >= 0)
        {
            bindings[duplicate] = bindings[slot];
        }

        bindings[slot] = binding;
        return current with { ShortcutKeys = bindings };
    }

    public void Click(Point pointer, bool backwards)
    {
        ApplyLootNames();
        if (_settings.AwaitingConfirmation)
        {
            return;
        }

        var current = _draft ?? _settings.Requested;
        for (var tab = 0; tab < Tabs.Length; tab++)
        {
            if (Tab(tab).Contains(pointer))
            {
                _tab = tab;
                _firstRow = 0;
                _binding = -1;
                return;
            }
        }

        if (_tab == 2)
        {
            if (RestoreKeys.Contains(pointer))
            {
                _draft = current with { ShortcutKeys = [] };
                _binding = -1;
                return;
            }

            for (var index = 0; index < Math.Min(VisibleRows, ShortcutRows.Length - _firstRow); index++)
            {
                if (Row(index).Contains(pointer))
                {
                    var slot = ShortcutRows[index + _firstRow];
                    if (slot < 0)
                    {
                        return;
                    }
                    if (backwards)
                    {
                        if (ResetShortcut(current, slot) is { } reset)
                        {
                            _draft = reset;
                            _binding = -1;
                        }
                    }
                    else
                    {
                        _binding = slot;
                    }
                    return;
                }
            }

            return;
        }

        var rows = Tabs[_tab];
        for (var index = 0; index < Math.Min(VisibleRows, rows.Length - _firstRow); index++)
        {
            if (!Row(index).Contains(pointer))
            {
                continue;
            }

            var option = rows[index + _firstRow];
            if (option < 0)
            {
                continue;
            }
            if (option == 196)
            {
                _lootNameDraft = string.Join(", ", current.AlwaysShowGroundNames ?? []);
                return;
            }

            var direction = backwards ? -1 : 1;
            if (option is 100 or 101 or 102)
            {
                var track = VolumeTrack(Row(index));
                var volume = Math.Clamp((pointer.X - track.Left) * 100 / track.Width, 0, 100);
                _draft = option switch
                {
                    100 => current with { MusicVolumePercent = volume },
                    101 => current with { SoundVolumePercent = volume },
                    _ when DesktopVoice.Available => current with { VoiceVolumePercent = volume },
                    _ => current
                };
                return;
            }

            if (option is >= 103 and <= 114)
            {
                if (!DesktopVoice.Available)
                {
                    return;
                }

                if (option == 109)
                {
                    _binding = VoiceShortcut;
                    return;
                }

                _draft = option switch
                {
                    103 => current with { VoiceEnabled = !current.VoiceEnabled },
                    >= 104 and <= 107 => current with { VoiceReceiveMask = current.VoiceReceiveMask ^ (1 << (option - 104)) },
                    108 => current with { VoiceTransmitChannel = (Math.Clamp(current.VoiceTransmitChannel, 0, 3) + direction + 4) % 4 },
                    112 => current with { VoiceMicrophone = NextMicrophone(current.VoiceMicrophone, direction) },
                    _ => current
                };
                return;
            }

            _draft = option switch
            {
                0 => current with { WindowMode = (WindowMode)(((int)current.WindowMode + direction + WindowModes) % WindowModes) },
                1 => current with { Monitor = ((current.Monitor + 1 + direction + _settings.MonitorCount + 1) % (_settings.MonitorCount + 1)) - 1 },
                2 => NextResolution(current, direction),
                3 => current with { RefreshRate = NextFrequency(current, direction) },
                4 => current with { VerticalSync = !current.VerticalSync },
                5 => current with { MusicEnabled = !current.MusicEnabled },
                6 => current with { SoundEnabled = !current.SoundEnabled },
                7 => current with { MaxFps = Cycle(FrameLimits, current.MaxFps, direction) },
                8 => current with { BackgroundMaxFps = Cycle(FrameLimits, current.BackgroundMaxFps, direction) },
                9 => current with { Quality = GraphicsQuality.Custom, AntiAliasing = (AntiAliasingMode)(((int)current.AntiAliasing + direction + AntiAliasingModes) % AntiAliasingModes) },
                10 => current with { Quality = GraphicsQuality.Custom, ShadowMapSize = Cycle(ShadowSizes, current.ShadowMapSize, direction) },
                11 => current.WithQuality(NextPreset(current.Quality, direction)),
                12 => current with { Quality = GraphicsQuality.Custom, ShadowUpdateRate = Cycle(ShadowUpdateRates, current.ShadowUpdateRate, direction) },
                13 => current with { Quality = GraphicsQuality.Custom, ShadowsEnabled = !current.ShadowsEnabled },
                14 => current with { ClassicTargetHealthBar = !current.ClassicTargetHealthBar },
                15 => current with { ClassicNamePlates = !current.ClassicNamePlates },
                16 => current with { WeatherEnabled = !current.WeatherEnabled },
                17 => current with { RealisticSky = !current.RealisticSky },
                18 => current with { Quality = GraphicsQuality.Custom, CloudQuality = (CloudQuality)(((int)current.CloudQuality + direction + CloudQualities) % CloudQualities) },
                19 => current with { Language = NextLanguage(current.Language, direction) },
                20 => current with { LevelGuide = !current.LevelGuide },
                21 => current with { Quality = GraphicsQuality.Custom, ScreenSpaceReflections = !current.ScreenSpaceReflections },
                22 => current with { DistanceFog = current.DistanceFog == DistanceFogMode.Off ? DistanceFogMode.Atmospheric : DistanceFogMode.Off },
                23 => current with { Quality = GraphicsQuality.Custom, VolumetricLight = !current.VolumetricLight },
                24 => current with { Quality = GraphicsQuality.Custom, ColorGrading = !current.ColorGrading },
                25 => current with { Quality = GraphicsQuality.Custom, AutoExposure = !current.AutoExposure },
                26 => current with { Quality = GraphicsQuality.Custom, Bloom = current.Bloom == BloomQuality.Off ? BloomQuality.High : BloomQuality.Off },
                27 => current with { HideGroundGold = !current.HideGroundGold },
                28 => current with { HideGroundPotions = !current.HideGroundPotions },
                29 => current with { HideGroundWeapons = !current.HideGroundWeapons },
                30 => current with { HideTradeChat = !current.HideTradeChat },
                31 => current with { HideCastleNotices = !current.HideCastleNotices },
                32 => current with { HideOnlineCount = !current.HideOnlineCount },
                33 => current with { DisableGroundLootFilter = !current.DisableGroundLootFilter },
                190 => current with { GroundMinimumEquipmentLevel = Cycle(EquipmentLevels,
                    current.GroundMinimumEquipmentLevel, direction) },
                191 => current with { GroundMaximumEquipmentLevel = Cycle(EquipmentLevels,
                    current.GroundMaximumEquipmentLevel, direction) },
                192 => current with { AlwaysShowRareGroundEquipment = !current.AlwaysShowRareGroundEquipment },
                193 => current with { HideGroundEquipmentWithoutSpec = !current.HideGroundEquipmentWithoutSpec },
                194 => current with
                {
                    DisableGroundLootFilter = false,
                    HideGroundGold = false,
                    HideGroundPotions = false,
                    HideGroundWeapons = false,
                    HiddenGroundCategories = 0,
                    HiddenGroundSpecJobs = 0,
                    HideGroundEquipmentWithoutSpec = false,
                    GroundMinimumEquipmentLevel = 0,
                    GroundMaximumEquipmentLevel = 0
                },
                195 => current with { HideQuestTracker = !current.HideQuestTracker },
                197 => current with { CameraMouseButtons = (Math.Clamp(current.CameraMouseButtons, 0, 2) + direction + 3) % 3 },
                198 => current with { HideBellatraBossNotices = !current.HideBellatraBossNotices },
                199 => current with { HideBellatraTeamNotices = !current.HideBellatraTeamNotices },
                200 => current with { CameraMouse4 = (CameraMouseAction)Cycle(MouseActions, (int)current.CameraMouse4, direction) },
                201 => current with { CameraMouse5 = (CameraMouseAction)Cycle(MouseActions, (int)current.CameraMouse5, direction) },
                202 => current with { HideBellatraResults = !current.HideBellatraResults },
                203 => current with { HideClanMembershipNotices = !current.HideClanMembershipNotices },
                204 => current with { ShowExperienceNotices = !current.ShowExperienceNotices },
                205 => current with { HideEventRewardNotices = !current.HideEventRewardNotices },
                206 => current with { ShowPremiumBuffNotices = !current.ShowPremiumBuffNotices },
                207 => current with { HideCalendarNotices = !current.HideCalendarNotices },
                208 => current with { HideGlobalBuffNotices = !current.HideGlobalBuffNotices },
                209 => current with { HideUnfocusedNames = !current.HideUnfocusedNames },
                210 => current with { HideDamageNumbers = !current.HideDamageNumbers },
                211 => current with { AlwaysShowGroundLabels = !current.AlwaysShowGroundLabels },
                212 => current with { InvertCameraOrbit = !current.InvertCameraOrbit },
                213 => current with { InvertCameraScroll = !current.InvertCameraScroll },
                DungeonCameraOption => WithDungeonCameraSwitched(current),
                MoveChatOption => current with { MoveChat = !current.MoveChat },
                TutorialTipsOption => current with { TutorialTips = !current.TutorialTips },
                ResetInterfaceOption => current.WithInterfaceReset(),
                ChatTranslateOption when ChatTranslationAvailable => current with { ChatTranslateTo = NextChatTranslation(current.ChatTranslateTo, direction) },
                UiScaleOption => current with { UiScale = NextUiScale(current.UiScale, direction) },
                >= FirstSpecOption and var spec => current with { HiddenGroundSpecJobs =
                    current.HiddenGroundSpecJobs ^ (uint)LootJobs[spec - FirstSpecOption].Job },
                >= 34 and var category => current with { HiddenGroundCategories = current.HiddenGroundCategories ^ (1UL << (category - 34)) },
                _ => current
            };
            return;
        }
    }

    // The next HUD scale of the list; a scale between two steps moves to the nearer one first.
    internal static float NextUiScale(float current, int direction)
    {
        var index = 0;
        for (var at = 1; at < UiScales.Length; at++)
        {
            if (MathF.Abs(UiScales[at] - current) < MathF.Abs(UiScales[index] - current))
            {
                index = at;
            }
        }

        return UiScales[(index + direction + UiScales.Length) % UiScales.Length];
    }

    private static string NextMicrophone(string current, int direction)
    {
        var devices = new[] { "" }.Concat(Microphone.All.Select(device => device.Name)).Distinct().ToArray();
        var index = Math.Max(0, Array.IndexOf(devices, current));
        return devices[(index + direction + devices.Length) % devices.Length];
    }

    private static Rectangle VolumeTrack(Rectangle row) => new(row.X + 8, row.Y + 9, row.Width - 60, 4);

    private ClientSettings NextResolution(ClientSettings current, int direction)
    {
        var sizes = Resolutions(current.WindowMode, _settings.DisplayModes, _settings.Desktop);
        if (sizes.Count == 0)
        {
            return current;
        }

        var index = sizes.IndexOf(new Point(current.WindowWidth, current.WindowHeight));
        var next = index < 0 && direction < 0 ? sizes[^1] : sizes[(index + direction + sizes.Count) % sizes.Count];
        return current with { WindowWidth = next.X, WindowHeight = next.Y };
    }

    // The sizes a mode offers, smallest first, all from the modes the monitor reports: fullscreen any of them; a window
    // those that fit on the desktop below its title bar; borderless none, since it always covers the desktop.
    internal static List<Point> Resolutions(WindowMode mode, IReadOnlyList<DisplayModeOption> modes, DisplayModeOption desktop)
    {
        var sizes = new List<Point>();
        if (mode == WindowMode.Borderless)
        {
            return sizes;
        }

        foreach (var option in modes)
        {
            var size = new Point(option.Width, option.Height);
            var fits = mode == WindowMode.Fullscreen || option.Width <= desktop.Width && option.Height <= desktop.Height - WindowPlacement.TitleBar;
            if (fits && !sizes.Contains(size))
            {
                sizes.Add(size);
            }
        }

        sizes.Sort(static (left, right) => ((long)left.X * left.Y).CompareTo((long)right.X * right.Y));
        return sizes;
    }

    private int NextFrequency(ClientSettings current, int direction)
    {
        if (current.WindowMode != WindowMode.Fullscreen)
        {
            return 0;
        }

        var frequencies = new List<int> { 0 };
        foreach (var mode in _settings.DisplayModes)
        {
            if ((current.Monitor == -1 || mode.Monitor == current.Monitor)
                && mode.Width == current.WindowWidth && mode.Height == current.WindowHeight && !frequencies.Contains(mode.RefreshRate))
            {
                frequencies.Add(mode.RefreshRate);
            }
        }

        frequencies.Sort();
        return Cycle(frequencies, current.RefreshRate, direction);
    }

    private static GraphicsQuality NextPreset(GraphicsQuality quality, int direction)
    {
        var index = Array.IndexOf(Presets, quality);
        if (index < 0)
        {
            return direction > 0 ? Presets[0] : Presets[^1];
        }

        return Presets[(index + direction + Presets.Length) % Presets.Length];
    }

    private static int Cycle(IReadOnlyList<int> values, int value, int direction)
    {
        var index = 0;
        for (var candidate = 0; candidate < values.Count; candidate++)
        {
            if (values[candidate] == value)
            {
                index = candidate;
                break;
            }
        }

        return values[(index + direction + values.Count) % values.Count];
    }

    private void RefreshLabels()
    {
        var settings = _draft ?? _settings.Requested;
        if (_labelSettings == settings && _labelTranslation == ChatTranslationAvailable)
        {
            return;
        }

        _labelSettings = settings;
        _labelTranslation = ChatTranslationAvailable;
        _labels[0] = ClientText.Format("Settings.Window", ClientText.Get("Settings.WindowMode." + settings.WindowMode));
        _labels[1] = settings.Monitor < 0 ? ClientText.Get("Settings.MonitorCurrent") : ClientText.Format("Settings.Monitor", settings.Monitor);
        _labels[2] = settings.WindowMode == WindowMode.Borderless
            ? ClientText.Get("Settings.ResolutionDesktop")
            : ClientText.Format("Settings.Resolution", settings.WindowWidth, settings.WindowHeight);
        _labels[3] = settings.WindowMode != WindowMode.Fullscreen ? ClientText.Get("Settings.FrequencyDesktop") : ClientText.Format("Settings.Frequency", settings.RefreshRate);
        _labels[4] = ClientText.Get(settings.VerticalSync ? "Settings.VSyncOn" : "Settings.VSyncOff");
        _labels[5] = ClientText.Get(settings.MusicEnabled ? "Settings.MusicOn" : "Settings.MusicOff");
        _labels[6] = ClientText.Get(settings.SoundEnabled ? "Settings.SoundOn" : "Settings.SoundOff");
        _labels[7] = settings.MaxFps == 0 ? ClientText.Get("Settings.MaxFpsUnlimited") : ClientText.Format("Settings.MaxFps", settings.MaxFps);
        _labels[8] = settings.BackgroundMaxFps == 0
            ? ClientText.Get("Settings.BackgroundFpsForeground")
            : ClientText.Format("Settings.BackgroundFps", settings.BackgroundMaxFps);
        _labels[9] = settings.AntiAliasing == AntiAliasingMode.Msaa
            ? ClientText.Format("Settings.AntiAliasingMsaa", Math.Max(settings.MultiSampleCount, 2))
            : settings.AntiAliasing == AntiAliasingMode.Off
                ? ClientText.Get("Settings.AntiAliasingOff")
                : ClientText.Format("Settings.AntiAliasing", settings.AntiAliasing.ToString().ToUpperInvariant());
        _labels[10] = ClientText.Format("Settings.SunShadow", settings.ShadowMapSize);
        _labels[11] = ClientText.Format("Settings.Quality", ClientText.Get("Settings.Quality." + settings.Quality));
        _labels[12] = settings.ShadowUpdateRate == 0
            ? ClientText.Get("Settings.ShadowAnimationEveryFrame")
            : ClientText.Format("Settings.ShadowAnimation", settings.ShadowUpdateRate);
        _labels[13] = ClientText.Get(settings.ShadowsEnabled ? "Settings.ShadowsOn" : "Settings.ShadowsOff");
        _labels[14] = ClientText.Get(settings.ClassicTargetHealthBar ? "Settings.TargetHealthClassic" : "Settings.TargetHealthAlways");
        _labels[15] = ClientText.Get(settings.ClassicNamePlates ? "Settings.NamePlatesClassic" : "Settings.NamePlatesCompact");
        _labels[16] = ClientText.Get(settings.WeatherEnabled ? "Settings.WeatherOn" : "Settings.WeatherOff");
        _labels[17] = ClientText.Get(settings.RealisticSky ? "Settings.SkyRealistic" : "Settings.SkyClassic");
        _labels[18] = ClientText.Format("Settings.Clouds", ClientText.Get("Settings.Clouds." + settings.CloudQuality));
        _labels[19] = ClientText.Format("Settings.Language", ClientText.Get("Language." + settings.Language));
        _labels[20] = Toggle("Settings.Row.LevelGuide", settings.LevelGuide);
        _labels[21] = Toggle("Settings.Row.ScreenReflections", settings.ScreenSpaceReflections);
        _labels[22] = Toggle("Settings.Row.Fog", settings.DistanceFog != DistanceFogMode.Off);
        _labels[23] = Toggle("Settings.Row.VolumetricLight", settings.VolumetricLight);
        _labels[24] = Toggle("Settings.Row.ColorGrading", settings.ColorGrading);
        _labels[25] = Toggle("Settings.Row.AutoExposure", settings.AutoExposure);
        _labels[26] = Toggle("Settings.Row.Bloom", settings.Bloom != BloomQuality.Off);
        _labels[27] = Toggle("Settings.Row.GroundGold", !settings.HideGroundGold);
        _labels[28] = Toggle("Settings.Row.GroundPotions", !settings.HideGroundPotions);
        _labels[29] = Toggle("Settings.Row.GroundWeapons", !settings.HideGroundWeapons);
        _labels[30] = Toggle("Settings.Row.TradeChat", !settings.HideTradeChat);
        _labels[31] = Toggle("Settings.Row.CastleNotices", !settings.HideCastleNotices);
        _labels[32] = Toggle("Settings.Row.OnlinePlayers", !settings.HideOnlineCount);
        _labels[33] = Toggle("Settings.Row.UseLootFilter", !settings.DisableGroundLootFilter);
        _labels[190] = LevelLabel("Settings.Row.MinimumLevel", settings.GroundMinimumEquipmentLevel);
        _labels[191] = LevelLabel("Settings.Row.MaximumLevel", settings.GroundMaximumEquipmentLevel);
        _labels[192] = Toggle("Settings.Row.RareEquipment", settings.AlwaysShowRareGroundEquipment);
        _labels[194] = ClientText.Get("Settings.Row.ShowAll");
        var alwaysShow = string.Join(", ", settings.AlwaysShowGroundNames ?? []);
        _labels[103] = Toggle("Settings.VoiceChat", settings.VoiceEnabled);
        for (var channel = 0; channel < VoiceChannels.Length; channel++)
        {
            _labels[104 + channel] = Toggle(VoiceChannels[channel], (settings.VoiceReceiveMask & (1 << channel)) != 0);
        }
        _labels[108] = ClientText.Format("Settings.Row.SpeakTo", ClientText.Get(VoiceChannels[Math.Clamp(settings.VoiceTransmitChannel, 0, 3)]));
        _labels[109] = ClientText.Format("Settings.Row.PushToTalkKey", BindingLabel(BindingOf(settings, VoiceShortcut)));
        _labels[110] = ClientText.Get("Settings.VoiceChat");
        _labels[111] = ClientText.Get("Settings.Microphone");
        _labels[112] = ClientText.Format("Settings.Row.Device",
            string.IsNullOrEmpty(settings.VoiceMicrophone) ? ClientText.Get("Settings.Row.SystemDefault") : settings.VoiceMicrophone);
        _labels[113] = ClientText.Get("Settings.Row.PushToTalkMode");
        _labels[196] = ClientText.Format("Settings.Row.AlwaysShow", alwaysShow);
        var mouseButtons = settings.CameraMouseButtons switch
        {
            1 => "Settings.MouseButtons.Swapped",
            2 => "Settings.MouseButtons.Off",
            _ => "Settings.MouseButtons.Default"
        };
        _labels[197] = ClientText.Format("Settings.Row.CameraSideButtons", ClientText.Get(mouseButtons));
        _labels[198] = Toggle("Settings.Row.BellatraBoss", !settings.HideBellatraBossNotices);
        _labels[203] = Toggle("Settings.Row.ClanMembership", !settings.HideClanMembershipNotices);
        _labels[205] = Toggle("Settings.Row.EventRewards", !settings.HideEventRewardNotices);
        _labels[206] = Toggle("Settings.Row.PremiumBuffs", settings.ShowPremiumBuffNotices);
        _labels[207] = Toggle("Settings.Row.EventCalendar", !settings.HideCalendarNotices);
        _labels[209] = Toggle("Settings.Row.CharacterNames", !settings.HideUnfocusedNames);
        _labels[210] = Toggle("Settings.Row.Damage", !settings.HideDamageNumbers);
        _labels[211] = Toggle("Settings.Row.GroundItemNames", settings.AlwaysShowGroundLabels);
        _labels[212] = Toggle("Settings.Row.InvertCameraRotation", settings.InvertCameraOrbit);
        _labels[213] = Toggle("Settings.Row.InvertCameraScroll", settings.InvertCameraScroll);
        _labels[DungeonCameraOption] = ClientText.Get(settings.DungeonCamera == DungeonCameraMode.TopDown
            ? "Settings.Row.DungeonCameraTopDown"
            : "Settings.Row.DungeonCameraNormal");
        _labels[UiScaleOption] = ClientText.Format("Settings.Row.HudScale", Math.Round(settings.UiScale * 100));
        _labels[MoveChatOption] = Toggle("Settings.Row.MoveChat", settings.MoveChat);
        _labels[TutorialTipsOption] = Toggle("Settings.Row.TutorialTips", settings.TutorialTips);
        _labels[ResetInterfaceOption] = ClientText.Get("Settings.Row.ResetInterface");
        _labels[ChatTranslateOption] = ClientText.Format("Settings.Row.ChatTranslateTo", settings.ChatTranslateTo switch
        {
            "" => ClientText.Get("Settings.ChatTranslate.Off"),
            ClientSettings.GameLanguageTranslation => ClientText.Get("Settings.ChatTranslate.Game"),
            var language => ClientText.Get("Language." + language)
        });
        _labels[208] = Toggle("Settings.Row.GlobalBonuses", !settings.HideGlobalBuffNotices);
        _labels[204] = Toggle("Settings.Row.ExpGained", settings.ShowExperienceNotices);
        _labels[202] = Toggle("Settings.Row.BellatraResults", !settings.HideBellatraResults);
        _labels[199] = Toggle("Settings.Row.BellatraSeals", !settings.HideBellatraTeamNotices);
        _labels[200] = "Mouse 4: " + MouseActionLabel(settings.CameraMouse4);
        _labels[201] = "Mouse 5: " + MouseActionLabel(settings.CameraMouse5);

        static string MouseActionLabel(CameraMouseAction action) => ClientText.Get(action switch
        {
            CameraMouseAction.Disabled => "Settings.MouseAction.Off",
            CameraMouseAction.RotateLeft => "Settings.MouseAction.RotateLeft",
            CameraMouseAction.RotateRight => "Settings.MouseAction.RotateRight",
            CameraMouseAction.ZoomIn => "Settings.MouseAction.ZoomIn",
            CameraMouseAction.ZoomOut => "Settings.MouseAction.ZoomOut",
            CameraMouseAction.TiltUp => "Settings.MouseAction.TiltUp",
            CameraMouseAction.TiltDown => "Settings.MouseAction.TiltDown",
            _ => "Settings.MouseButtons.Default"
        });

        _labels[195] = Toggle("Settings.Row.QuestTracker", !settings.HideQuestTracker);
        _labels[193] = Toggle("Settings.Row.WithoutSpecialization", !settings.HideGroundEquipmentWithoutSpec);
        for (var index = 0; index < LootJobs.Length; index++)
        {
            var job = LootJobs[index];
            _labels[FirstSpecOption + index] = Toggle(job.Label, (settings.HiddenGroundSpecJobs & (uint)job.Job) == 0);
        }

        static string LevelLabel(string key, int level) => ClientText.Format(key, level > 0 ? level.ToString(CultureInfo.InvariantCulture) : ClientText.Get("Settings.Row.Unlimited"));

        for (var i = 0; i < GroundLootFilter.Labels.Length; i++)
        {
            _labels[34 + i] = Toggle(GroundLootFilter.Labels[i], !GroundLootFilter.Hidden(settings, i));
        }

        for (var index = 0; index < Shortcuts.Length; index++)
        {
            _shortcutNames[index] = NumberedShortcut(index, ClientText.Get(Shortcuts[index].Name));
        }

        // A switch row: the drawing reads the phrase's value after the last ": " (or full-width colon) as on or off.
        static string Toggle(string key, bool enabled) => ClientText.Format(enabled ? "Settings.Toggle.On" : "Settings.Toggle.Off", ClientText.Get(key));
    }

    // Off, the game's language, then each supported language.
    internal static string NextChatTranslation(string current, int direction)
    {
        var choices = ChatTranslationChoices;
        var index = Math.Max(0, Array.IndexOf(choices, current));
        return choices[(index + direction + choices.Length) % choices.Length];
    }

    private static readonly string[] ChatTranslationChoices = ["", ClientSettings.GameLanguageTranslation, .. ClientSettings.SupportedLanguages];

    private static string NextLanguage(string language, int direction)
    {
        var languages = ClientSettings.SupportedLanguages;
        var index = 0;
        for (var candidate = 0; candidate < languages.Count; candidate++)
        {
            if (string.Equals(languages[candidate], language, StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
                break;
            }
        }

        return languages[(index + direction + languages.Count) % languages.Count];
    }
}
