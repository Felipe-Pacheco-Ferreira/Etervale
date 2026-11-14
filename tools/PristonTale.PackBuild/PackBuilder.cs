using System.IO.Compression;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using PristonTale.Assets;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.GltfExport;
using PristonTale.Simulation.World;

namespace PristonTale.PackBuild;

// Builds one pack per field (its stage, stage objects and textures), one per field minimap, and one per monster or NPC
// model (the model info, mesh, skeleton, textures and sub model), and packs.json. A field lists its stage pack and the
// packs of the monsters and bosses that spawn in it (the world database's spawn tables); every model of the database gets
// a pack, so a client can fetch one the server sends that no field lists (an event form).
internal static class PackBuilder
{
    // Every zip entry carries this time, so a pack rebuilt from the same files has the same bytes and SHA-256.
    private static readonly DateTimeOffset EntryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // SQLite hands integers back as 64-bit.
    private sealed record SpawnRow(long Code, string Model, string EventModel);

    private sealed record ModelRow(string Model, string EventModel);

    public static int Run(string assets, string database, string output)
    {
        Directory.CreateDirectory(output);
        var staging = Path.Combine(output, "minimaps-staging");
        var packs = new List<PackEntry>();
        var fields = new List<FieldPacks>();
        var models = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var unresolved = 0;
        var missing = 0;
        var problems = 0;

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        var spawns = connection.Query<SpawnRow>(new CommandDefinition("""
            SELECT f.field_code AS Code, m.model AS Model, m.event_model AS EventModel
            FROM field_monster_spawns s JOIN fields f ON f.stage = s.stage JOIN monsters m ON m.id = s.monster_id
            WHERE f.field_code IS NOT NULL
            UNION
            SELECT f.field_code, m.model, m.event_model
            FROM field_bosses b JOIN fields f ON f.stage = b.stage JOIN monsters m ON m.id = b.monster_id OR m.id = b.escort_monster_id
            WHERE f.field_code IS NOT NULL
            """)).ToList();
        // NPC models too: most come with the APK (char/npc), which their packs then find already in place.
        var allModels = connection.Query<ModelRow>(new CommandDefinition(
            "SELECT model AS Model, event_model AS EventModel FROM monsters UNION ALL SELECT model, '' FROM npcs")).ToList();

        foreach (var model in allModels.SelectMany(row => new[] { row.Model, row.EventModel }).Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(PackManifest.ModelKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var info = PristonModelPaths.Info(AssetFiles.Combine(assets, model));
            // A monster whose model the game data lacks is not drawn on the PC either; it gets no pack.
            if (!File.Exists(info))
            {
                Console.Error.WriteLine($"model {model}: not in the game data, no pack");
                missing++;
                continue;
            }

            try
            {
                var files = PristonAssetDependencies.ActorFiles(assets, info);
                unresolved += files.Count(file => file.Path.Length == 0);
                var id = PackManifest.ModelPackId(model);
                packs.Add(Write(output, id, assets, files.Select(file => file.Path), []));
                models[model] = id;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"model {model}: {error.Message}");
                problems++;
            }
        }

        foreach (var field in new WorldCatalog().AllFields.Where(field => field.FieldCode > 0 && field.StageSmd.Length > 0).OrderBy(field => field.FieldCode))
        {
            try
            {
                var files = PristonAssetDependencies.StageFiles(assets, field.StageSmd, field.StageObjectSmds);
                unresolved += files.Count(file => file.Path.Length == 0);
                var minimap = "";
                var minimapPack = "";
                var map = AssetFiles.Resolve(Path.Combine(assets, "Field", "map", field.Id + ".tga"));
                if (File.Exists(map))
                {
                    Directory.CreateDirectory(staging);
                    var prefix = Path.Combine(staging, field.Id + "-minimap");
                    MobileMinimapBake.Write(AssetFiles.Resolve(Path.Combine(assets, field.StageSmd)), map, prefix);
                    minimap = "minimaps/" + field.Id + "-minimap";
                    minimapPack = PackManifest.MinimapPackId(field.Id);
                    packs.Add(Write(output, minimapPack, assets, [], [(minimap + ".png", prefix + ".png"), (minimap + ".bin", prefix + ".bin")]));
                }

                var id = PackManifest.FieldPackId(field.Id);
                packs.Add(Write(output, id, assets, files.Select(file => file.Path), []));
                var monsters = spawns.Where(row => row.Code == field.FieldCode)
                    .SelectMany(row => new[] { row.Model, row.EventModel })
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => models.TryGetValue(PackManifest.ModelKey(path), out var pack) ? pack : null)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                fields.Add(new FieldPacks(field.FieldCode, field.Id, field.Name, [id], monsters, minimap, minimapPack));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"field {field.Id} ({field.FieldCode}): {error.Message}");
                problems++;
            }
        }

        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        var manifest = new PackManifest(PackManifest.CurrentVersion, packs, fields, models);
        using (var stream = File.Create(Path.Combine(output, PackManifest.FileName)))
        {
            manifest.Write(stream);
        }

        // A field left out would count as present on the phones, so a problem fails the build (exit code 1).
        var megabytes = packs.Sum(pack => pack.Size) / (1024d * 1024d);
        Console.WriteLine($"{fields.Count} fields, {models.Count} models, {packs.Count} packs, {megabytes:0} MB; "
            + $"{missing} models and {unresolved} textures not in the game data (left out), {problems} problems");
        return problems == 0 ? 0 : 1;
    }

    // Zips the files (asset paths on disk, unresolved ones left out) and the extra entries into <output>/<id>.zip.
    private static PackEntry Write(string output, string id, string assets, IEnumerable<string> files, IReadOnlyList<(string Entry, string Source)> extra)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files.Where(file => file.Length > 0))
        {
            var relative = Path.GetRelativePath(assets, file).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                throw new InvalidDataException($"{file} is outside the assets folder");
            }

            entries[relative] = file;
        }

        foreach (var (entry, source) in extra)
        {
            entries[entry] = source;
        }

        var path = Path.Combine(output, PackManifest.PackFileName(id));
        var listed = new List<PackFile>(entries.Count);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (entry, source) in entries)
            {
                var item = zip.CreateEntry(entry, CompressionLevel.Optimal);
                item.LastWriteTime = EntryTime;
                using (var input = File.OpenRead(source))
                using (var stream = item.Open())
                {
                    input.CopyTo(stream);
                }

                listed.Add(new PackFile(entry, new FileInfo(source).Length));
            }
        }

        using var read = File.OpenRead(path);
        return new PackEntry(id, read.Length, Convert.ToHexString(SHA256.HashData(read)).ToLowerInvariant(), listed);
    }
}
