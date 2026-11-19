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
    private const int FirstSpecOption = 214;
    // The HUD scale's row, between the voice rows and the loot level rows.
    private const int UiScaleOption = 115;
    private static readonly float[] UiScales = [0.75f, 0.9f, 1f, 1.1f, 1.25f, 1.5f];
    private static readonly int[] MouseActions = Enum.GetValues<CameraMouseAction>().Select(action => (int)action).ToArray();
    private static readonly (JobBitCode Job, string Portuguese, string English)[] LootJobs =
    [
        (JobBitCode.Mechanician, "Mecânico", "Mechanician"), (JobBitCode.Fighter, "Lutador", "Fighter"),
        (JobBitCode.Pikeman, "Pike", "Pikeman"), (JobBitCode.Archer, "Arqueira", "Archer"),
        (JobBitCode.Knight, "Cavaleiro", "Knight"), (JobBitCode.Atalanta, "Atalanta", "Atalanta"),
        (JobBitCode.Priestess, "Sacerdotisa", "Priestess"), (JobBitCode.Magician, "Mago", "Magician"),
        (JobBitCode.Assassin, "Assassina", "Assassin"), (JobBitCode.MartialArtist, "Artista marcial", "Martial artist"),
        (JobBitCode.Shaman, "Xamã", "Shaman")
    ];
    private static readonly int[] EquipmentLevels = [.. Enumerable.Range(0, 256)];
    private const int RowHeight = 22;
    private static readonly int[][] Tabs =
    [
        Columns([11, -2, 9, 13, 10, 12, 21, -3, 24, 25, 26, 23, 22, -4, 0, 1, 2, 3, 4, 7, 8, UiScaleOption],
            [209, 210, 211, 17, 18]),
        [5, 6, 100, 101, 110, 111, 103, 108, 102, 109, 104, 112, 105, 113, 106, 114, 107, 114],
        [],
        Columns([19, -5, 14, 20, 15, 16, 32, 30, 31, 33, -6, 27, 28, 29,
            .. Enumerable.Range(34, GroundLootFilter.Labels.Length), 190, 191, 192, 193, 194, 196, .. Enumerable.Range(FirstSpecOption, LootJobs.Length)],
            [195, -7, 197, 200, 201, -8, 198, 199, 202, 203, 204, 205, 206, 207, 208, 212, 213])
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

    private static readonly string[] PortugueseTabs = ["Vídeo", "Áudio", "Teclas", "Outros"];
    private static readonly string[] EnglishTabs = ["Video", "Audio", "Keys", "Other"];
    public const int FirstSkillShortcut = 13;
    public const int MinimapShortcut = FirstSkillShortcut + HudSkillBindings.ShortcutCount;
    public const int NewsShortcut = MinimapShortcut + 1;
    public const int GroundLabelsShortcut = NewsShortcut + 1;
    private const int FirstCameraShortcut = GroundLabelsShortcut + 1;
    public const int FirstActionBarShortcut = FirstCameraShortcut + 6;
    public const int VoiceShortcut = FirstActionBarShortcut + HudSkillBindings.ShortcutCount;
    public const int HideHudShortcut = VoiceShortcut + 1;
    public const int PersistentInfoShortcut = HideHudShortcut + 1;
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
    private static readonly (string Portuguese, string English, Keys Key)[] Shortcuts =
    [
        ("Inventário", "Inventory", Keys.V), ("Personagem", "Character", Keys.C), ("Habilidades", "Skills", Keys.S),
        ("Missões", "Quests", Keys.Q), ("Social", "Social", Keys.D), ("Menu do sistema", "System menu", Keys.X),
        ("Correr / andar", "Run / walk", Keys.R), ("Configurações", "Settings", Keys.F10),
        ("Trocar bolsa (A/B)", "Switch bag (A/B)", Keys.E), ("Trocar conjunto de armas", "Switch weapon set", Keys.W),
        ("Poção do cinto 1", "Belt potion 1", Keys.D1), ("Poção do cinto 2", "Belt potion 2", Keys.D2),
        ("Poção do cinto 3", "Belt potion 3", Keys.D3),
        .. Enumerable.Range(0, HudSkillBindings.ShortcutCount)
            .Select(index => ($"Atalho de habilidade {index + 1}", $"Skill shortcut {index + 1}", Keys.F1 + index)),
        ("Minimapa", "Minimap", Keys.Tab), ("Novidades", "News", Keys.H), ("Nomes dos itens no chão", "Ground item names", Keys.A),
        ("Girar câmera à esquerda", "Rotate camera left", Keys.Left), ("Girar câmera à direita", "Rotate camera right", Keys.Right),
        ("Aproximar câmera", "Zoom in", Keys.Up), ("Afastar câmera", "Zoom out", Keys.Down),
        ("Inclinar câmera para cima", "Tilt camera up", Keys.PageUp), ("Inclinar câmera para baixo", "Tilt camera down", Keys.PageDown),
        .. ActionBarKeys.Select((key, index) => ($"Barra de ações: espaço {index + 1}", $"Action bar: slot {index + 1}", key)),
        ("Pressionar para falar", "Push to talk", Keys.G),
        ("Esconder todo o HUD (para prints)", "Hide the whole HUD (for screenshots)", Keys.F12),
        ("Informações fixas sobre os personagens", "Persistent character information", Keys.Back)
    ];

    private static readonly int[] ShortcutRows = Columns(
        [1, 0, 2, 3, 4, 5, 7, NewsShortcut, -9, MinimapShortcut, GroundLabelsShortcut, PersistentInfoShortcut, HideHudShortcut, -10, 6, 8, 9, -11,
            VoiceShortcut],
        [.. Enumerable.Range(FirstSkillShortcut, HudSkillBindings.ShortcutCount),
         .. Enumerable.Range(FirstActionBarShortcut, ActionBarKeys.Length), -12, 10, 11, 12,
         -7, .. Enumerable.Range(FirstCameraShortcut, 6)]);

    private const int CloudQualities = (int)CloudQuality.High + 1;

    private static readonly int[] FrameLimits = [0, 30, 60, 90, 120, 144, 165, 240, 360];
    private static readonly int[] ShadowSizes = [1024, 2048, 4096, 8192];
    private static readonly int WindowModes = Enum.GetValues<WindowMode>().Length;
    private static readonly int AntiAliasingModes = Enum.GetValues<AntiAliasingMode>().Length;
    private static readonly GraphicsQuality[] Presets = [GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High, GraphicsQuality.Ultra];
    private static readonly int[] ShadowUpdateRates = [30, 60, 120, 0];
    private readonly PresentationSettings _settings;
    private readonly string[] _labels = new string[FirstSpecOption + LootJobs.Length];
    private ClientSettings _labelSettings;

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

    private static bool SupportsModifiers(int index) => index < 10 || (index >= FirstSkillShortcut && index < MinimapShortcut) || index == MinimapShortcut || index == NewsShortcut || (index >= FirstActionBarShortcut && index < VoiceShortcut);

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

        return (int)Shortcuts[index].Key;
    }

    private static bool ValidKey(Keys key) => key is Keys.V or Keys.C or Keys.S or Keys.Q or Keys.D or Keys.X or Keys.R
        or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown
        or (>= Keys.D0 and <= Keys.D9) or Keys.OemMinus or Keys.G or Keys.A or Keys.Tab or Keys.H or Keys.E or Keys.W or Keys.D1 or Keys.D2 or Keys.D3
        or (>= Keys.F1 and <= Keys.F8) or Keys.F10 or Keys.F11 or Keys.F12 or Keys.Back or Keys.OemOpenBrackets or Keys.OemCloseBrackets or Keys.OemSemicolon;

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
        6 or 8 or 9 => ShortcutGroup.Character,
        VoiceShortcut => ShortcutGroup.Voice,
        (>= FirstSkillShortcut and < MinimapShortcut) or (>= FirstActionBarShortcut and < VoiceShortcut) => ShortcutGroup.Skills,
        >= 10 and < FirstSkillShortcut => ShortcutGroup.Potions,
        >= FirstCameraShortcut and < FirstActionBarShortcut => ShortcutGroup.Camera,
        _ => ShortcutGroup.Windows
    };

    internal static string ShortcutName(int index, bool portuguese) => portuguese ? Shortcuts[index].Portuguese : Shortcuts[index].English;

    internal static string PageShortcutLabel(ClientSettings settings, int index) => PageBindingLabel(BindingOf(settings, index));

    internal static string PageShortcutDefaultLabel(int index) => PageBindingLabel((int)Shortcuts[index].Key);

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
        slot >= 0 && slot < Shortcuts.Length ? Rebind(settings, slot, Shortcuts[slot].Key, 0) : null;

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
        if (_labelSettings == settings)
        {
            return;
        }

        _labelSettings = settings;
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
        _labels[20] = ClientText.Language == "pt-BR" ? $"Guia de nível: {(settings.LevelGuide ? "Ligado" : "Desligado")}" : $"Level guide: {(settings.LevelGuide ? "On" : "Off")}";
        var portuguese = ClientText.Language == "pt-BR";
        _labels[21] = Toggle(portuguese ? "Reflexos em tela" : "Screen reflections", settings.ScreenSpaceReflections);
        _labels[22] = Toggle(portuguese ? "Neblina" : "Fog", settings.DistanceFog != DistanceFogMode.Off);
        _labels[23] = Toggle(portuguese ? "Luz volumétrica" : "Volumetric light", settings.VolumetricLight);
        _labels[24] = Toggle(portuguese ? "Cores (LUT)" : "Color grading", settings.ColorGrading);
        _labels[25] = Toggle(portuguese ? "Exposição automática" : "Auto exposure", settings.AutoExposure);
        _labels[26] = Toggle("Bloom", settings.Bloom != BloomQuality.Off);
        _labels[27] = Toggle(portuguese ? "Mostrar ouro no chão" : "Show ground gold", !settings.HideGroundGold);
        _labels[28] = Toggle(portuguese ? "Mostrar poções no chão" : "Show ground potions", !settings.HideGroundPotions);
        _labels[29] = Toggle(portuguese ? "Mostrar armas no chão" : "Show ground weapons", !settings.HideGroundWeapons);
        _labels[30] = Toggle(portuguese ? "Chat de comércio" : "Trade chat", !settings.HideTradeChat);
        _labels[31] = Toggle(portuguese ? "Avisos do Castelo Bless" : "Bless Castle notices", !settings.HideCastleNotices);
        _labels[32] = Toggle(portuguese ? "Mostrar jogadores online" : "Show online players", !settings.HideOnlineCount);
        _labels[33] = Toggle(portuguese ? "Usar o filtro de loot" : "Use loot filter", !settings.DisableGroundLootFilter);
        _labels[190] = LevelLabel(portuguese ? "Nível mínimo" : "Minimum level", settings.GroundMinimumEquipmentLevel);
        _labels[191] = LevelLabel(portuguese ? "Nível máximo" : "Maximum level", settings.GroundMaximumEquipmentLevel);
        _labels[192] = Toggle(portuguese ? "Sempre mostrar equipamentos raros" : "Always show rare equipment", settings.AlwaysShowRareGroundEquipment);
        _labels[194] = portuguese ? "Mostrar tudo (filtro de loot)" : "Show all (loot filter)";
        var alwaysShow = string.Join(", ", settings.AlwaysShowGroundNames ?? []);
        var channels = portuguese ? new[] { "Perto", "Grupo", "Clã", "Todos no mapa" } : new[] { "Nearby", "Party", "Clan", "Everyone on the map" };
        _labels[103] = Toggle(portuguese ? "Chat de voz" : "Voice chat", settings.VoiceEnabled);
        for (var channel = 0; channel < channels.Length; channel++)
        {
            _labels[104 + channel] = Toggle(channels[channel], (settings.VoiceReceiveMask & (1 << channel)) != 0);
        }
        _labels[108] = (portuguese ? "Falar para: " : "Speak to: ") + channels[Math.Clamp(settings.VoiceTransmitChannel, 0, 3)];
        _labels[109] = (portuguese ? "Tecla de falar: " : "Push to talk: ") + BindingLabel(BindingOf(settings, VoiceShortcut));
        _labels[110] = portuguese ? "Chat de voz" : "Voice chat";
        _labels[111] = portuguese ? "Microfone" : "Microphone";
        _labels[112] = (portuguese ? "Dispositivo: " : "Device: ") +
            (string.IsNullOrEmpty(settings.VoiceMicrophone) ? (portuguese ? "Padrão do sistema" : "System default") : settings.VoiceMicrophone);
        _labels[113] = portuguese ? "Modo: Pressionar para falar" : "Mode: Push to talk";
        _labels[196] = (portuguese ? "Sempre mostrar: " : "Always show: ") + alwaysShow;
        var mouseButtons = settings.CameraMouseButtons switch
        {
            1 => portuguese ? "Invertidos" : "Swapped",
            2 => portuguese ? "Desligados" : "Off",
            _ => portuguese ? "Padrão" : "Default"
        };
        _labels[197] = (portuguese ? "Botões laterais da câmera: " : "Camera side buttons: ") + mouseButtons;
        _labels[198] = Toggle(portuguese ? "Bellatra: derrota do chefe" : "Bellatra boss defeat", !settings.HideBellatraBossNotices);
        _labels[203] = Toggle(portuguese ? "Clã: entradas e saídas" : "Clan: joins and departures", !settings.HideClanMembershipNotices);
        _labels[205] = Toggle(portuguese ? "Recompensas de evento" : "Event rewards", !settings.HideEventRewardNotices);
        _labels[206] = Toggle(portuguese ? "Ativação de buffs premium" : "Premium buff activation", settings.ShowPremiumBuffNotices);
        _labels[207] = Toggle(portuguese ? "Calendário de eventos" : "Event calendar notices", !settings.HideCalendarNotices);
        _labels[209] = Toggle(portuguese ? "Mostrar nomes dos personagens" : "Show character names", !settings.HideUnfocusedNames);
        _labels[210] = Toggle(portuguese ? "Mostrar dano" : "Show damage", !settings.HideDamageNumbers);
        _labels[211] = Toggle(portuguese ? "Nomes de todos os itens no chão" : "Always show ground item names", settings.AlwaysShowGroundLabels);
        _labels[212] = Toggle(portuguese ? "Inverter giro da câmera" : "Invert camera rotation", settings.InvertCameraOrbit);
        _labels[213] = Toggle(portuguese ? "Inverter rolagem da câmera" : "Invert camera scroll", settings.InvertCameraScroll);
        _labels[UiScaleOption] = (portuguese ? "Escala do HUD: " : "HUD scale: ") + Math.Round(settings.UiScale * 100) + "%";
        _labels[208] = Toggle(portuguese ? "Bônus globais" : "Global bonus notices", !settings.HideGlobalBuffNotices);
        _labels[204] = Toggle(portuguese ? "EXP ganha" : "EXP gained", settings.ShowExperienceNotices);
        _labels[202] = Toggle(portuguese ? "Bellatra: avisos de resultado" : "Bellatra result notices", !settings.HideBellatraResults);
        _labels[199] = Toggle(portuguese ? "Bellatra: selos da equipe" : "Bellatra team seals", !settings.HideBellatraTeamNotices);
        _labels[200] = "Mouse 4: " + MouseActionLabel(settings.CameraMouse4);
        _labels[201] = "Mouse 5: " + MouseActionLabel(settings.CameraMouse5);

        string MouseActionLabel(CameraMouseAction action) => action switch
        {
            CameraMouseAction.Disabled => portuguese ? "Desligado" : "Off",
            CameraMouseAction.RotateLeft => portuguese ? "Girar à esquerda" : "Rotate left",
            CameraMouseAction.RotateRight => portuguese ? "Girar à direita" : "Rotate right",
            CameraMouseAction.ZoomIn => portuguese ? "Aproximar" : "Zoom in",
            CameraMouseAction.ZoomOut => portuguese ? "Afastar" : "Zoom out",
            CameraMouseAction.TiltUp => portuguese ? "Inclinar para cima" : "Tilt up",
            CameraMouseAction.TiltDown => portuguese ? "Inclinar para baixo" : "Tilt down",
            _ => portuguese ? "Padrão" : "Default"
        };

        _labels[195] = Toggle(portuguese ? "Rastreador de missões" : "Quest tracker", !settings.HideQuestTracker);
        _labels[193] = Toggle(portuguese ? "Sem especialização" : "Without specialization", !settings.HideGroundEquipmentWithoutSpec);
        for (var index = 0; index < LootJobs.Length; index++)
        {
            var job = LootJobs[index];
            _labels[FirstSpecOption + index] = Toggle(portuguese ? job.Portuguese : job.English,
                (settings.HiddenGroundSpecJobs & (uint)job.Job) == 0);
        }

        string LevelLabel(string name, int level) => name + ": " + (level > 0 ? level.ToString() : portuguese ? "Sem limite" : "Unlimited");

        for (var i = 0; i < GroundLootFilter.Labels.Length; i++)
        {
            var label = GroundLootFilter.Labels[i];
            _labels[34 + i] = Toggle(portuguese ? label.Portuguese : label.English, !GroundLootFilter.Hidden(settings, i));
        }

        string Toggle(string name, bool enabled) => $"{name}: {(portuguese ? (enabled ? "Ligado" : "Desligado") : (enabled ? "On" : "Off"))}";
    }

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
