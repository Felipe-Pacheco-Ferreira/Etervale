using System.IO.Compression;
using PristonTale.Assets;

namespace PristonTale.GltfExport;

// Builds what the Android character selection packages (docs/android.md): into the art directory, the screen's art
// (CharacterSelectArt) as PNG under each source's name and its cues and loops (CharacterSelectSounds) as they are; into
// the archive, the baked 3D scene and the music, which are too large to keep loose.
internal static class MobileCharacterSelectBake
{
    public const string SceneEntry = "CharSelect/scene.bin";
    public const string MusicEntry = "CharSelect/music.wav";

    public static int Write(string assetRoot, string artDirectory, string archivePath)
    {
        Directory.CreateDirectory(artDirectory);
        var missing = 0;
        foreach (var (directory, name) in CharacterSelectArt.Files())
        {
            var path = AssetFiles.ResolveFile(assetRoot, Path.Combine(directory, name));
            var image = path is null ? default : ImageDecode.Decode(File.ReadAllBytes(path), Path.GetExtension(path));
            if (!image.IsValid)
            {
                Console.Error.WriteLine($"character select art missing or unreadable: {directory}/{name}");
                missing++;
                continue;
            }

            PngWriter.Write(Path.Combine(artDirectory, Path.ChangeExtension(name, ".png")), image.Width, image.Height, image.Rgba);
        }

        foreach (var sound in CharacterSelectSounds.Cues.Concat(CharacterSelectSounds.Loops.Select(loop => loop.Path)))
        {
            File.Copy(Required(assetRoot, sound), Path.Combine(artDirectory, CharacterSelectSounds.PackagedName(sound)), overwrite: true);
        }

        File.Copy(Required(assetRoot, CharacterSelectArt.Font), Path.Combine(artDirectory, Path.GetFileName(CharacterSelectArt.Font)), overwrite: true);
        var work = Path.Combine(Path.GetTempPath(), $"ptgltf_select_{Environment.ProcessId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var scenePath = Path.Combine(work, "scene.bin");
            var characters = Path.Combine(work, MobileActorFormat.CharacterDirectory);
            MobileCharacterSelectSceneBake.Write(assetRoot, scenePath, characters);
            File.Delete(archivePath);
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(scenePath, SceneEntry, CompressionLevel.SmallestSize);
            archive.CreateEntryFromFile(Required(assetRoot, CharacterSelectSounds.Music), MusicEntry, CompressionLevel.SmallestSize);
            foreach (var character in Directory.EnumerateFiles(characters))
            {
                archive.CreateEntryFromFile(character, $"{MobileActorFormat.CharacterDirectory}/{Path.GetFileName(character)}", CompressionLevel.SmallestSize);
            }
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }

        return missing == 0 ? 0 : 1;
    }

    // Every world loading picture (WorldLoadingArt) as PNG under its source's name, no taller than MostHeight.
    public static int WriteLoadingArt(string assetRoot, string outputDirectory)
    {
        var source = AssetFiles.Resolve(AssetFiles.Combine(assetRoot, WorldLoadingArt.Directory));
        Directory.CreateDirectory(outputDirectory);
        var missing = 0;
        foreach (var path in Directory.EnumerateFiles(source, "*" + WorldLoadingArt.Extension))
        {
            var image = ImageDecode.Decode(File.ReadAllBytes(path), Path.GetExtension(path));
            if (!image.IsValid)
            {
                Console.Error.WriteLine($"loading art unreadable: {Path.GetFileName(path)}");
                missing++;
                continue;
            }

            var (width, height, rgba) = Shrink(image, WorldLoadingArt.MostPackagedHeight);
            PngWriter.Write(Path.Combine(outputDirectory, Path.ChangeExtension(Path.GetFileName(path), ".png")), width, height, rgba);
        }

        return missing == 0 ? 0 : 1;
    }

    // The picture averaged down by a whole factor until it is no taller than mostHeight.
    private static (int Width, int Height, byte[] Rgba) Shrink(DecodedImage image, int mostHeight)
    {
        var factor = (image.Height + mostHeight - 1) / mostHeight;
        if (factor <= 1)
        {
            return (image.Width, image.Height, image.Rgba);
        }

        var width = image.Width / factor;
        var height = image.Height / factor;
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sum = 0;
                    for (var dy = 0; dy < factor; dy++)
                    {
                        for (var dx = 0; dx < factor; dx++)
                        {
                            sum += image.Rgba[((y * factor + dy) * image.Width + x * factor + dx) * 4 + channel];
                        }
                    }

                    result[(y * width + x) * 4 + channel] = (byte)(sum / (factor * factor));
                }
            }
        }

        return (width, height, result);
    }

    private static string Required(string assetRoot, string relative) =>
        AssetFiles.ResolveFile(assetRoot, relative) ?? throw new FileNotFoundException("A character select file is missing.", relative);
}
