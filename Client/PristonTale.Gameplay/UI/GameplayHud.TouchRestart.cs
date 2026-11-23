using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.UI;

// The touch HUD's death plate, drawn by the page as the Android client's (touch-restart.js): what the death costs, standing
// up in the field (closed where the experience does not cover it) or in town, and the seconds before the town is chosen
// by itself. It shows while the restart window would (HudRestartPanel.Open); the canvas leaves that window out meanwhile.
public sealed partial class GameplayHud
{
    private const string TouchRestartPrefix = "touch-restart:";

    internal string TouchRestart()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteBoolean("open", _restart.Open);
            if (_restart.Open)
            {
                json.WriteNumber("secondsLeft", _restart.SecondsLeft(_clock.Elapsed.TotalMilliseconds));
                json.WriteBoolean("fieldOpen", RestartOffer?.FieldOpen == true);
                json.WriteBoolean("known", RestartOffer.HasValue);
                json.WriteBoolean("scroll", RestartOffer?.Scroll == true);
                // Experience is 64-bit: it travels as text.
                json.WriteString("experience", (RestartOffer?.ExperienceShare ?? 0).ToString(CultureInfo.InvariantCulture));
                json.WriteString("gold", (RestartOffer?.GoldShare ?? 0).ToString(CultureInfo.InvariantCulture));
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // "touch-restart:Field" or "touch-restart:Town": the plate offers no exit, as the Android client's does not.
    internal static bool TryReadRestartAction(string action, out RestartChoice choice)
    {
        choice = default;
        if (action is null || !action.StartsWith(TouchRestartPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var name = action[TouchRestartPrefix.Length..];
        return TryParseName(name, out choice) && choice is RestartChoice.Field or RestartChoice.Town;
    }

    private bool RunTouchRestart(string action)
    {
        if (!action.StartsWith(TouchRestartPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (TryReadRestartAction(action, out var choice))
        {
            _restart.Pick(choice, RestartOffer?.FieldOpen == true);
        }

        return true;
    }
}
