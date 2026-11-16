using System.Text;
using Android.Content.Res;

namespace PristonTale.Mobile;

internal static class MobileQuestText
{
    public static string Read(AssetManager assets, string folder, string file, string unavailable)
    {
        var relative = file.Replace('\\', '/').ToLowerInvariant();
        try
        {
            using var translation = new StreamReader(assets.Open($"{folder}/pt-br/{relative}"), Encoding.UTF8);
            return translation.ReadToEnd();
        }
        catch (Java.IO.IOException)
        {
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var original = new StreamReader(assets.Open($"{folder}/{relative}"), Encoding.GetEncoding(1252));
            return original.ReadToEnd();
        }
        catch (Java.IO.IOException)
        {
            return unavailable;
        }
    }
}
