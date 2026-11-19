using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Gameplay.Input;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.UI;

// The browser's touch HUD, drawn by the page as the Android client's action wheel: the attack button, the skill buttons
// and the potion belt. A skill on the action bar's first slots holds that skill button; the others fill with the learned
// skills in order (TouchSkillSlots).
public sealed partial class GameplayHud
{
    private readonly uint[] _touchSkills = new uint[TouchSkillSlots.Count];
    private readonly bool[] _touchChosen = new bool[TouchSkillSlots.Count];

    // The attack button: the selected target, or else the nearest monster.
    internal event Action TouchAttackRequested;

    // A skill button, with the skill's code.
    internal event Action<uint> TouchSkillRequested;

    // What the touch HUD shows: whether the player is alive, each skill button's code and name, each belt box's potion.
    internal string TouchState()
    {
        var state = _state;
        var skills = state?.Skills ?? Array.Empty<OwnedSkillView>();
        for (var slot = 0; slot < TouchSkillSlots.Count; slot++)
        {
            var shortcut = _bindings.Shortcut(slot).SkillId;
            _touchChosen[slot] = shortcut is not (0 or HudSkillBindings.BasicAttackId);
            if (_touchChosen[slot])
            {
                _touchSkills[slot] = shortcut;
            }
        }
        TouchSkillSlots.Fill(skills, _touchSkills, _touchChosen);

        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteBoolean("alive", state is { Life: > 0 });
            json.WriteStartArray("skills");
            foreach (var code in _touchSkills)
            {
                json.WriteStartObject();
                var name = code != 0 && SkillRuleCatalog.TryGet(code, out var rule) ? DataText.SkillName(code, rule.Name) : "";
                json.WriteNumber("code", code);
                json.WriteString("name", name);
                json.WriteString("caption", TouchSkillSlots.ButtonCaption(name));
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("belt");
            for (var box = 0; box < PotionBelt.Boxes; box++)
            {
                var item = state is null ? null : OwnedItemView.InBox(state.Items, box);
                json.WriteStartObject();
                json.WriteString("name", item?.Name ?? "");
                json.WriteNumber("count", item?.Count ?? 0);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // touch-attack, touch-skill:<slot> and touch-belt:<box>; false for any other action.
    private bool RunTouchAction(string action)
    {
        const string skillPrefix = "touch-skill:";
        const string beltPrefix = "touch-belt:";
        if (action == "touch-attack")
        {
            TouchAttackRequested?.Invoke();
            return true;
        }
        if (action.StartsWith(skillPrefix, StringComparison.Ordinal) && int.TryParse(action.AsSpan(skillPrefix.Length), out var slot)
            && slot >= 0 && slot < TouchSkillSlots.Count)
        {
            if (_touchSkills[slot] != 0)
            {
                TouchSkillRequested?.Invoke(_touchSkills[slot]);
            }
            return true;
        }
        if (action.StartsWith(beltPrefix, StringComparison.Ordinal) && int.TryParse(action.AsSpan(beltPrefix.Length), out var box)
            && box >= 0 && box < PotionBelt.Boxes)
        {
            _inventory.BeltKey(box, shift: false);
            return true;
        }

        return false;
    }
}
