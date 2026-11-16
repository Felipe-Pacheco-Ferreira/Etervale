using System.IO.Compression;
using System.Security.Cryptography;

// The game files on the phone, in one folder that every version keeps (Root): the APK's own archives are unpacked over
// it once per CacheVersion, and the asset packs downloaded later (VulkanPacks) live there too, so a new APK keeps them.
internal static class VulkanAssets
{
    private static readonly object Gate = new();
    private static Task preparation;

    // Raise the version whenever the APK archives gain content: they are unpacked again over the folder.
    private const int CacheVersion = 26;
    private const string RootName = "assets";
    private const string SkillEffectsArchive = "skill-effects.zip";
    private const string SkillEffectsMarker = "skill-effects.sha256";
    // The folders of the versions before the shared folder, one per version; deleted once.
    private const string OldCachePrefix = "ricarten-desktop-";

    private static readonly string[] Archives = ["ricarten-desktop.zip", "opening-desktop.zip", "adventurer-journey.zip", "summon-models.zip"];

    public static string Root => Path.Combine(CacheParent, RootName);

    private static float progress = -1f;

    // How far the APK archives are unpacked, 0 to 1, for the loading screen's bar; below 0 before it is known, and 1 once
    // there is nothing to unpack.
    public static float Progress => Volatile.Read(ref progress);

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
        var ready = Path.Combine(root, $"apk-archives-{CacheVersion}");
        var skillEffectsMarker = Path.Combine(root, SkillEffectsMarker);
        if (!File.Exists(ready))
        {
            ExtractBaseArchives(root, ready, skillEffectsMarker);
        }

        ExtractSkillEffects(root, skillEffectsMarker);
        Volatile.Write(ref progress, 1f);
    }

    private static void ExtractBaseArchives(string root, string ready, string skillEffectsMarker)
    {
        DeleteOldCaches();
        Directory.CreateDirectory(root);
        File.Delete(skillEffectsMarker);
        foreach (var marker in Directory.EnumerateFiles(root, "apk-archives-*"))
        {
            File.Delete(marker);
        }

        // Each archive is copied out of the APK and then unpacked: both steps count toward the progress by its size.
        var sizes = Archives.Select(ArchiveSize).ToArray();
        var total = Math.Max(1L, sizes.Sum() * 2);
        var done = 0L;
        for (var index = 0; index < Archives.Length; index++)
        {
            var before = done;
            ExtractArchive(root, Archives[index], step => Volatile.Write(ref progress, (float)((before + step) / (double)total)));
            done += sizes[index] * 2;
        }

        // The archives may have replaced files of downloaded packs: each pack is checked again by its files' sizes and
        // fetched again where they differ (PackStore).
        File.Delete(Path.Combine(root, PristonTale.Client.Net.Packs.PackStore.InstalledFileName));
        File.WriteAllText(ready, string.Empty);
    }

    private static void ExtractSkillEffects(string root, string marker)
    {
        using var input = Android.App.Application.Context.Assets!.Open(SkillEffectsArchive);
        using var package = new MemoryStream();
        input.CopyTo(package);
        var hash = Convert.ToHexString(SHA256.HashData(package.GetBuffer().AsSpan(0, (int)package.Length)));
        if (File.Exists(marker) && string.Equals(File.ReadAllText(marker), hash, StringComparison.Ordinal))
        {
            return;
        }

        package.Position = 0;
        using var zip = new ZipArchive(package, ZipArchiveMode.Read);
        ExtractEntries(Path.Combine(root, "Effect", "Vfx"), SkillEffectsArchive, zip, null);
        File.WriteAllText(marker, hash);
    }

    private static void DeleteOldCaches()
    {
        foreach (var directory in Directory.EnumerateDirectories(CacheParent, OldCachePrefix + "*"))
        {
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

    // The archive's size in the APK (the archives are stored uncompressed there), or 1 when it cannot be read: the
    // progress then still moves archive by archive.
    private static long ArchiveSize(string name)
    {
        try
        {
            using var descriptor = Android.App.Application.Context.Assets!.OpenFd(name);
            return Math.Max(1L, descriptor.Length);
        }
        catch (Java.IO.IOException)
        {
            return 1L;
        }
    }

    // Copies the archive out of the APK and unpacks it over the folder, reporting the bytes copied and then unpacked
    // (each counted against the archive's size).
    private static void ExtractArchive(string root, string name, Action<long> reported)
    {
        var package = Path.Combine(root, "assets.zip");
        try
        {
            var buffer = new byte[1 << 20];
            long copied = 0;
            using (var input = Android.App.Application.Context.Assets!.Open(name))
            using (var output = File.Create(package))
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    copied += read;
                    reported(copied);
                }
            }

            using var zip = ZipFile.OpenRead(package);
            ExtractEntries(root, name, zip, unpacked => reported(copied + Math.Min(unpacked, copied)));
        }
        finally
        {
            File.Delete(package);
        }
    }

    private static void ExtractEntries(string root, string name, ZipArchive zip, Action<long> reported)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        long unpacked = 0;
        foreach (var entry in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(fullRoot, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{name}: entry {entry.FullName} leaves the asset folder");
            }

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            unpacked += entry.CompressedLength;
            reported?.Invoke(unpacked);
        }
    }
}
