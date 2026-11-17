namespace PristonTale.Gameplay.UI;

public enum WebLoginState
{
    Form,
    Busy,
    Done
}

// The login form the browser page draws over the canvas (Client/PristonTale.Browser/wwwroot/main.js) in place of
// LoginPanel. The page hands in what the player typed and the world chosen after it (its game server's wss:// address);
// the login screen reports each step back for the page to show. Report is set only by the browser host, so the other
// clients keep LoginPanel.
internal static class WebLoginOverlay
{
    private static string _account;
    private static string _password;
    private static string _endpoint;

    internal static Action<WebLoginState, string> Report { get; set; }

    public static bool Active => Report is not null;

    internal static void Submit(string account, string password, string endpoint = "")
    {
        _account = account ?? string.Empty;
        _password = password ?? string.Empty;
        _endpoint = endpoint ?? string.Empty;
    }

    internal static bool TryTake(out string account, out string password, out string endpoint)
    {
        account = _account;
        password = _password;
        endpoint = _endpoint;
        _account = null;
        _password = null;
        _endpoint = null;
        return account is not null;
    }

    internal static void Show(WebLoginState state, string message = "") => Report?.Invoke(state, message);
}
