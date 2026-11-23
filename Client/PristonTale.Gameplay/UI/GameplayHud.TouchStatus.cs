using System.Buffers;
using System.Text;
using System.Text.Json;
using PristonTale.Gameplay.Localization;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;

namespace PristonTale.Gameplay.UI;

// The touch HUD's status window, drawn by the page as the Android client's (touch-status.js): the level medallion, class,
// experience and vitals; the five attributes with "+" and "−" planning free points and Confirmar sending them; the combat
// values with what the planned points add (AttributeGains). Opening the status window (Personagem, its key) while the
// touch HUD is in use asks the page for it instead of the canvas window.
public sealed partial class GameplayHud
{
    private const string TouchPointsPrefix = "touch-points:";
    private const int AttributeCount = 5;

    private readonly int[] _touchPlanned = new int[AttributeCount];

    // Confirmed points still to send, attribute by attribute (one progression command at a time), and the completions
    // count when the last one left, so a refusal stops the rest.
    private readonly int[] _touchPointsQueue = new int[AttributeCount];
    private int _touchPointsSent = -1;
    private CharacterClassRegistry _touchClasses;
    private int _touchStatusRequests;

    private void ToggleStatus()
    {
        if (TouchHudShown)
        {
            _touchStatusRequests++;
            return;
        }

        TogglePanel(_statusSlide);
    }

    // How many times the status window was asked for; with full, the character and, for planned (five comma-separated
    // amounts), what those points add.
    internal string TouchStatus(bool full, string planned)
    {
        var state = _state;
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("requests", _touchStatusRequests);
            if (full && state is not null)
            {
                WriteTouchStatus(json, state, TryReadPoints(planned, state.StatPoints, _touchPlanned) ? _touchPlanned : null);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private void WriteTouchStatus(Utf8JsonWriter json, OwnerState state, int[] planned)
    {
        var classes = _touchClasses ??= new CharacterClassRegistry();
        json.WriteString("name", CharacterName ?? "");
        json.WriteString("title", DataText.ClassTitle(_class, state.Tier));
        json.WriteNumber("level", state.Level);
        json.WriteNumber("maxLevel", ExperienceTable.MaximumLevel);
        var span = state.NextLevel > state.LevelStart ? state.NextLevel - state.LevelStart : 1UL;
        json.WriteNumber("experience", state.Experience > state.LevelStart ? Math.Min(1d, (state.Experience - state.LevelStart) / (double)span) : 0d);
        WritePair(json, "life", state.Life, state.MaxLife);
        WritePair(json, "mana", state.Mana, state.MaxMana);
        WritePair(json, "stamina", state.Stamina, state.MaxStamina);
        WritePair(json, "weight", state.CarriedWeight, state.MaxWeight);
        json.WriteBoolean("overweight", state.Overweight);
        json.WriteNumber("points", state.StatPoints);
        json.WriteNumber("main", (int)classes.DamageAttribute(_class));
        json.WriteStartArray("attributes");
        json.WriteNumberValue(state.Strength);
        json.WriteNumberValue(state.Spirit);
        json.WriteNumberValue(state.Talent);
        json.WriteNumberValue(state.Agility);
        json.WriteNumberValue(state.Health);
        json.WriteEndArray();
        json.WriteStartObject("combat");
        json.WriteNumber("damageMin", state.DamageMin);
        json.WriteNumber("damageMax", state.DamageMax);
        json.WriteNumber("attackRating", state.AttackRating);
        json.WriteNumber("defence", state.Defence);
        json.WriteNumber("absorption", state.Absorption);
        json.WriteNumber("critical", state.CriticalChance);
        json.WriteNumber("block", state.BlockChance);
        json.WriteNumber("moveSpeed", state.MoveSpeed);
        json.WriteNumber("skillPoints", state.SkillPoints);
        json.WriteEndObject();
        if (planned is not null)
        {
            var gains = PlannedGains(classes, state, planned);
            json.WriteStartObject("gains");
            json.WriteNumber("damageMin", gains.DamageMin);
            json.WriteNumber("damageMax", gains.DamageMax);
            json.WriteNumber("attackRating", gains.AttackRating);
            json.WriteNumber("defence", gains.Defence);
            json.WriteNumber("absorption", gains.Absorption);
            json.WriteNumber("moveSpeed", gains.MoveSpeed);
            json.WriteNumber("life", gains.MaxLife);
            json.WriteNumber("mana", gains.MaxMana);
            json.WriteNumber("stamina", gains.MaxStamina);
            json.WriteNumber("weight", gains.MaxWeight);
            json.WriteEndObject();
        }

        json.WriteBoolean("busy", TouchPointsBusy);

        static void WritePair(Utf8JsonWriter json, string name, long value, long max)
        {
            json.WriteStartArray(name);
            json.WriteNumberValue(value);
            json.WriteNumberValue(max);
            json.WriteEndArray();
        }
    }

    // What the planned points add, from the attributes, the level and the worn weapon and usable worn items' damage.
    private AttributeGains PlannedGains(CharacterClassRegistry classes, OwnerState state, int[] planned)
    {
        var now = new CharacterAttributes(state.Strength, state.Spirit, state.Talent, state.Agility, state.Health);
        var after = now.Add(new CharacterAttributes(planned[0], planned[1], planned[2], planned[3], planned[4]));
        ItemKind? weapon = null;
        var damageMin = 0;
        var damageMax = 0;
        foreach (var item in state.Items)
        {
            if (!item.Equipped || item.Details is not { ClassAllowed: true } details)
            {
                continue;
            }

            damageMin += details.Roll.DamageMin;
            damageMax += details.Roll.DamageMax;
            weapon = item.Slot == (int)EquipmentSlot.Weapon ? item.Kind : weapon;
        }

        return AttributeGains.Of(classes.Get(_class).Definition.FunctionProfile, state.Level, now, after, weapon, damageMin, damageMax);
    }

    // Five comma-separated amounts, none negative, some planned and within the free points.
    internal static bool TryReadPoints(string text, int free, int[] amounts)
    {
        Array.Clear(amounts);
        var parts = text?.Split(',');
        if (parts is not { Length: AttributeCount })
        {
            return false;
        }

        var total = 0;
        for (var index = 0; index < AttributeCount; index++)
        {
            if (!int.TryParse(parts[index], out amounts[index]) || amounts[index] < 0)
            {
                Array.Clear(amounts);
                return false;
            }

            total += amounts[index];
        }

        if (total == 0 || total > free)
        {
            Array.Clear(amounts);
            return false;
        }

        return true;
    }

    private bool TouchPointsBusy => _progression.Pending || Array.Exists(_touchPointsQueue, static points => points > 0);

    // touch-points:<five amounts>, Confirmar: the planned points, sent attribute by attribute.
    private bool RunTouchPoints(string action)
    {
        if (!action.StartsWith(TouchPointsPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (_state is { } state && !TouchPointsBusy && TryReadPoints(action[TouchPointsPrefix.Length..], state.StatPoints, _touchPointsQueue))
        {
            _touchPointsSent = -1;
            SendTouchPoints();
        }

        return true;
    }

    // Each update: the next queued attribute once the last command answered; a refusal drops the rest.
    private void SendTouchPoints()
    {
        if (_progression.Pending || _state is not { } state)
        {
            return;
        }

        if (_touchPointsSent >= 0 && _progression.Completions != _touchPointsSent && !_progression.LastOk)
        {
            Array.Clear(_touchPointsQueue);
            return;
        }

        for (var index = 0; index < AttributeCount; index++)
        {
            if (_touchPointsQueue[index] == 0)
            {
                continue;
            }

            _touchPointsSent = _progression.Completions;
            _progression.Submit(new ProgressionCommand(Guid.NewGuid(), Math.Max(state.ProgressionRevision, _progression.Revision),
                ProgressionAction.AllocateAttribute, (CharacterAttribute)index, _touchPointsQueue[index]));
            _touchPointsQueue[index] = 0;
            return;
        }
    }
}
