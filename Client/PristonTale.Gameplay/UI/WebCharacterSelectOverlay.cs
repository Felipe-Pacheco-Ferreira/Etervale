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
    Exit
}

public readonly record struct WebCharacterCommand(WebCharacterCommandKind Kind, int Index, string Text);

// The character list and creation the browser page draws over the canvas (Client/PristonTale.Browser/wwwroot/main.js)
// in place of CharacterSelectView. The screen reports a JSON picture of its state whenever it changes; the page sends
// the player's choices back as commands, taken on the screen's next update. Report is set only by the browser host.
internal static class WebCharacterSelectOverlay
{
    private static readonly Queue<WebCharacterCommand> Commands = new();

    internal static Action<string> Report { get; set; }

    public static bool Active => Report is not null;

    internal static void Send(WebCharacterCommandKind kind, int index, string text) => Commands.Enqueue(new WebCharacterCommand(kind, index, text ?? string.Empty));

    internal static bool TryTake(out WebCharacterCommand command) => Commands.TryDequeue(out command);

    internal static void Clear() => Commands.Clear();

    internal static void Show(string json) => Report?.Invoke(json);
}
