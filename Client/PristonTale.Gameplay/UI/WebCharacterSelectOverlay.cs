using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI;

public enum WebCharacterCommandKind
{
    Select,
    Enter,
    Delete,
    NewCharacter,
    Tribe,
    Class,
    Next,
    Face,
    Create,
    Back,
    Exit,
    PickOnStage,
    ResetStagePick
}

public enum WebCharacterStage
{
    Roster,
    Class
}

public readonly record struct WebCharacterCommand(WebCharacterCommandKind Kind, int Index, string Text,
    Vector2 StagePosition = default, double ClickSeconds = double.NaN, WebCharacterStage Stage = WebCharacterStage.Roster);

internal struct WebStageClickTracker
{
    internal const double DoublePressSeconds = 0.5d;

    private bool _pending;
    private int _index;
    private double _seconds;
    private WebCharacterStage _stage;

    internal readonly WebStageClickSnapshot Snapshot => new(_pending, _index, _seconds, _stage);

    public bool Pick(int index, int selectedIndex, double seconds, WebCharacterStage stage = WebCharacterStage.Class)
    {
        var valid = index >= 0 && double.IsFinite(seconds) && seconds >= 0;
        var confirm = valid && _pending && stage == _stage && index == _index && index == selectedIndex && seconds >= _seconds
            && seconds < _seconds + DoublePressSeconds;
        _pending = valid && !confirm;
        _index = index;
        _seconds = seconds;
        _stage = stage;
        return confirm;
    }

    public void Reset() => _pending = false;
}

internal readonly record struct WebStageClickSnapshot(bool Pending, int Index, double Seconds, WebCharacterStage Stage);

// The character list and creation the browser page draws over the canvas (Client/PristonTale.Browser/wwwroot/main.js)
// in place of CharacterSelectView. The screen reports a JSON picture of its state whenever it changes; the page sends
// the player's choices back as commands, taken on the screen's next update. Report is set only by the browser host.
internal static class WebCharacterSelectOverlay
{
    private static readonly Queue<WebCharacterCommand> Commands = new();

    internal static Action<string> Report { get; set; }

    internal static bool ProfileStageInput { get; set; }

    public static bool Active => Report is not null;

    internal static void Send(WebCharacterCommandKind kind, int index, string text) => Commands.Enqueue(new WebCharacterCommand(kind, index, text ?? string.Empty));

    internal static void PickOnStage(WebCharacterStage stage, Vector2 stagePosition, double clickSeconds) =>
        Commands.Enqueue(new WebCharacterCommand(WebCharacterCommandKind.PickOnStage, -1, string.Empty, stagePosition, clickSeconds, stage));

    internal static bool TryTake(out WebCharacterCommand command) => Commands.TryDequeue(out command);

    internal static void Clear() => Commands.Clear();

    internal static void Show(string json) => Report?.Invoke(json);
}
