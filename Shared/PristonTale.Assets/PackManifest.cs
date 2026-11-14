using System.Text;
using System.Text.Json;

namespace PristonTale.Assets;

// The asset packs a client downloads: packs.json beside the pack zips. Each pack is a zip of game files at their
// asset-root-relative paths (Path, '/' separated, in the data's own letter case); a field lists the packs it needs and
// its minimap's file prefix; Models maps a model path the server sends (ModelKey) to the pack that holds it.
public sealed record PackManifest(int Version, IReadOnlyList<PackEntry> Packs, IReadOnlyList<FieldPacks> Fields,
    IReadOnlyDictionary<string, string> Models)
{
    public const int CurrentVersion = 1;
    public const string FileName = "packs.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static PackManifest Read(Stream stream) =>
        JsonSerializer.Deserialize<PackManifest>(stream, Json) ?? throw new InvalidDataException("Empty pack manifest.");

    public void Write(Stream stream) => JsonSerializer.Serialize(stream, this, Json);

    public static string FieldPackId(string fieldId) => "field-" + Slug(fieldId);

    public static string MinimapPackId(string fieldId) => "minimap-" + Slug(fieldId);

    public static string ModelPackId(string modelPath) => "model-" + Slug(PathWithoutExtension(ModelKey(modelPath)));

    public static string PackFileName(string packId) => packId + ".zip";

    // A model path as the data and the server name it (either separator, any case, .ini or .inx), as one key.
    public static string ModelKey(string modelPath)
    {
        var path = modelPath.Replace('\\', '/').Trim('/').ToLowerInvariant();
        return PathWithoutExtension(path) + ".inx";
    }

    private static string PathWithoutExtension(string path)
    {
        var slash = path.LastIndexOf('/');
        var dot = path.LastIndexOf('.');
        return dot > slash ? path[..dot] : path;
    }

    private static string Slug(string text)
    {
        var slug = new StringBuilder(text.Length);
        foreach (var letter in text.ToLowerInvariant())
        {
            slug.Append(char.IsAsciiLetterOrDigit(letter) ? letter : '-');
        }

        return slug.ToString().Trim('-');
    }
}

public sealed record PackEntry(string Id, long Size, string Sha256, IReadOnlyList<PackFile> Files);

public sealed record PackFile(string Path, long Size);

// Packs: what the field needs before it loads (its stage). Monsters: the packs of the monsters and bosses that spawn in
// it, fetched while the player is there. Minimap: the asset-relative prefix of the field's <prefix>.png and <prefix>.bin,
// in MinimapPack; both empty when the field has none. Neither Monsters nor the minimap hold the field back.
public sealed record FieldPacks(int Code, string Id, string Name, IReadOnlyList<string> Packs, IReadOnlyList<string> Monsters, string Minimap,
    string MinimapPack);
