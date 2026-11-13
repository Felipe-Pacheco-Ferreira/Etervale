using System.Security.Cryptography;
using PristonTale.Web.Hosting;

namespace PristonTale.Web.Services;

public sealed record AndroidFile(string Path, string DownloadName, string Version, long Length, DateTimeOffset LastWrite, string Sha256);

// The Android package of Downloads:AndroidApkPath. Its SHA-256 is computed once per version of the file, so players can
// check the file they got; replacing the file publishes a new one without a restart.
public sealed class AndroidPackage(WebSettings settings)
{
    public const string ContentType = "application/vnd.android.package-archive";
    private readonly string _path = WebSettings.BesideExecutable(settings.Downloads.AndroidApkPath);
    private readonly object _gate = new();
    private AndroidFile? _current;

    // Whether a package is offered, without reading it (pages that only link to the download).
    public bool IsAvailable => File.Exists(_path);

    public string Version => settings.Downloads.AndroidVersion.Trim();

    // Null while there is no package to offer. Replace the file by copying beside it and renaming, so the hash is never
    // taken from a half-copied file.
    public AndroidFile? Current()
    {
        var info = new FileInfo(_path);
        if (!info.Exists)
        {
            return null;
        }

        lock (_gate)
        {
            if (_current is null || _current.Length != info.Length || _current.LastWrite != info.LastWriteTimeUtc)
            {
                _current = Describe(info);
            }

            return _current;
        }
    }

    private AndroidFile Describe(FileInfo info)
    {
        using var stream = info.OpenRead();
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        var version = Version;
        var name = version.Length == 0 || !version.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
            ? "priston-avalon.apk"
            : $"priston-avalon-{version}.apk";
        return new AndroidFile(info.FullName, name, version, info.Length, info.LastWriteTimeUtc, hash);
    }
}
