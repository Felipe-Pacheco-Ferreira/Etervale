using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Assets;
using PristonTale.Gameplay.Input;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.UI;

// The touch HUD's skills window, drawn by the page as the Android client's (touch-skills.js): a tab per class tier, the
// tier's skills as cards and the chosen skill's sheet, with "+" training a rank at the skill master the player talked to
// and the touch skill buttons to put a learned skill on. Opening the skills window (Habilidades, its key, a skill master)
// while the touch HUD is in use asks the page for it instead of the canvas window.
public sealed partial class GameplayHud
{
    private const string TouchTrainPrefix = "touch-train:";
    private const string TouchSkillButtonPrefix = "touch-skill-button:";

    private int _touchSkillsRequests;

    private void ToggleSkills()
    {
        if (TouchHudShown)
        {
            _touchSkillsRequests++;
            return;
        }

        TogglePanel(_skillSlide);
    }

    // A skill master's window: the page's skills window with its "+" while the touch HUD is in use.
    private void OpenSkillMaster(ulong master)
    {
        _skillMaster = master;
        if (TouchHudShown)
        {
            _touchSkillsRequests++;
            return;
        }

        OpenOnly(_skillSlide);
    }

    // How many times the skills window was asked for; with full, the tier's cards and the chosen skill's sheet (the
    // indexes run over the class's skill list; a negative tier is the class's own).
    internal string TouchSkills(bool full, int tier, int chosen)
    {
        var state = _state;
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("requests", _touchSkillsRequests);
            if (full && state is not null)
            {
                WriteTouchSkills(json, state, tier, chosen);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private void WriteTouchSkills(Utf8JsonWriter json, OwnerState state, int tier, int chosen)
    {
        var skills = ClassSkillCatalog.For(_class);
        var shownTier = Math.Clamp(tier < 0 ? state.Tier : tier, 0, PlayerCharacter.LastTier);
        json.WriteNumber("tier", shownTier);
        json.WriteNumber("points", state.SkillPoints);
        json.WriteNumber("elite", state.EliteSkillPoints);
        json.WriteBoolean("eliteShown", state.EliteSkillPoints > 0 || SkillLearning.OpenedBy(state.Tier) > SkillLearning.EliteStart);
        json.WriteBoolean("master", _skillMaster != 0);
        json.WriteBoolean("busy", _progression.Pending);
        json.WriteStartArray("tiers");
        for (var each = 0; each <= PlayerCharacter.LastTier && each * SkillLearning.SkillsPerTier < skills.Count; each++)
        {
            json.WriteStartObject();
            json.WriteString("title", DataText.ClassTitle(_class, each));
            json.WriteBoolean("reached", each <= state.Tier);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        var first = shownTier * SkillLearning.SkillsPerTier;
        var last = Math.Min(skills.Count, first + SkillLearning.SkillsPerTier);
        json.WriteStartArray("cards");
        for (var index = first; index < last; index++)
        {
            var skill = skills[index];
            var rank = state.SkillRank(skill.Code);
            json.WriteStartObject();
            json.WriteNumber("index", index);
            json.WriteString("name", DataText.SkillName(skill));
            json.WriteString("picture", skill.Code == 0 || skill.IconKey.Length == 0 ? ""
                : TouchPicturePath(AssetFiles.Combine(_context.AssetRoot, HudArt.SkillButton(_class, skill.IconKey, gray: rank == 0))));
            json.WriteNumber("ring", TouchSkillTier(skill.Tier));
            json.WriteNumber("rank", rank);
            json.WriteBoolean("passive", skill.MouseUse == SkillMouseUse.None);
            json.WriteBoolean("unavailable", skill.Code == 0);
            json.WriteNumber("button", TouchButtonOf(skill.Code));
            json.WriteBoolean("trainable", TouchTrainable(index, state));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("maxRank", SkillLearning.MaxRank);
        if (chosen >= first && chosen < last)
        {
            WriteTouchSkillSheet(json, skills, chosen, state);
        }
    }

    private void WriteTouchSkillSheet(Utf8JsonWriter json, IReadOnlyList<Skill> skills, int index, OwnerState state)
    {
        var skill = skills[index];
        var rank = state.SkillRank(skill.Code);
        json.WriteStartObject("sheet");
        json.WriteNumber("index", index);
        json.WriteString("name", DataText.SkillName(skill));
        json.WriteNumber("rank", rank);
        json.WriteBoolean("unavailable", skill.Code == 0);
        if (skill.Code != 0)
        {
            var shownRank = Math.Max(1, rank);
            json.WriteString("description", DataText.SkillDescription(skill.Code, skill.Description));
            json.WriteBoolean("passive", skill.MouseUse == SkillMouseUse.None);
            json.WriteString("cost", HudSkillsPanel.CostLine(skill, shownRank));
            json.WriteStartArray("details");
            if (SkillRuleCatalog.TryGet(skill.Code, out var rule))
            {
                foreach (var detail in HudSkillDetails.Lines(rule, shownRank))
                {
                    json.WriteStringValue(detail);
                }
            }

            json.WriteEndArray();
            var check = SkillLearning.Check(skills, index, state.SkillRank, state.Level, state.Tier, state.SkillPoints, state.EliteSkillPoints);
            var price = SkillLearning.Price(index, rank, _skills.Tax);
            json.WriteString("check", check.ToString());
            json.WriteNumber("price", price);
            json.WriteNumber("missing", Math.Max(0, price - state.Money));
            json.WriteBoolean("elite", SkillLearning.UsesElitePoints(index));
            json.WriteNumber("levelFor", SkillLearning.LevelFor(skill, rank));
            json.WriteString("tierTitle", DataText.ClassTitle(_class, index / SkillLearning.SkillsPerTier));
            json.WriteString("previous", index > 0 ? DataText.SkillName(skills[index - 1]) : "");
            json.WriteBoolean("trainable", TouchTrainable(index, state));
            json.WriteBoolean("buttons", TouchSkillSlots.Eligible(skill, rank));
        }

        json.WriteEndObject();
    }

    // The skills a touch button may take, for the list a long press on it opens (touch-skill-picker.js), in the class's
    // order: each with its index for touch-skill-button, name, picture, ring and the button holding it (-1 for none).
    internal string TouchSkillChoices()
    {
        var skills = ClassSkillCatalog.For(_class);
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            var state = _state;
            for (var index = 0; state is not null && index < skills.Count; index++)
            {
                var skill = skills[index];
                if (!TouchSkillSlots.Eligible(skill, state.SkillRank(skill.Code)))
                {
                    continue;
                }

                json.WriteStartObject();
                json.WriteNumber("index", index);
                json.WriteString("name", DataText.SkillName(skill));
                json.WriteString("picture", skill.IconKey.Length == 0 ? "" : TouchPicturePath(AssetFiles.Combine(_context.AssetRoot, HudArt.SkillButton(_class, skill.IconKey, gray: false))));
                json.WriteNumber("ring", TouchSkillTier(skill.Tier));
                json.WriteNumber("button", TouchButtonOf(skill.Code));
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The touch skill button (TouchSkillSlots) the player put the skill on, or -1.
    private int TouchButtonOf(uint code)
    {
        for (var slot = 0; slot < TouchSkillSlots.Count; slot++)
        {
            if (code != 0 && _bindings.Shortcut(slot).SkillId == code)
            {
                return slot;
            }
        }

        return -1;
    }

    // The next rank can be trained and paid for, and the player talked to a skill master.
    private bool TouchTrainable(int index, OwnerState state) => _skillMaster != 0 && _skills.CanTrain(index, state);

    // touch-train:<index> (a rank, asked of the skill master) and touch-skill-button:<slot>:<index> (a skill on a button).
    private bool RunTouchSkills(string action)
    {
        var skills = ClassSkillCatalog.For(_class);
        if (action.StartsWith(TouchTrainPrefix, StringComparison.Ordinal))
        {
            if (_state is { } state && !_progression.Pending && int.TryParse(action.AsSpan(TouchTrainPrefix.Length), out var index)
                && index >= 0 && index < skills.Count && TouchTrainable(index, state))
            {
                _progression.Submit(new ProgressionCommand(Guid.NewGuid(), Math.Max(state.ProgressionRevision, _progression.Revision), ProgressionAction.LearnSkill,
                    SkillCode: skills[index].Code, NpcEntityId: _skillMaster));
            }

            return true;
        }

        if (!action.StartsWith(TouchSkillButtonPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = action[TouchSkillButtonPrefix.Length..].Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var slot) && slot >= 0 && slot < TouchSkillSlots.Count && int.TryParse(parts[1], out var chosen)
            && chosen >= 0 && chosen < skills.Count && _state is { } owner && TouchSkillSlots.Eligible(skills[chosen], owner.SkillRank(skills[chosen].Code)))
        {
            var skill = skills[chosen];
            _bindings.Assign(slot, skill.Code, (skill.MouseUse & SkillMouseUse.Right) != 0 ? SkillMouseUse.Right : SkillMouseUse.Left);
        }

        return true;
    }
}
