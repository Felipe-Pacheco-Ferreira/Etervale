using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Localization;
using PristonTale.Simulation.Combat;

namespace PristonTale.Gameplay.Effects;

public enum FloatingTextKind
{
    DamageDealt,
    Critical,
    DamageTaken,
    Heal,
    Miss,
    Evade,
    // A hit between other entities (ClientSettings.ShowOtherPlayersDamage): smaller and dimmer than the local player's.
    OtherDamage
}

// Damage and heal numbers above the entities they hit. Entries live in a fixed ring; their text is formatted once when
// they are added, so drawing allocates nothing.
public sealed class FloatingCombatText
{
    public const int Capacity = 64;
    private const float LifetimeSeconds = 1.25f;
    private const float RiseUnits = 34f;

    // Spread numbers land this far apart (world units) instead of in a narrow column.
    private const float SpreadUnits = 40f;

    // The element icon's side as a share of the number's line height, and its gap from the number as a share of its side.
    private const float IconHeight = 0.8f;
    private const float IconGap = 0.15f;

    private readonly Entry[] _entries = new Entry[Capacity];
    private int _next;

    // The settings' number size and spread (DamageNumberSize, SpreadDamageNumbers).
    public float SizeScale { get; set; } = 1f;
    public bool Spread { get; set; }

    // The settings' element options (ShowElementIcon, ColorDamageByElement); the icons come from Draw's element art.
    public bool ShowElementIcon { get; set; } = true;
    public bool ColorByElement { get; set; }

    public static float ScaleOf(DamageNumberSize size) => size switch
    {
        DamageNumberSize.Small => 0.75f,
        DamageNumberSize.Large => 1.35f,
        DamageNumberSize.Huge => 1.75f,
        _ => 1f
    };

    public int Count
    {
        get
        {
            var count = 0;
            foreach (var entry in _entries)
            {
                count += entry.Age < LifetimeSeconds && entry.Text is not null ? 1 : 0;
            }

            return count;
        }
    }

    public void Add(Vector3 worldPosition, int amount, FloatingTextKind kind, Element element = Element.Bionic)
    {
        var text = kind switch
        {
            FloatingTextKind.Heal => "+" + amount,
            FloatingTextKind.Miss => ClientText.Get("Combat.Miss"),
            FloatingTextKind.Evade => ClientText.Get("Combat.Evade"),
            FloatingTextKind.Critical => amount + "!",
            _ => amount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var jitter = ((_next * 37) % 21 - 10) / 10f;
        var offset = Spread
            ? new Vector3(jitter * SpreadUnits, 0f, ((_next * 53) % 21 - 10) / 10f * SpreadUnits)
            : new Vector3(jitter * 15f, 0f, 0f);
        _entries[_next] = new Entry(worldPosition + offset, text, kind, element, 0f);
        _next = (_next + 1) % Capacity;
    }

    public void Update(float seconds)
    {
        for (var index = 0; index < _entries.Length; index++)
        {
            if (_entries[index].Text is not null && _entries[index].Age < LifetimeSeconds)
            {
                _entries[index].Age += seconds;
            }
        }
    }

    public void Draw(SpriteBatch batch, SpriteFont font, Viewport viewport, Matrix view, Matrix projection, Texture2D elementArt)
    {
        var begun = false;
        for (var index = 0; index < _entries.Length; index++)
        {
            ref readonly var entry = ref _entries[index];
            if (entry.Text is null || entry.Age >= LifetimeSeconds)
            {
                continue;
            }

            var life = entry.Age / LifetimeSeconds;
            var world = entry.Position + new Vector3(0f, RiseUnits * (1f - (1f - life) * (1f - life)), 0f);
            var projected = viewport.Project(world, projection, view, Matrix.Identity);
            if (projected.Z is < 0f or > 1f)
            {
                continue;
            }

            if (!begun)
            {
                batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
                begun = true;
            }

            var (color, style) = Style(entry.Kind);
            var scale = style * SizeScale;
            var alpha = life < 0.7f ? 1f : 1f - (life - 0.7f) / 0.3f;
            var pop = entry.Kind == FloatingTextKind.Critical ? 1f + 0.35f * MathF.Max(0f, 1f - entry.Age * 6f) : 1f;
            var size = font.MeasureString(entry.Text) * scale * pop;
            var at = new Vector2(projected.X - size.X / 2f, projected.Y - size.Y);
            batch.DrawString(font, entry.Text, at + new Vector2(1.5f, 1.5f), Color.Black * (0.75f * alpha), 0f, Vector2.Zero, scale * pop, SpriteEffects.None, 0f);
            batch.DrawString(font, entry.Text, at, DamageElementStyle.NumberColor(color, entry.Element, ColorByElement) * alpha, 0f, Vector2.Zero, scale * pop, SpriteEffects.None, 0f);
            DrawIcon(batch, elementArt, entry.Element, at, size, alpha);
        }

        if (begun)
        {
            batch.End();
        }
    }

    // The element's symbol right of the number, as tall as the number's text.
    private void DrawIcon(SpriteBatch batch, Texture2D art, Element element, Vector2 at, Vector2 size, float alpha)
    {
        var source = DamageElementStyle.IconOf(element, ShowElementIcon);
        if (art is null || source.IsEmpty)
        {
            return;
        }

        var side = size.Y * IconHeight;
        var target = new Rectangle((int)(at.X + size.X + IconGap * side), (int)(at.Y + (size.Y - side) / 2f), (int)side, (int)side);
        batch.Draw(art, target, source, DamageElementStyle.IconTint(element) * alpha);
    }

    private static (Color Color, float Scale) Style(FloatingTextKind kind) => kind switch
    {
        FloatingTextKind.Critical => (new Color(255, 196, 64), 1.25f),
        FloatingTextKind.DamageTaken => (new Color(255, 90, 80), 1f),
        FloatingTextKind.Heal => (new Color(120, 255, 140), 1.05f),
        FloatingTextKind.Miss => (new Color(200, 200, 200), 0.9f),
        FloatingTextKind.OtherDamage => (new Color(205, 205, 195), 0.8f),
        _ => (new Color(255, 255, 235), 1f)
    };

    private struct Entry(Vector3 position, string text, FloatingTextKind kind, Element element, float age)
    {
        public Vector3 Position = position;
        public string Text = text;
        public FloatingTextKind Kind = kind;
        public Element Element = element;
        public float Age = age;
    }
}
