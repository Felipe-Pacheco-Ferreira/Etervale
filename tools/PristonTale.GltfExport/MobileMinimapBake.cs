using PristonTale.Contracts.Units;
using PristonTale.Engine3D.Formats;

namespace PristonTale.GltfExport;

// A field's minimap for Android: <prefix>.png, the map image, and <prefix>.bin, the four floats (left, top, right,
// bottom, field units) of the stage rectangle it covers.
internal static class MobileMinimapBake
{
    public static void Write(string stageSmd, string mapImage, string prefix)
    {
        var rectangle = new PristonSmdReader().ReadStage(stageSmd).AuthoredMapRectangle;
        var image = ImageDecode.Decode(File.ReadAllBytes(mapImage), Path.GetExtension(mapImage));
        if (!image.IsValid || rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
        {
            throw new InvalidDataException("Missing minimap image or stage bounds.");
        }

        PngWriter.Write(prefix + ".png", image.Width, image.Height, image.Rgba);
        using var writer = new BinaryWriter(File.Create(prefix + ".bin"));
        writer.Write(rectangle.Left / GameUnits.FixedScale);
        writer.Write(rectangle.Top / GameUnits.FixedScale);
        writer.Write(rectangle.Right / GameUnits.FixedScale);
        writer.Write(rectangle.Bottom / GameUnits.FixedScale);
    }
}
