using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Protocol.Transport;

namespace PristonTale.Gameplay.Screens.Opening;

internal sealed class ServerSelectScreen(ScreenContext context) : GameScreen(context)
{
    private string[] _servers = [];
    private KeyboardState _previous;
    private int _selected;
    private Texture2D _pixel;

    public override string StatusText => ClientText.Language == "pt-BR" ? "Servidores" : "Servers";

    public override void Enter()
    {
        _pixel = new Texture2D(Context.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _previous = Keyboard.GetState();
        _servers = new[] { Context.ServerAddress }
            .Concat(Context.Presentation.Requested.GameServers ?? [])
            .Where(IsValid)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (_servers.Length > 0)
        {
            if (Pressed(Keys.Up))
            {
                _selected = (_selected + _servers.Length - 1) % _servers.Length;
            }

            if (Pressed(Keys.Down))
            {
                _selected = (_selected + 1) % _servers.Length;
            }

            if (Pressed(Keys.Enter))
            {
                Context.SelectServer?.Invoke(_servers[_selected]);
                Context.RequestScreen(new OpeningScreen(Context));
            }
        }

        _previous = keyboard;
        return;

        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && !_previous.IsKeyDown(key);
    }

    public override void Dispose() => _pixel?.Dispose();

    public override void HandleEscape() => Context.RequestScreen(new OpeningScreen(Context));

    public override void Draw()
    {
        Context.GraphicsDevice.Clear(new Color(24, 20, 15));
        var batch = Context.SpriteBatch;
        var font = Context.Font;
        var portuguese = ClientText.Language == "pt-BR";
        var viewport = Context.GraphicsDevice.Viewport;
        var width = Math.Max(1, Math.Min(530, viewport.Width - 24));
        var panel = new Rectangle((viewport.Width - width) / 2, Math.Max(0, (viewport.Height - 340) / 2), width, 340);
        batch.Begin();
        batch.Draw(_pixel, panel, new Color(159, 119, 52));
        var interior = panel;
        interior.Inflate(-1, -1);
        batch.Draw(_pixel, interior, new Color(26, 21, 14));
        batch.Draw(_pixel, new Rectangle(interior.X, interior.Y, interior.Width, 42), new Color(53, 42, 23));
        batch.DrawString(font, StatusText, new Vector2(panel.X + 16, panel.Y + 12), Color.Wheat);
        batch.DrawString(font, portuguese ? "Setas: selecionar   Enter: conectar   Esc: voltar" : "Arrows: select   Enter: connect   Esc: back",
            new Vector2(panel.X + 16, panel.Bottom - 31), Color.White);
        var first = Math.Max(0, _selected - 4);
        for (var index = first; index < Math.Min(first + 8, _servers.Length); index++)
        {
            var uri = TlsTransport.ParseAddress(_servers[index]);
            var label = uri.GetComponents(UriComponents.HostAndPort, UriFormat.SafeUnescaped);
            var row = new Rectangle(panel.X + 14, panel.Y + 54 + (index - first) * 29, Math.Max(1, panel.Width - 28), 26);
            batch.Draw(_pixel, row, index == _selected ? new Color(163, 122, 46) : new Color(76, 58, 30));
            var fill = row;
            fill.Inflate(-1, -1);
            if (fill.Width > 0)
            {
                batch.Draw(_pixel, fill, index == _selected ? new Color(65, 49, 23) : new Color(35, 28, 18));
            }
            batch.DrawString(font, label,
                new Vector2(row.X + 8, row.Y + 5), index == _selected ? Color.Gold : Color.Wheat);
        }

        batch.End();
    }

    private static bool IsValid(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        try
        {
            TlsTransport.ParseAddress(address);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
