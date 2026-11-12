using System.IO.Compression;

internal static class VulkanAssets
{
    private static readonly object Gate = new();
    private static Task preparation;

    public static string Root => Path.Combine(Android.App.Application.Context.NoBackupFilesDir!.AbsolutePath, "ricarten-desktop-17");

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

        Directory.CreateDirectory(root);
        ExtractArchive(root, "ricarten-desktop.zip");
        ExtractArchive(root, "opening-desktop.zip");
        ExtractJourney(root);
        File.WriteAllText(ready, string.Empty);
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
