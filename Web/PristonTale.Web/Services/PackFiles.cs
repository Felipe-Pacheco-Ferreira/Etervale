using PristonTale.Assets;
using PristonTale.Web.Hosting;

namespace PristonTale.Web.Services;

public sealed record PackDownload(string Path, string ContentType, DateTimeOffset LastWrite);

// The asset packs of Downloads:PacksPath (tools/PristonTale.PackBuild output): packs.json and the pack zips, served by
// name only. A name that is not packs.json or a pack id with .zip is refused, so no other file can be reached.
public sealed class PackFiles(WebSettings settings)
{
    public const string RoutePrefix = "/packs";
    private const string ZipType = "application/zip";
    private const string JsonType = "application/json";
    private readonly string _directory = WebSettings.BesideExecutable(settings.Downloads.PacksPath);

    public PackDownload? Find(string name)
    {
        if (!IsPackName(name))
        {
            return null;
        }

        var info = new FileInfo(Path.Combine(_directory, name));
        return info.Exists
            ? new PackDownload(info.FullName, name == PackManifest.FileName ? JsonType : ZipType, info.LastWriteTimeUtc)
            : null;
    }

    public static bool IsPackName(string name)
    {
        if (name == PackManifest.FileName)
        {
            return true;
        }

        const string Zip = ".zip";
        if (!name.EndsWith(Zip, StringComparison.Ordinal) || name.Length == Zip.Length)
        {
            return false;
        }

        foreach (var letter in name.AsSpan(0, name.Length - Zip.Length))
        {
            if (!char.IsAsciiLetterLower(letter) && !char.IsAsciiDigit(letter) && letter != '-')
            {
                return false;
            }
        }

        return true;
    }
}
