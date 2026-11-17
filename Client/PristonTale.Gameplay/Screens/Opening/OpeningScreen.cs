using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI;

namespace PristonTale.Gameplay.Screens.Opening;

public sealed class OpeningScreen : GameScreen
{
    private OpeningCinematic _cinematic;
    private WebArrivalBackdrop _webBackdrop;
    private OpeningLoginController _loginController;

    private LoginAudio _audio;
    private KeyboardState _lastKeyboard;
    private string _status;

    private bool _loginRevealed;

    public OpeningScreen(ScreenContext context) : base(context)
    {
    }

    public Func<MouseState> PointerInput { get; init; }
    public bool KeyboardRequested => _loginRevealed && _loginController.KeyboardRequested;
    public bool KeyboardFieldTapped => _loginRevealed && _loginController.KeyboardFieldTapped;

    public override string StatusText => _status;

    public override void Enter()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (WebLoginOverlay.Active)
        {
            _webBackdrop = new WebArrivalBackdrop(Context);
        }
        else
        {
            _cinematic = new OpeningCinematic(Context.GraphicsDevice, Context.Content, Context.Log, AssetPath,
                Context.AssetRoot, Context.Sky, Context.Presentation, Context.Effects);
            _cinematic.Load();
        }

        var loaded = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _audio = LoginAudio.Opening(Context.Audio);
        _loginController = new OpeningLoginController(Context, _audio, PointerInput is not null);
        _status = ClientText.Format("Status.Opening", _cinematic?.StatusSuffix ?? "3D");
        Context.Log.Info($"[Opening] scene {loaded:F0} ms, login "
            + $"{System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds - loaded:F0} ms.");
    }

    public override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _audio.Update(dt);

        _cinematic?.Update(gameTime);
        _webBackdrop?.UpdateFlight(gameTime);
        if (_webBackdrop is { Entering: true })
        {
            if (_webBackdrop.AtRoster)
            {
                // The next screen takes the live renderer before this screen is disposed.
                Context.RequestScreen(new CharacterSelectScreen(Context) { Backdrop = _webBackdrop });
                _webBackdrop = null;
            }

            return;
        }

        var keyboard = Keyboard.GetState();
        var pointer = PointerInput?.Invoke() ?? Mouse.GetState();
        if (Context.Presentation.AwaitingConfirmation)
        {
            _lastKeyboard = keyboard;
            return;
        }

        if (!_loginRevealed)
        {
            if (AnyKeyPressed(keyboard, _lastKeyboard) || _cinematic?.IsOver == true || PointerInput is not null && pointer.LeftButton == ButtonState.Pressed
                || WebLoginOverlay.Active)
                _loginRevealed = true;
        }
        else if (_loginController.Update(keyboard, _lastKeyboard, pointer, dt) == OpeningLoginController.LoginResult.Succeeded)
        {
            if (_webBackdrop is not null)
            {
                _webBackdrop.BeginEntry();
            }
            else
            {
                Context.RequestScreen(new CharacterSelectScreen(Context));
            }
        }

        _lastKeyboard = keyboard;
    }

    private static bool AnyKeyPressed(in KeyboardState current, in KeyboardState previous)
    {
        foreach (var key in current.GetPressedKeys())
            if (!previous.IsKeyDown(key))
                return true;
        return false;
    }

    public override void Draw()
    {
        _cinematic?.Draw();
        _webBackdrop?.Draw();

        var viewport = Context.GraphicsDevice.Viewport;
        if (!_loginRevealed)
        {
            Context.SpriteBatch.Begin();
            Context.SpriteBatch.End();
        }
        else
        {
            _loginController.Draw(viewport);
        }
    }

    public override void Dispose()
    {
        _loginController?.Dispose();
        _audio?.Dispose();
        _cinematic?.Dispose();
        _webBackdrop?.Dispose();
    }
}
