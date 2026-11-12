using PristonTale.Contracts.Units;
using PristonTale.Assets;
using PristonTale.Engine3D.Equipment;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.GltfExport;

internal static class MobileAppearanceBake
{
    public static void Write(string root, string output, string[] armorKeys)
    {
        Directory.CreateDirectory(output);
        var temporary = Path.Combine(Path.GetTempPath(), $"etervale-appearance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            WritePets(root, output, temporary);
            WriteEquipment(root, output, temporary);

            using var catalog = new StreamWriter(Path.Combine(output, "armor.tsv"));
            var registry = new CharacterClassRegistry();
            var keys = armorKeys.Length == 0 ? EquipmentVisualTables.ArmorSlots.Keys.AsEnumerable() : armorKeys;
            foreach (var job in CharacterSelectArt.TempskronLineup.Concat(CharacterSelectArt.MorionLineup))
            {
                var definition = registry.Get(job).Definition;
                WriteSockets(root, Path.Combine(output, $"sockets-{(int)job}.bin"), definition.DefaultVisuals.Body.InxExtensionPath, definition);
                var row = EquipmentVisualTables.Armors[definition.DefaultVisuals.ArmorTableIndex];
                var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in keys)
                {
                    if (key.StartsWith("CA", StringComparison.OrdinalIgnoreCase)
                        || !EquipmentVisualTables.ArmorSlots.TryGetValue(key, out var slot) || (uint)slot >= (uint)row.Length)
                    {
                        continue;
                    }
                    var relativePath = $"char/tmABCD/{row[slot]}";
                    if (AssetFiles.ResolveFile(root, relativePath) is null)
                    {
                        continue;
                    }
                    var file = $"armor-{(int)job}-{Path.GetFileNameWithoutExtension(row[slot]).ToLowerInvariant()}.bin";
                    if (written.Add(file))
                    {
                        var textures = new List<byte[]>();
                        var body = MobileCharacterSelectSceneBake.BakePart(root, relativePath, definition.MotionJobMask, temporary, textures, null);
                        WritePart(Path.Combine(output, file), textures, body);
                        WriteSockets(root, Path.Combine(output, Path.ChangeExtension(file, ".sockets.bin")), relativePath, definition);
                    }
                    catalog.WriteLine($"{(int)job}\t{key}\t{file}");
                }
            }
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static void WriteEquipment(string root, string output, string temporary)
    {
        var starter = CharacterSelectArt.TempskronLineup.Concat(CharacterSelectArt.MorionLineup)
            .Select(StarterGifts.WeaponKey).Append("DS101").ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var catalog = new StreamWriter(Path.Combine(output, "gear.tsv"));
        foreach (var item in EquipmentVisualTables.Items.Where(item => starter.Contains(item.Key)))
        {
            var mesh = AssetFiles.ResolveFile(root, $"image/sinimage/Items/DropItem/it{item.Mesh}.smd");
            if (mesh is null)
            {
                throw new FileNotFoundException($"No equipment mesh for {item.Key}.");
            }

            var options = new ExportOptions(FlipWinding: false, FlipV: false, NoAnims: true);
            var materials = new MaterialFactory(Path.GetDirectoryName(mesh)!, temporary, forceDoubleSided: true);
            var textures = new List<byte[]>();
            var actor = new PristonActorLoader().LoadRigid(mesh);
            var gltf = GltfSceneExport.ToModel(GltfSceneExport.BuildModel(actor.Mesh, materials, options), options);
            var model = MobileCharacterSelectSceneBake.Reload(gltf, temporary, textures);
            var frame = model.Bake(null, 0f).Select(batch => MobileActorWriter.Channel(batch.Vertices, 0, 3)).ToArray();
            var filename = $"gear-{item.Key.ToLowerInvariant()}.bin";
            WritePart(Path.Combine(output, filename), textures, new MobilePart(MobileActorWriter.Batches(model, null), [new MobileClip(1f, [frame])]));
            var hand = item.LeftHand ? "Bip01 L Hand" : "Bip weapon01";
            var village = item.VillageBone.Length > 0 ? item.VillageBone : hand;
            catalog.WriteLine($"{item.Key}\t{filename}\t{hand}\t{village}");
        }
    }

    private static void WriteSockets(string root, string output, string relative, CharacterClassDef definition)
    {
        var path = AssetFiles.ResolveFile(root, relative) ?? throw new FileNotFoundException(relative);
        var actor = new PristonActorLoader().Load(root, path);
        var clips = GltfSceneExport.EnumeratePlayerClips(actor, definition.MotionJobMask).ToArray();
        var animator = new PristonModelAnimator(actor.Skeleton);
        var wanted = EquipmentVisualTables.Items.Select(item => item.VillageBone)
            .Concat(["Bip01 L Hand", "Bip weapon01", "Bip01 L Forearm", "Bip01 R Forearm", "Bip weapon05", "Bip in07", "Bip in08"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bones = actor.Skeleton.Objects.Select((bone, index) => (bone.Name, Index: index))
            .Where(bone => wanted.Contains(bone.Name)).ToArray();
        using var writer = new BinaryWriter(File.Create(output));
        writer.Write(bones.Length);
        foreach (var bone in bones)
        {
            writer.Write(bone.Name);
        }
        var names = new[] { GltfSceneExport.StandClip, GltfSceneExport.WalkClip, GltfSceneExport.RunClip };
        const int samples = 32;
        writer.Write(samples);
        foreach (var name in names)
        {
            var clip = clips.Single(clip => clip.Name == name);
            for (var index = 0; index < samples; index++)
            {
                var tick = clip.StartTicks + (clip.EndTicks - clip.StartTicks) * index / (float)samples;
                var pose = animator.Evaluate(tick, clip.EndTicks - 1);
                foreach (var bone in bones)
                {
                    var m = pose[bone.Index].ToAffineNumerics();
                    for (var row = 0; row < 4; row++)
                    {
                        for (var column = 0; column < 4; column++)
                        {
                            writer.Write(m[row, column]);
                        }
                    }
                }
            }
        }
    }

    public static void WritePets(string root, string output)
    {
        Directory.CreateDirectory(output);
        var temporary = Path.Combine(Path.GetTempPath(), $"etervale-pets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            WritePets(root, output, temporary);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static void WritePets(string root, string output, string temporary)
    {
        string[] pets = ["Pet_terry", "Pet_nepsys", "Pet_io", "Pet_mute"];
        for (var index = 0; index < pets.Length; index++)
        {
            WritePet(root, output, temporary, index + 1, $"Effect/LowLevelPet/{pets[index]}.smd", "Effect/LowLevelPet/Pet_motion.smb");
        }
        WritePet(root, output, temporary, 5, "Effect/PcBangPet/Pet_Phoenimin/Phoenix.smd", "Effect/PcBangPet/Pet_Phoenimin/Phoenix.smb");
    }

    private static void WritePet(string root, string output, string temporary, int id, string meshName, string motionName)
    {
        var mesh = AssetFiles.ResolveFile(root, meshName) ?? throw new FileNotFoundException(meshName);
        var motion = AssetFiles.ResolveFile(root, motionName) ?? throw new FileNotFoundException(motionName);
        var actor = new PristonActorLoader().LoadSkinned(mesh, motion);
        var options = new ExportOptions(FlipWinding: false, FlipV: false, StageObject: true);
        var materials = new MaterialFactory(Path.GetDirectoryName(mesh)!, temporary, forceDoubleSided: true);
        var textures = new List<byte[]>();
        var gltf = GltfSceneExport.ToModel(GltfSceneExport.BuildActor(actor, materials, options), options);
        var model = MobileCharacterSelectSceneBake.Reload(gltf, temporary, textures);
        const string clip = "Animation";
        var lastTick = actor.Skeleton.LastFrame;
        var startTick = GameUnits.FrameTicks;
        var phoenix = id == 5;
        var idle = BakeRange(phoenix ? 10 : 80, phoenix ? 74 : 230);
        var fly = BakeRange(phoenix ? 80 : 10, phoenix ? 145 : 70);
        WritePart(Path.Combine(output, $"pet-{id}.bin"), textures,
            new MobilePart(MobileActorWriter.Batches(model, clip), [idle, fly]));

        MobileClip BakeRange(int start, int end)
        {
            var seconds = (end - start) / (float)GameUnits.TicksPerSecond;
            var samples = Math.Max(2, (int)MathF.Ceiling(seconds * 24f));
            var frames = new float[samples][][];
            for (var index = 0; index < samples; index++)
            {
                var tick = (start + (end - start) * index / (float)samples) * GameUnits.FrameTicks;
                var phase = Math.Clamp((tick - startTick) / (lastTick - startTick), 0f, 1f);
                frames[index] = model.Bake(clip, phase)
                    .Select(batch => MobileActorWriter.Channel(batch.Vertices, 0, 3)).ToArray();
            }
            return new MobileClip(seconds, frames);
        }
    }

    private static void WritePart(string output, List<byte[]> textures, MobilePart part)
    {
        using var writer = new BinaryWriter(File.Create(output));
        MobileActorWriter.WriteTextures(writer, textures);
        MobileActorWriter.WritePart(writer, part);
    }
}
