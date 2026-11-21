using Microsoft.Xna.Framework.Audio;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Assets;

namespace PristonTale.Gameplay.Audio;

public sealed class PristonAudioLibrary : IDisposable
{
    private static readonly (int Code, string Directory)[] NpcSoundBankDirectories =
    [
        (CharacterSoundCodes.Wolverine, "wav/Effects/monster/Wolverin"),
        (CharacterSoundCodes.Fury, "wav/Effects/monster/Fury"),
        (CharacterSoundCodes.Morif, "wav/Effects/NPC/morif"),
        (CharacterSoundCodes.MollyWolf, "wav/Effects/NPC/mollywolf"),
        (CharacterSoundCodes.SkillMaster, "wav/Effects/NPC/SkillMaster"),
        (CharacterSoundCodes.MagicMaster, "wav/Effects/NPC/MagicMaster"),
        (CharacterSoundCodes.CastleDoor, "wav/Effects/NPC/castledoor"),
        (CharacterSoundCodes.CastleCrystalFire, "wav/Effects/NPC/crystal_r"),
        (CharacterSoundCodes.CastleCrystalLightning, "wav/Effects/NPC/crystal_g"),
        (CharacterSoundCodes.CastleCrystalIce, "wav/Effects/NPC/crystal_b"),
        (CharacterSoundCodes.CastleTower, "wav/Effects/NPC/tower-b"),
        (CharacterSoundCodes.CastleSoldierA, "wav/Effects/NPC/Soldier_A"),
        (CharacterSoundCodes.CastleSoldierB, "wav/Effects/NPC/Soldier_B"),
        (CharacterSoundCodes.CastleSoldierC, "wav/Effects/NPC/Soldier_C")
    ];

    private static readonly (string Code, string Path)[] FieldMusicFiles =
    [
        ("BGM_CODE_TOWN1", "wav/BGM/Town - Tempskron_Stronghold.bgm"),
        ("BGM_CODE_TOWN2", "wav/BGM/Town 1 - Tempskron_Ricarten - When wind comes-o.bgm"),
        ("BGM_CODE_FOREST", "wav/BGM/Field - Forest - DarkWood.bgm"),
        ("BGM_CODE_DUNGEON", "wav/BGM/Dungeon - Gloomy Heart.bgm"),
        ("BGM_CODE_FILAI", "wav/BGM/Town 2 - Morion_Philliy - Voyage above the Clouds.bgm"),
        ("BGM_CODE_SOD1", "wav/Sod/BGM/Stage_Play1.bgm"),
        ("BGM_CODE_SOD2", "wav/Sod/BGM/Stage_Play1.bgm"),
        ("BGM_CODE_SOD3", "wav/Sod/BGM/Stage_Play3.bgm"),
        ("BGM_CODE_DESERT", "wav/BGM/Field - Desert - Pilgrim.bgm"),
        ("BGM_CODE_ICE", "wav/BGM/Ice 1.bgm")
    ];

    private static readonly string[] AmbientFiles =
    [
        "wav/Ambient/stone-mill.wav",
        "wav/Ambient/dungeon_pumping_b.wav",
        "wav/Ambient/dungeon_pumping_l.wav",
        "wav/Ambient/dungeon_spin_b.wav",
        "wav/Ambient/lake 1.wav",
        "wav/Ambient/mystic 1.wav",
        "wav/Ambient/mystic 2.wav",
        "wav/Ambient/ship_swaing_l.wav",
        "wav/Ambient/stream.wav",
        "wav/Ambient/town_bell 1.wav",
        "wav/Ambient/town_bell 2.wav",
        "wav/Ambient/town_spin_b.wav",
        "wav/Ambient/town_spin_l.wav",
        "wav/Ambient/waterfall.wav",
        "wav/Ambient/stone-mill2.wav",
        "wav/Ambient/watermill 1.wav",
        "wav/Ambient/windmill 1.wav",
        "wav/Ambient/windmill 2.wav",
        "wav/Ambient/windmill 3.wav",
        "wav/Ambient/windmill 4.wav",
        "wav/Ambient/owl 1.wav",
        "wav/Ambient/dungeon_big-bolt 1.wav",
        "wav/Ambient/dungeon_stonepole.wav",
        "wav/Ambient/zombiemill 1.wav",
        "wav/Ambient/dungeon_weight.wav",
        "wav/Ambient/dungeon_device_side.wav",
        "wav/Ambient/dungeon_device_axe.wav",
        "wav/Ambient/dungeon_cage 1.wav"
    ];

    // In FieldAmbience order.
    private static readonly string[] FieldAmbientFiles =
    [
        "wav/Ambient/tempskron-town.wav",
        "wav/Ambient/forest-day.wav",
        "wav/Ambient/forest-night.wav",
        "wav/Ambient/ruin wind.wav",
        "wav/Ambient/desert wind.wav",
        "wav/Ambient/dungeon.wav",
        "wav/Ambient/temple_amb.wav",
        "wav/Ambient/cave_amb.wav",
        "wav/Ambient/darksanc_amb.wav",
        "wav/Ambient/iron_amb.wav",
        "wav/Ambient/iron_amb2.wav",
        "wav/Ambient/Babel Approach.wav",
        "wav/Ambient/icewind.wav"
    ];

    private const string OpeningMusicFile = "StartImage/Opening/Intro.wav";
    private const string PortalWarpFile = "wav/Effects/Skill/Learn_Skill.wav";
    private const string QuestFanfareFile = "image/Sinimage/Sound/drink2.wav";
    private const string WhisperFile = "wav/Effects/event/hosound.wav";
    private const string CraftFailureFile = "image/Sinimage/Sound/sheltom-failure.wav";
    private const string RainFile = "wav/rain01.wav";
    private const string ThunderFile = "wav/thunder1.wav";

    private readonly string _assetRoot;
    private readonly IClientLog _log;
    private readonly SoundLoader<SoundEffect> _sounds;
    private readonly bool _onDemand;
    private readonly Dictionary<string, string> _fieldMusic = new(StringComparer.Ordinal);
    private readonly Dictionary<(int SoundCode, MotionCode Motion), string[]> _npcSounds = [];
    private readonly Dictionary<uint, string> _talkSounds = [];
    private readonly Dictionary<EventCue, string> _events = [];
    private readonly Dictionary<LoginCue, string> _loginCues = [];
    private readonly List<(string Path, float Volume)> _openingLoops = [];
    private readonly List<(string Path, float Volume)> _characterSelectLoops = [];
    private int _npcSoundCount;

    private PristonAudioLibrary(string assetRoot, IClientLog log)
    {
        _assetRoot = assetRoot;
        _log = log;
        _onDemand = SoundFiles.Source.OnDemand;
        _sounds = new SoundLoader<SoundEffect>(
            path => SoundFiles.ReadAsync(assetRoot, path),
            static wave =>
            {
                using var stream = new MemoryStream(wave, writable: false);
                return SoundEffect.FromStream(stream);
            },
            (path, error) => log.Warn($"[Audio] sound {path} unavailable: {error.Message}"));
    }

    // The music and loops of the login and character screens as (asset-relative path, volume), each played through
    // LoginSound. The opening's are empty when the library was loaded without the opening music.
    public IReadOnlyList<(string Path, float Volume)> OpeningLoops => _openingLoops;
    public IReadOnlyList<(string Path, float Volume)> CharacterSelectLoops => _characterSelectLoops;
    public SoundEffect PortalWarpSound => _sounds.Get(PortalWarpFile);

    // Played when a quest starts, moves on or ends.
    public SoundEffect QuestFanfareSound => _sounds.Get(QuestFanfareFile);

    // Played when a whisper or notice opens the closed chat window.
    public SoundEffect WhisperSound => _sounds.Get(WhisperFile);

    public SoundEffect CraftFailureSound => _sounds.Get(CraftFailureFile);

    // The rain's loop and a lightning strike's thunder.
    public SoundEffect RainSound => _sounds.Get(RainFile);
    public SoundEffect ThunderSound => _sounds.Get(ThunderFile);
    public bool MusicEnabled { get; private set; } = true;
    public bool SoundEnabled { get; private set; } = true;
    public float MusicGain { get; private set; } = 1f;
    public float SoundGain { get; private set; } = 1f;
    public int AmbientCount => AmbientFiles.Length;
    public int FieldAmbientCount => FieldAmbientFiles.Length;
    public int NpcSoundCount => _npcSoundCount;
    public int TalkSoundCount => _talkSounds.Count;

    // From disk every sound is decoded here. When the sound files arrive on demand (the browser), nothing is decoded
    // yet: the short cues start downloading now, and music, ambience and NPC sounds when they are first asked for, so
    // each of them is silent until its file arrives.
    public static PristonAudioLibrary LoadBootstrap(string assetRoot, IClientLog log, bool loadOpeningMusic = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        ArgumentNullException.ThrowIfNull(log);

        var library = new PristonAudioLibrary(assetRoot, log);
        try
        {
            library.Catalogue(loadOpeningMusic);
            if (library._onDemand)
            {
                foreach (var path in library.CuePaths())
                {
                    library._sounds.Request(path);
                }
            }
            else
            {
                foreach (var path in library.CuePaths().Concat(library.StreamPaths()))
                {
                    library._sounds.Load(path);
                }
            }

            log.Info(
                $"[Audio] prepared {library._fieldMusic.Count} field music codes, {FieldAmbientFiles.Length} field ambience loops, "
                + $"{AmbientFiles.Length} positional ambience sounds, {library._npcSoundCount} NPC motion sounds and "
                + $"{library._talkSounds.Count} NPC talk sounds{(library._onDemand ? ", loaded as they are needed" : string.Empty)}.");
            return library;
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    // A login or character-screen loop of OpeningLoops or CharacterSelectLoops; null while its file is still arriving.
    public SoundEffect LoginSound(string path) => _sounds.Get(path);

    public SoundEffect ItemSound(string catalogKey) => _sounds.Get(ItemSoundRules.Directory + ItemSoundRules.For(catalogKey));

    public SoundEffect Cue(LoginCue cue) => _sounds.Get(_loginCues.GetValueOrDefault(cue));

    public SoundEffect CoinSound => _sounds.Get(ItemSoundRules.Directory + ItemSoundRules.Coin);

    public SoundEffect Event(EventCue cue) => _sounds.Get(_events.GetValueOrDefault(cue));

    // False for no music, and while the music's file is still arriving.
    public bool TryGetFieldMusic(string code, out SoundEffect music)
    {
        music = string.IsNullOrEmpty(code) || code == "BGM_CODE_CUSTOM" ? null : _sounds.Get(_fieldMusic.GetValueOrDefault(code));
        return music is not null;
    }

    // Null while the sound's file is still arriving.
    public SoundEffect Ambient(int code)
    {
        if ((uint)code >= (uint)AmbientFiles.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, $"Ambient sound code must be between 0 and {AmbientFiles.Length - 1}.");
        }

        return _sounds.Get(AmbientFiles[code]);
    }

    // Null while the sound's file is still arriving.
    public SoundEffect FieldAmbient(int code)
    {
        if ((uint)code >= (uint)FieldAmbientFiles.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, $"Field ambience code must be between 0 and {FieldAmbientFiles.Length - 1}.");
        }

        return _sounds.Get(FieldAmbientFiles[code]);
    }

    // One of the motion's sounds at random; null when it has none, and while that file is still arriving (asking
    // starts every variant's download).
    public SoundEffect NpcSound(int soundCode, MotionCode motion)
    {
        if (!_npcSounds.TryGetValue((soundCode, motion), out var variants))
        {
            return null;
        }

        foreach (var variant in variants)
        {
            _sounds.Request(variant);
        }

        return _sounds.Get(variants[Random.Shared.Next(variants.Length)]);
    }

    public bool TryGetTalkSound(string message, out SoundEffect sound)
    {
        sound = string.IsNullOrEmpty(message) ? null : _sounds.Get(_talkSounds.GetValueOrDefault(TalkSound.GetSoundCode(message)));
        return sound is not null;
    }

    public void ApplySettings(ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        MusicEnabled = settings.MusicEnabled;
        SoundEnabled = settings.SoundEnabled;
        MusicGain = Math.Clamp(settings.MusicVolumePercent, 0, 100) / 100f;
        SoundGain = Math.Clamp(settings.SoundVolumePercent, 0, 100) / 100f;
    }

    private void Catalogue(bool loadOpeningMusic)
    {
        if (loadOpeningMusic)
        {
            _openingLoops.Add((OpeningMusicFile, FieldAudioController.MusicVolume));
        }

        _characterSelectLoops.Add((CharacterSelectSounds.Music, FieldAudioController.MusicVolume));
        _characterSelectLoops.AddRange(LoginAudio.CharacterSelectLoopFiles);
        foreach (var (cue, path) in LoginAudio.CueFiles)
        {
            _loginCues.Add(cue, path);
        }

        foreach (var (code, path) in FieldMusicFiles)
        {
            _fieldMusic.Add(code, path);
        }

        foreach (var (cue, path) in EventSounds.Files)
        {
            _events[cue] = path;
        }

        CatalogueNpcSoundBanks();
        CatalogueTalkSounds();
    }

    // The short one-shot sounds any session plays, downloaded ahead when files arrive on demand.
    private IEnumerable<string> CuePaths() =>
    [
        PortalWarpFile, QuestFanfareFile, WhisperFile, CraftFailureFile, RainFile, ThunderFile,
        .. _loginCues.Values,
        .. ItemSoundRules.Files.Select(static file => ItemSoundRules.Directory + file)
    ];

    // The music, the loops, the Bellatra cues and the NPC sounds, fetched when first asked for when files arrive on demand.
    private IEnumerable<string> StreamPaths() =>
    [
        .. _openingLoops.Select(static loop => loop.Path),
        .. _characterSelectLoops.Select(static loop => loop.Path),
        .. _fieldMusic.Values,
        .. AmbientFiles,
        .. FieldAmbientFiles,
        .. _events.Values,
        .. _npcSounds.Values.SelectMany(static variants => variants),
        .. _talkSounds.Values
    ];

    private void CatalogueNpcSoundBanks()
    {
        var grouped = new Dictionary<(int SoundCode, MotionCode Motion), List<string>>();
        foreach (var (soundCode, relativeDirectory) in NpcSoundBankDirectories)
        {
            foreach (var path in SoundFiles.Waves(_assetRoot, relativeDirectory))
            {
                if (!TryGetMotion(Path.GetFileName(path), out var motion))
                {
                    continue;
                }

                var key = (soundCode, motion);
                if (!grouped.TryGetValue(key, out var variants))
                {
                    variants = [];
                    grouped.Add(key, variants);
                }

                variants.Add(path);
                _npcSoundCount++;
            }
        }

        foreach (var (key, variants) in grouped)
        {
            _npcSounds.Add(key, variants.ToArray());
        }
    }

    private void CatalogueTalkSounds()
    {
        var paths = SoundFiles.Waves(_assetRoot, "wav/Talk");
        if (paths.Count == 0)
        {
            _log.Info("[Audio] wav/Talk is absent; hashed NPC voice playback is disabled until the original voice archive is supplied.");
            return;
        }

        foreach (var path in paths)
        {
            if (!uint.TryParse(Path.GetFileNameWithoutExtension(path), out var soundCode))
            {
                _log.Warn($"[Audio] ignored NPC talk asset with a non-numeric name: {path}");
                continue;
            }

            _talkSounds[soundCode] = path;
        }
    }

    private static bool TryGetMotion(string fileName, out MotionCode motion)
    {
        if (fileName.StartsWith("attack", StringComparison.OrdinalIgnoreCase))
        {
            motion = MotionCode.Attack;
            return true;
        }

        if (fileName.StartsWith("damage", StringComparison.OrdinalIgnoreCase))
        {
            motion = MotionCode.Damage;
            return true;
        }

        if (fileName.StartsWith("dead", StringComparison.OrdinalIgnoreCase))
        {
            motion = MotionCode.Dead;
            return true;
        }

        if (fileName.StartsWith("neutral", StringComparison.OrdinalIgnoreCase))
        {
            motion = MotionCode.Stand;
            return true;
        }

        if (fileName.StartsWith("move", StringComparison.OrdinalIgnoreCase))
        {
            motion = MotionCode.Walk;
            return true;
        }

        if (fileName.StartsWith("disappear", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("change", StringComparison.OrdinalIgnoreCase))
        {
            motion = MotionCode.Warp;
            return true;
        }

        if (fileName.StartsWith("skill", StringComparison.OrdinalIgnoreCase))
        {
            motion = MotionCode.Skill;
            return true;
        }

        motion = default;
        return false;
    }

    public void Dispose()
    {
        foreach (var sound in _sounds.Created)
        {
            sound.Dispose();
        }

        _sounds.Clear();
        _fieldMusic.Clear();
        _loginCues.Clear();
        _events.Clear();
        _openingLoops.Clear();
        _characterSelectLoops.Clear();
        _npcSounds.Clear();
        _talkSounds.Clear();
        _npcSoundCount = 0;
    }
}
