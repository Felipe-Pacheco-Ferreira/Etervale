using PristonTale.WebAssetPacks;

const string Usage = "Usage: ptwebpacks --root <published client folder> --out <new output folder>";
string? root = null;
string? output = null;
for (var at = 0; at + 1 < args.Length; at += 2)
{
    switch (args[at])
    {
        case "--root":
            root = args[at + 1];
            break;
        case "--out":
            output = args[at + 1];
            break;
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

if (root is null || output is null || args.Length != 4)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    var report = await WebAssetPackBuilder.BuildAsync(Path.GetFullPath(root), Path.GetFullPath(output));
    Console.WriteLine($"{report.Files} files in {report.Chunks} chunks: {report.RawBytes} raw bytes, {report.ZipBytes} ZIP bytes, "
        + $"largest chunk {report.LargestChunkBytes} bytes, {report.Milliseconds:F0} ms");
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
