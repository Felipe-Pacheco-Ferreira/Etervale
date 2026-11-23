using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using PristonTale.Assets;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Input;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Social;
using PristonTale.Simulation.Titles;
using Color = Microsoft.Xna.Framework.Color;

namespace PristonTale.Gameplay.UI;

// The browser's touch HUD, drawn by the page as the Android client's bottom HUD: the attack button, the skill buttons,
// the potion belt, the Menu and Bolsa buttons and the experience bar. A skill on the action bar's first slots holds that
// skill button; the others fill with the learned skills in order (TouchSkillSlots).
public sealed partial class GameplayHud
{
    private readonly uint[] _touchSkills = new uint[TouchSkillSlots.Count];
    private readonly bool[] _touchChosen = new bool[TouchSkillSlots.Count];

    // The attack button: the selected target, or else the nearest monster.
    internal event Action TouchAttackRequested;

    // A skill button, with the skill's code.
    internal event Action<uint> TouchSkillRequested;

    // The pictures the page may ask for (TouchPicture): the skill buttons', items' and field maps' files it was handed.
    private readonly HashSet<string> _touchPictures = new(StringComparer.OrdinalIgnoreCase);

    // With the page's touch HUD, the canvas leaves out what it replaces (the bottom bar with its orbs, action bar, belt
    // and skill sockets; the chat window; the target frame), both drawn and taking clicks.
    private bool TouchHudShown => WebWorldHud.TouchActive(_context.Presentation.Requested);

    // The target frame's entity (GameplayScreen sets it: the monster attacked, or else the one picked), or 0.
    internal ulong TouchTargetId { get; set; }

    // Where the page's HUD editor moved the player and target frames (TouchPortraitPlacement), for the 3D heads in them.
    internal Microsoft.Xna.Framework.Vector2 TouchPlayerShift { get; private set; }
    internal Microsoft.Xna.Framework.Vector2 TouchTargetShift { get; private set; }

    internal void SetTouchFrameShift(float playerX, float playerY, float targetX, float targetY)
    {
        TouchPlayerShift = TouchPortraitPlacement.Shift(playerX, playerY);
        TouchTargetShift = TouchPortraitPlacement.Shift(targetX, targetY);
    }

    // The bag's items when the player last looked in it, for the count of new ones on the Bolsa button.
    private readonly HashSet<Guid> _touchSeenItems = [];
    private bool _touchBagSeen;

    // What the touch HUD shows: whether the player is alive, the player frame (name, the title shown and its rarity's
    // colour, level, class title, life, mana,
    // stamina, points to spend, the portrait's window), the target frame (its window and the target's name, level and
    // life; life 0 of 0 for someone that cannot be fought), the level and the experience toward the next, each skill
    // button's skill (name, picture, tier, rank, cooldown), each belt box's potion, the items new in the bag and the
    // rewards ready to claim.
    internal string TouchState()
    {
        var state = _state;
        var skills = state?.Skills ?? Array.Empty<OwnedSkillView>();
        var now = SkillClockMs;
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
            json.WriteString("name", CharacterName ?? "");
            json.WriteNumber("level", state?.Level ?? 0);
            json.WriteNumber("maxLevel", ExperienceTable.MaximumLevel);
            json.WriteString("title", state is null ? "" : DataText.ClassTitle(_class, state.Tier));
            var shownTitle = Titles?.Shown ?? 0;
            var hasTitle = TitleRules.IsTitle(shownTitle);
            var titleColor = hasTitle ? HudNameColors.Title(TitleRules.Of((TitleCode)shownTitle).Rarity) : Color.Transparent;
            json.WriteString("shownTitle", hasTitle ? ClientText.TitleName(shownTitle) : "");
            json.WriteStartArray("shownTitleColor");
            json.WriteNumberValue(titleColor.R);
            json.WriteNumberValue(titleColor.G);
            json.WriteNumberValue(titleColor.B);
            json.WriteEndArray();
            json.WriteNumber("life", state?.Life ?? 0);
            json.WriteNumber("maxLife", state?.MaxLife ?? 0);
            json.WriteNumber("mana", state?.Mana ?? 0);
            json.WriteNumber("maxMana", state?.MaxMana ?? 0);
            json.WriteNumber("stamina", state?.Stamina ?? 0);
            json.WriteNumber("maxStamina", state?.MaxStamina ?? 0);
            json.WriteNumber("statPoints", state?.StatPoints ?? 0);
            json.WriteStartArray("portrait");
            json.WriteNumberValue(TouchPortraitPlacement.X);
            json.WriteNumberValue(TouchPortraitPlacement.Y);
            json.WriteNumberValue(TouchPortraitPlacement.Size);
            json.WriteEndArray();
            json.WriteStartArray("targetFrame");
            json.WriteNumberValue(TouchPortraitPlacement.TargetFrameWidth);
            json.WriteNumberValue(TouchPortraitPlacement.TargetFrameTop);
            json.WriteNumberValue(TouchPortraitPlacement.TargetInset);
            json.WriteNumberValue(TouchPortraitPlacement.TargetY);
            json.WriteNumberValue(TouchPortraitPlacement.TargetSize);
            json.WriteEndArray();
            if (Combat.TryGetTargetState(TouchTargetId, out var targetName, out var targetLevel, out var targetLife, out var targetMaxLife))
            {
                json.WriteStartObject("target");
                json.WriteNumber("id", TouchTargetId);
                json.WriteString("name", targetName ?? "");
                json.WriteNumber("level", targetLevel);
                json.WriteNumber("life", targetLife);
                json.WriteNumber("maxLife", targetMaxLife);
                json.WriteEndObject();
            }
            json.WriteNumber("experience", HudBar.ExperienceFraction(state));
            json.WriteNumber("maxRank", SkillLearning.MaxRank);
            json.WriteNumber("bag", TouchNewItems(state));
            json.WriteNumber("rewards", _rewardBoard.Tracks.Sum(track => track.Ready));
            json.WriteStartArray("skills");
            foreach (var code in _touchSkills)
            {
                json.WriteStartObject();
                var name = code != 0 && SkillRuleCatalog.TryGet(code, out var rule) ? DataText.SkillName(code, rule.Name) : "";
                var known = ClassSkillCatalog.TryGet(code, out var skill);
                json.WriteNumber("code", code);
                json.WriteString("name", name);
                json.WriteString("caption", TouchSkillSlots.ButtonCaption(name));
                json.WriteString("icon", known && skill.IconKey.Length > 0 ? TouchPicturePath(AssetFiles.Combine(_context.AssetRoot, HudArt.SkillButton(_class, skill.IconKey, gray: false))) : "");
                json.WriteNumber("tier", known ? TouchSkillTier(skill.Tier) : 0);
                json.WriteNumber("rank", state?.SkillRank(code) ?? 0);
                json.WriteNumber("remaining", Cooldowns?.RemainingMs(code, now) ?? 0d);
                json.WriteNumber("duration", Cooldowns?.DurationMs(code) ?? 0);
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
                json.WriteString("icon", item is not null && _icons.TryPath(item.CatalogKey, out var sprite) ? TouchPicturePath(sprite) : "");
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The newest chat lines of the All tab, oldest first, each with its kind's name (the touch HUD's chat lines and
    // window), and the count of lines added so far so the page redraws only when it grows.
    internal string TouchChat(int lines)
    {
        var log = _chat.Window.Log;
        var count = Math.Min(Math.Clamp(lines, 0, HudChatLog.Capacity), log.NewestCount(HudChatTab.All, 0));
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("added", log.Added);
            json.WriteStartArray("lines");
            for (var index = count - 1; index >= 0; index--)
            {
                var entry = log.NewestAt(HudChatTab.All, 0, index);
                json.WriteStartObject();
                json.WriteString("text", entry.Text);
                json.WriteString("kind", entry.Kind.ToString());
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // A line from the touch HUD's chat window, sent as the chat window's Enter sends it.
    internal void TouchChatSend(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            var line = text.Trim();
            _chat.SubmitText(line.Length > ChatRules.InputLimit ? line[..ChatRules.InputLimit] : line, _clock.Elapsed.TotalMilliseconds);
        }
    }

    // The items that came into the bag since it was last open; opening it counts them as seen.
    private int TouchNewItems(OwnerState state)
    {
        if (state is null)
        {
            return 0;
        }

        if (!_touchBagSeen || _inventorySlide.Open)
        {
            _touchSeenItems.Clear();
            foreach (var item in state.Items)
            {
                _touchSeenItems.Add(item.InstanceId);
            }

            _touchBagSeen = true;
            return 0;
        }

        var fresh = 0;
        foreach (var item in state.Items)
        {
            if (!_touchSeenItems.Contains(item.InstanceId))
            {
                fresh++;
            }
        }

        return fresh;
    }

    // A skill's tier as its button's ring colour (1 to 5, hud-art.js), 0 for none.
    private static int TouchSkillTier(JobTier tier) => tier != 0 ? BitOperations.Log2((uint)tier) + 1 : 0;

    private string TouchPicturePath(string path)
    {
        _touchPictures.Add(path);
        return path;
    }

    // A picture handed out in the touch state or map, decoded for the page: width and height (16-bit little-endian), then
    // straight (not premultiplied) RGBA rows; empty for any other path or an unreadable file.
    internal byte[] TouchPicture(string path)
    {
        if (string.IsNullOrEmpty(path) || !_touchPictures.Contains(path) || !AssetFiles.Exists(path))
        {
            return [];
        }

        var image = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(path), Path.GetExtension(path), bmpCoverage: true);
        if (!image.IsValid || image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
        {
            return [];
        }

        PristonUiPixels.Prepare(image, PristonUiColorKey.Black);
        var bytes = new byte[4 + image.Pixels.Length * 4];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)image.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)image.Height);
        for (var index = 0; index < image.Pixels.Length; index++)
        {
            var color = image.Pixels[index];
            var at = 4 + index * 4;
            var alpha = color.A;
            bytes[at] = Straight(color.R, alpha);
            bytes[at + 1] = Straight(color.G, alpha);
            bytes[at + 2] = Straight(color.B, alpha);
            bytes[at + 3] = alpha;
        }

        return bytes;

        static byte Straight(byte channel, byte alpha) => alpha is 0 or 255 ? channel : (byte)Math.Min(255, channel * 255 / alpha);
    }

    // touch-attack, touch-bag:..., touch-points:..., touch-train:..., touch-skill-button:..., touch-party:...,
    // touch-rewards:..., touch-quest-go, touch-run, touch-skill:<slot> and touch-belt:<box> (RunWorldAction takes touch-npc:...
    // and touch-restart:... first); false for any other action.
    private bool RunTouchAction(string action)
    {
        const string skillPrefix = "touch-skill:";
        const string beltPrefix = "touch-belt:";
        if (action == "touch-attack")
        {
            TouchAttackRequested?.Invoke();
            return true;
        }

        if (RunTouchBag(action) || RunTouchPoints(action) || RunTouchSkills(action) || RunTouchParty(action) || RunTouchRewards(action) || RunTouchHunt(action))
        {
            return true;
        }

        if (action == TouchQuestGoAction)
        {
            RunTouchQuestGo();
            return true;
        }
        if (action == "touch-run")
        {
            RunToggleRequested?.Invoke();
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
