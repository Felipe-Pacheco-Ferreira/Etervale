using System.IO.Compression;

internal static class VulkanAssets
{
    private static readonly object Gate = new();
    private static Task preparation;

    // Raise the version whenever ricarten-desktop.zip or opening-desktop.zip gains content: the archives are extracted
    // once per version, and older extractions are deleted.
    private const string CachePrefix = "ricarten-desktop-";
    private const int CacheVersion = 18;

    public static string Root => Path.Combine(CacheParent, CachePrefix + CacheVersion);

    private static string CacheParent => Android.App.Application.Context.NoBackupFilesDir!.AbsolutePath;

    public static Task PrepareAsync()
    {
        lock (Gate)
        {
            if (preparation is null || preparation.IsFaulted)
            {
                preparation = Task.Run(Extract);
            }

            return preparation;
        }
    }

    private static void Extract()
    {
        var root = Root;
        var ready = Path.Combine(root, "extracted");
        if (File.Exists(ready))
        {
            ExtractJourney(root);
            return;
        }

        DeleteOlderCaches(root);
        Directory.CreateDirectory(root);
        ExtractArchive(root, "ricarten-desktop.zip");
        ExtractArchive(root, "opening-desktop.zip");
        ExtractJourney(root);
        File.WriteAllText(ready, string.Empty);
    }

    private static void DeleteOlderCaches(string current)
    {
        foreach (var directory in Directory.EnumerateDirectories(CacheParent, CachePrefix + "*"))
        {
            if (string.Equals(directory, current, StringComparison.Ordinal))
            {
                continue;
            }

            // Only housekeeping: a folder that cannot be deleted now is tried again at the next new version.
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Android.Util.Log.Warn("Etervale.Assets", $"old asset cache {directory} kept: {error.Message}");
            }
        }
    }

    private static void ExtractJourney(string root)
    {
        var marker = Path.Combine(root, "adventurer-journey-extracted");
        if (!File.Exists(marker))
        {
            ExtractArchive(root, "adventurer-journey.zip");
            File.WriteAllText(marker, string.Empty);
        }
    }

    private static void ExtractArchive(string root, string name)
    {
        var package = Path.Combine(root, "assets.zip");
        try
        {
            using (var input = Android.App.Application.Context.Assets!.Open(name))
            using (var output = File.Create(package))
            {
                input.CopyTo(output);
            }

            ZipFile.ExtractToDirectory(package, root, overwriteFiles: true);
        }
        finally
        {
            File.Delete(package);
        }
    }
}
