using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.Input;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.UI;

internal enum TouchHuntAction
{
    Toggle,
    Origin,
    PreviewHunt,
    PreviewLoot,
    ResetSettings,
    ResetTotals,
    ResetGroup,
    Rest,
    ClearLog
}

// The touch HUD's free auto-hunt (FreeHuntSession), drawn by the page as the Android client's eight-tab panel
// (touch-hunt.js). Its options are kept in the client settings (FreeHuntProfileStore), its totals and log too
// (ClientSettings.FreeHuntRecords, at most every HuntRecordSaveMs); the page reads TouchHunt and sends its options back as
// JSON (SetFreeHuntSettings) and its buttons as touch-hunt:<action>. The free hunt runs only with the touch HUD in use.
public sealed partial class GameplayHud
{
    private const string TouchHuntPrefix = "touch-hunt:";
    private const long HuntRecordSaveMs = 30_000;
    private static readonly JsonSerializerOptions HuntJson = new() { WriteIndented = false };
    private FreeHuntSession _freeHunt;
    private long _huntRecordSavedAt;
    private HuntTotals _huntRecordTotals;
    private int _huntRecordLog = -1;

    internal FreeHuntSession FreeHunt => _freeHunt;

    // The quest hunt or the free hunt runs (the touch HUD's Auto-caça orb).
    internal bool AnyHuntRunning => AutoHuntEnabled || _freeHunt?.Enabled == true;

    // The touch HUD's status chip: the quest hunt, or the free hunt with its "show the status board" option on.
    internal bool HuntStatusShown => AutoHuntEnabled || _freeHunt is { Enabled: true } hunt && hunt.Settings.ShowStatus;

    private string HuntScope => _context.Backend?.SettingsScope ?? "";

    // What the free hunt reads each frame; an NPC talking keeps it from starting.
    internal FreeHuntFrame FreeHuntFrame(bool canHunt, int fieldId) => new(_state, _questView, _npcOpen, canHunt && !_npcOpen, fieldId);

    // The screen hands over its free hunt: the character's options and records come from the client settings.
    internal void AttachFreeHunt(FreeHuntSession session)
    {
        _freeHunt = session;
        var settings = _context.Presentation.Requested;
        session.Settings = FreeHuntProfileStore.Load(settings.FreeHuntProfiles, CharacterName, HuntScope);
        if (settings.FreeHuntRecords.TryGetValue(CharacterName, out var text))
        {
            try
            {
                session.Restore(JsonSerializer.Deserialize<HuntRecord>(text, HuntJson));
            }
            catch (JsonException ex)
            {
                _context.Log.Warn($"[Hunt] Saved hunt record ignored: {ex.Message}");
            }
        }

        _huntRecordTotals = session.Totals;
        _huntRecordLog = session.LogVersion;
    }

    internal bool SetFreeHuntSettings(string json)
    {
        if (_freeHunt is null || !FreeHuntProfile.TryRead(json, out var settings))
        {
            return false;
        }

        ApplyFreeHuntSettings(settings);
        return true;
    }

    // An imported code pauses the hunt for review and keeps this character's account sharing.
    internal bool ImportFreeHuntProfile(string code)
    {
        if (_freeHunt is null || !FreeHuntProfile.TryDecode(code, out var profile))
        {
            return false;
        }

        _freeHunt.SetEnabled(false, HuntStopReason.ProfileImported);
        ApplyFreeHuntSettings(profile with { ShareAccount = _freeHunt.Settings.ShareAccount });
        return true;
    }

    internal string FreeHuntProfileCode() => _freeHunt is null ? "" : FreeHuntProfile.Encode(_freeHunt.Settings);

    private void ApplyFreeHuntSettings(FreeHuntSettings settings)
    {
        _freeHunt.Settings = settings;
        var requested = _context.Presentation.Requested;
        _context.Presentation.Requested = requested with
        {
            FreeHuntProfiles = FreeHuntProfileStore.Save(requested.FreeHuntProfiles, CharacterName, HuntScope, settings)
        };
    }

    // The totals and log kept with the client settings once they changed, at most every HuntRecordSaveMs unless forced.
    internal void SaveHuntRecord(bool force)
    {
        var now = Environment.TickCount64;
        if (_freeHunt is null || !force && now - _huntRecordSavedAt < HuntRecordSaveMs
            || ReferenceEquals(_freeHunt.Totals, _huntRecordTotals) && _freeHunt.LogVersion == _huntRecordLog)
        {
            return;
        }

        _huntRecordSavedAt = now;
        _huntRecordTotals = _freeHunt.Totals;
        _huntRecordLog = _freeHunt.LogVersion;
        var requested = _context.Presentation.Requested;
        _context.Presentation.Requested = requested with
        {
            FreeHuntRecords = new Dictionary<string, string>(requested.FreeHuntRecords)
            {
                [CharacterName] = JsonSerializer.Serialize(_freeHunt.Record(), HuntJson)
            }
        };
    }

    // The hunt's state for the page: whether it runs, may run or waits for the player; the totals, party and the owner's
    // level; and, with full, the options and what they offer (learned skills, the bag's crystals, Force Orbs and potions).
    internal string TouchHunt(bool full)
    {
        var hunt = _freeHunt;
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteBoolean("active", hunt?.Enabled == true);
            json.WriteBoolean("available", hunt?.Available == true);
            json.WriteBoolean("paused", hunt?.ManuallyPaused == true);
            json.WriteBoolean("resting", hunt?.StatisticsResting == true);
            json.WriteNumber("logVersion", hunt?.LogVersion ?? 0);
            json.WriteNumber("level", _state?.Level ?? 0);
            json.WriteNumber("experience", _state?.Experience ?? 0);
            json.WriteNumber("levelStart", _state?.LevelStart ?? 0);
            json.WriteNumber("nextLevel", _state?.NextLevel ?? 0);
            json.WritePropertyName("totals");
            JsonSerializer.Serialize(json, hunt?.Totals ?? HuntTotals.Empty, HuntJson);
            if (full && hunt is not null)
            {
                json.WritePropertyName("settings");
                JsonSerializer.Serialize(json, hunt.Settings, HuntJson);
                WriteHuntSkills(json);
                WriteHuntItems(json);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The learned skills a hunt may cast: the touch buttons' (TouchSkillSlots.Eligible), with their names and ranks.
    private void WriteHuntSkills(Utf8JsonWriter json)
    {
        json.WriteStartArray("skills");
        foreach (var owned in _state?.Skills ?? [])
        {
            if (ClassSkillCatalog.TryGet(owned.Code, out var skill) && TouchSkillSlots.Eligible(skill, owned.Rank))
            {
                json.WriteStartObject();
                json.WriteNumber("code", owned.Code);
                json.WriteString("name", DataText.SkillName(skill));
                json.WriteNumber("rank", owned.Rank);
                json.WriteEndObject();
            }
        }

        json.WriteEndArray();
    }

    // The bag's and belt's crystals, Force Orbs and potions by family, once each, for the panel's choices.
    private void WriteHuntItems(Utf8JsonWriter json)
    {
        json.WriteStartArray("items");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _state?.Items ?? [])
        {
            if (!(item.InBag || item.BeltBox >= 0) || !seen.Add(item.CatalogKey))
            {
                continue;
            }

            var kind = MonsterCrystals.TryGet(item.CatalogKey, out var crystal) && crystal.Kind == CrystalKind.Pet ? "crystal"
                : ForceOrbs.TryGet(item.CatalogKey, out _) ? "force"
                : PotionDefinition.FamilyOf(item.CatalogKey) switch
                {
                    PotionResource.Life => "hp",
                    PotionResource.Mana => "mp",
                    PotionResource.Stamina => "res",
                    _ => ""
                };
            if (kind.Length > 0)
            {
                json.WriteStartObject();
                json.WriteString("key", item.CatalogKey);
                json.WriteString("name", item.Name);
                json.WriteString("kind", kind);
                json.WriteEndObject();
            }
        }

        json.WriteEndArray();
    }

    // The log, oldest first: each line's time, what happened, why it stopped and the name it concerns.
    internal string TouchHuntLog()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var entry in _freeHunt?.LogEntries ?? [])
            {
                json.WriteStartObject();
                json.WriteString("time", entry.Time);
                json.WriteString("kind", entry.Kind.ToString());
                json.WriteString("reason", entry.Reason.ToString());
                json.WriteString("name", entry.Name);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The loot catalog's lines ("KEY\tName"), each once, with its loot category and picture, for the Coleta tab.
    internal string ClassifyHuntItems(string catalog)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var line in (catalog ?? "").Split('\n'))
            {
                var delimiter = line.IndexOf('\t');
                var key = delimiter > 0 ? line[..delimiter] : "";
                if (!ItemCodes.TryParse(key, out var code) || !seen.Add(key))
                {
                    continue;
                }

                json.WriteStartObject();
                json.WriteString("code", key);
                json.WriteString("name", line[(delimiter + 1)..].TrimEnd('\r'));
                json.WriteString("category", FreeHuntSettings.Category(key, code, code == ItemCodes.GoldCode).ToString());
                json.WriteString("potion", PotionDefinition.FamilyOf(key) switch
                {
                    PotionResource.Life => "@HP",
                    PotionResource.Mana => "@MP",
                    PotionResource.Stamina => "@RES",
                    _ => ""
                });
                json.WriteString("picture", _icons.TryPath(key, out var sprite) ? TouchPicturePath(sprite) : "");
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static bool TryReadHuntAction(string action, out TouchHuntAction kind)
    {
        kind = default;
        return action is not null && action.StartsWith(TouchHuntPrefix, StringComparison.Ordinal)
            && TryParseName(action[TouchHuntPrefix.Length..], out kind);
    }

    private bool RunTouchHunt(string action)
    {
        if (!action.StartsWith(TouchHuntPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (_freeHunt is not { } hunt || !TryReadHuntAction(action, out var kind))
        {
            return true;
        }

        switch (kind)
        {
            case TouchHuntAction.Toggle:
                ToggleFreeHunt();
                break;
            case TouchHuntAction.Origin:
                hunt.SetOrigin();
                break;
            case TouchHuntAction.PreviewHunt or TouchHuntAction.PreviewLoot:
                hunt.PreviewRange(kind == TouchHuntAction.PreviewLoot);
                break;
            case TouchHuntAction.ResetSettings:
                ApplyFreeHuntSettings(new FreeHuntSettings());
                break;
            case TouchHuntAction.ResetTotals:
                hunt.ResetStatistics();
                break;
            case TouchHuntAction.ResetGroup:
                hunt.ResetGroup();
                break;
            case TouchHuntAction.Rest:
                hunt.RestStatistics(!hunt.StatisticsResting);
                break;
            case TouchHuntAction.ClearLog:
                hunt.ClearLog();
                SaveHuntRecord(force: true);
                break;
        }

        return true;
    }

    // Pausar while it runs; else Iniciar where it may run, which ends the quest hunt and the automatic quest.
    private void ToggleFreeHunt()
    {
        if (_freeHunt is not { } hunt)
        {
            return;
        }

        if (hunt.Enabled)
        {
            hunt.SetEnabled(false);
        }
        else if (hunt.Available)
        {
            AutoHuntEnabled = false;
            StopQuestGuide();
            hunt.SetEnabled(true);
        }
    }
}
