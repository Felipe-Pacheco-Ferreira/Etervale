using Microsoft.Xna.Framework;
using PristonTale.Simulation.Combat;

namespace PristonTale.Gameplay.Effects;

// How a hit's element shows on its damage number (ClientSettings.ColorDamageByElement, ShowElementIcon): the number's
// colour and the element's symbol, cut from the resistance boxes of the status window's art (HudArt.Status), tinted
// with the colour. Earth, water and wind have no box there, so they show their colour only. Bionic is a physical hit.
public static class DamageElementStyle
{
    public const int IconPixels = 22;

    private static readonly (Color Color, Rectangle Icon)[] Looks = Table();

    private static (Color, Rectangle)[] Table()
    {
        var looks = new (Color, Rectangle)[(int)Element.Wind + 1];
        looks[(int)Element.Earth] = (new Color(214, 170, 96), Rectangle.Empty);
        looks[(int)Element.Fire] = (new Color(255, 124, 48), Symbol(190, 87));
        looks[(int)Element.Ice] = (new Color(120, 206, 255), Symbol(190, 123));
        looks[(int)Element.Lighting] = (new Color(255, 236, 96), Symbol(257, 87));
        looks[(int)Element.Poison] = (new Color(150, 232, 90), Symbol(257, 50));
        looks[(int)Element.Water] = (new Color(84, 152, 255), Rectangle.Empty);
        looks[(int)Element.Wind] = (new Color(176, 244, 214), Rectangle.Empty);
        return looks;
    }

    // The symbol inside a resistance box, without its frame.
    private static Rectangle Symbol(int x, int y) => new(x, y, IconPixels, IconPixels);

    private static bool Shown(Element element) => element != Element.Bionic && (uint)element < (uint)Looks.Length;

    // The number's colour: the element's when the option is on and the hit has one, else the kind's own.
    public static Color NumberColor(Color kindColor, Element element, bool colorByElement) =>
        colorByElement && Shown(element) ? Looks[(int)element].Color : kindColor;

    // The element's colour, for its icon; white for a physical hit.
    public static Color IconTint(Element element) => Shown(element) ? Looks[(int)element].Color : Color.White;

    // The icon's source in the status art, or Rectangle.Empty when the option is off or the element has none.
    public static Rectangle IconOf(Element element, bool showIcon) => showIcon && Shown(element) ? Looks[(int)element].Icon : Rectangle.Empty;
}
