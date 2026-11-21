using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Contracts.Sessions;
using PristonTale.Gameplay.Localization;

namespace PristonTale.Gameplay.Screens.Opening;

// Runs the login: LoginPanel on the canvas, or in the browser the page's own form (WebLoginOverlay), which hands in the
// account and password and is told each step back.
internal sealed class OpeningLoginController : IDisposable
{
    private enum Phase { Login, Authenticating }

    private readonly ScreenContext _context;
    private readonly LoginAudio _audio;
    private readonly LoginPanel _login;
    private readonly bool _touchLayout;
    private readonly bool _webOverlay = WebLoginOverlay.Active;
    private bool _formShown;
    private Phase _phase = Phase.Login;
    private Task<LoginOutcome> _loginTask;
    private CancellationTokenSource _loginCts;

    public OpeningLoginController(ScreenContext context, LoginAudio audio, bool touchLayout)
    {
        _context = context;
        _audio = audio;
        _touchLayout = touchLayout;
        if (_webOverlay)
        {
            WebLoginOverlay.TryTake(out _, out _, out _);
        }
        else
        {
            _login = new LoginPanel(context.SpriteBatch, context.Fonts, context.UiAtlas) { TouchLayout = touchLayout };
        }
    }

    public bool KeyboardRequested => _login?.KeyboardRequested == true;
    public bool KeyboardFieldTapped => _login?.KeyboardFieldTapped == true;

    public enum LoginResult { None, Succeeded }

    public LoginResult Update(in KeyboardState keyboard, in KeyboardState lastKeyboard, MouseState pointer, float dt)
    {
        var typed = _context.TypedText.Drain();
        if (_webOverlay)
        {
            // The opening screen updates the login only once it is revealed: that is when the page shows its form.
            if (!_formShown)
            {
                _formShown = true;
                WebLoginOverlay.Show(WebLoginState.Form);
            }

            if (WebLoginOverlay.TryTake(out var account, out var password, out var endpoint) && _phase == Phase.Login)
            {
                // The world chosen on the page: its game server takes the login.
                if (endpoint.Length > 0 && endpoint != _context.ServerAddress)
                {
                    _context.SelectServer?.Invoke(endpoint);
                }

                BeginLogin(account, password);
            }
        }
        else if (_phase == Phase.Login && _login.Update(keyboard, lastKeyboard, pointer, typed, dt, _context.GraphicsDevice.Viewport) == LoginIntent.SubmitLogin)
        {
            BeginLogin(_login.Account.Text, _login.Password.Text);
        }

        return _phase == Phase.Authenticating ? PollLogin() : LoginResult.None;
    }

    private void BeginLogin(string account, string password)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
        {
            if (_login is not null)
            {
                _login.Status = "Enter your account and password.";
            }
            ReportToPage(WebLoginState.Form, ClientText.Get("Login.Failure.BadCredentials"));
            return;
        }

        _audio.Play(LoginCue.Login);
        if (_login is not null)
        {
            _login.Status = "Connecting…";
            _login.InputEnabled = false;
        }
        _phase = Phase.Authenticating;
        ReportToPage(WebLoginState.Busy, string.Empty);
        _loginCts = _touchLayout ? new CancellationTokenSource(TimeSpan.FromSeconds(20)) : new CancellationTokenSource();
        if (_context.Backend.RequiresNewConnection)
        {
            _context.RenewBackend();
        }

        _loginTask = _context.Backend.LoginAsync(account, password, _context.Presentation.Requested.Language, _loginCts.Token);
    }

    private LoginResult PollLogin()
    {
        if (_loginTask is not { IsCompleted: true })
        {
            return LoginResult.None;
        }

        var succeeded = _loginTask.IsCompletedSuccessfully && _loginTask.Result.Ok;
        if (!succeeded)
        {
            var message = _loginTask.IsCompletedSuccessfully
                ? ClientText.Get("Login.Failure." + _loginTask.Result.Failure)
                : ClientText.Get("Login.Unreachable");
            if (_login is not null)
            {
                _login.Status = message;
                _login.InputEnabled = true;
            }
            _phase = Phase.Login;
            ReportToPage(WebLoginState.Form, message);
        }
        else
        {
            ReportToPage(WebLoginState.Done, string.Empty);
        }

        _loginTask = null;
        _loginCts?.Dispose();
        _loginCts = null;
        return succeeded ? LoginResult.Succeeded : LoginResult.None;
    }

    public void Draw(Viewport viewport)
    {
        if (!_webOverlay)
        {
            _login.Draw(viewport);
        }
    }

    private void ReportToPage(WebLoginState state, string message)
    {
        if (_webOverlay)
        {
            WebLoginOverlay.Show(state, message);
        }
    }

    public void Dispose()
    {
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        if (_loginTask is not null)
        {
            _ = ObserveLoginAsync(_loginTask);
        }
    }

    private async Task ObserveLoginAsync(Task<LoginOutcome> login)
    {
        try
        {
            await login;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.Log.Error($"[Login] canceled request failed: {ex}");
        }
    }
}
