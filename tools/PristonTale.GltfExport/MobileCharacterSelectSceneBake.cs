using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Engine3D.Formats;
using PristonTale.Simulation.Characters;

namespace PristonTale.GltfExport;

// Bakes the Android client's 3D data that does not belong to one field (docs/android.md, "Baked files"): the character
// selection's stage, and one character file per class holding its default body and each of its faces as animated
// parts. Body and head frames of one clip are sampled at the same phases, so the client plays any head on its body.
internal static class MobileCharacterSelectSceneBake
{
    private const float FramesPerSecond = 12f;
    private const int FewestFrames = 2;
    private const int MostFrames = 60;

    // The clips of a character part, in the order of MobileActorFormat.
    private static readonly string[] ClipNames = Clips();

    public static void Write(string assetRoot, string scenePath, string characterDirectory)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"ptgltf_select_{Environment.ProcessId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        Directory.CreateDirectory(characterDirectory);
        try
        {
            WriteStage(assetRoot, scenePath, temporary);
            var registry = new CharacterClassRegistry();
            foreach (var playerClass in CharacterSelectArt.TempskronLineup.Concat(CharacterSelectArt.MorionLineup))
            {
                WriteCharacter(assetRoot, registry.Get(playerClass).Definition, Path.Combine(characterDirectory, MobileActorFormat.CharacterFile(playerClass)),
                    temporary);
            }
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static void WriteStage(string assetRoot, string scenePath, string temporary)
    {
        var path = AssetFiles.ResolveFile(assetRoot, CharacterSelectStage.StagePath)
            ?? throw new FileNotFoundException("The login stage is missing.", CharacterSelectStage.StagePath);
        var options = new ExportOptions(FlipWinding: false, FlipV: false);
        var materials = new MaterialFactory(Path.GetDirectoryName(path)!, temporary, unlit: true);
        var textures = new List<byte[]>();
        var scene = GltfSceneExport.BuildStage(new PristonSmdReader().ReadStage(path), materials, options);
        var stage = Reload(GltfSceneExport.ToModel(scene, options), temporary, textures).Bake(null, 0f);
        using var writer = new BinaryWriter(File.Create(scenePath));
        writer.Write(MobileActorFormat.SelectSceneMagic);
        MobileActorWriter.WriteTextures(writer, textures);
        MobileActorWriter.WriteStage(writer, stage);
        Console.WriteLine($"Character select stage: {stage.Count} batches, {textures.Count} textures.");
    }

    private static void WriteCharacter(string assetRoot, CharacterClassDef definition, string outputPath, string temporary)
    {
        var textures = new List<byte[]>();
        var visuals = definition.DefaultVisuals;
        var body = BakePart(assetRoot, visuals.Body.InxExtensionPath, definition.MotionJobMask, temporary, textures, null);
        var heads = new MobilePart[CharacterFace.FaceCount];
        for (var face = 0; face < heads.Length; face++)
        {
            heads[face] = BakePart(assetRoot, CharacterFace.ResolveHeadPath(visuals, face), definition.MotionJobMask, temporary, textures, body);
        }

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.Write(MobileActorFormat.CharacterMagic);
        MobileActorWriter.WriteTextures(writer, textures);
        MobileActorWriter.WritePart(writer, body);
        writer.Write(heads.Length);
        foreach (var head in heads)
        {
            MobileActorWriter.WritePart(writer, head);
        }

        Console.WriteLine($"{definition.Class}: " + string.Join(", ",
            ClipNames.Select((name, clip) => $"{name} {body.Clips[clip]!.Seconds:0.00} s in {body.Clips[clip]!.Frames.Length} frames")));
    }

    // A head is sampled with its body's frame counts; a body chooses them from its clips' lengths.
    internal static MobilePart BakePart(string assetRoot, string relativePath, uint jobMask, string temporary, List<byte[]> textures, MobilePart? body)
    {
        var path = AssetFiles.ResolveFile(assetRoot, relativePath) ?? throw new FileNotFoundException("A character part is missing.", relativePath);
        var options = new ExportOptions(FlipWinding: false, FlipV: false, JobMask: jobMask, PlayerClips: true);
        var actor = new PristonActorLoader().Load(assetRoot, path);
        var materials = new MaterialFactory(Path.GetDirectoryName(Path.GetFullPath(actor.Mesh.Path))!, temporary, forceDoubleSided: true);
        var model = Reload(GltfSceneExport.ToModel(GltfSceneExport.BuildActor(actor, materials, options), options), temporary, textures);
        var clips = new MobileClip?[ClipNames.Length];
        for (var clip = 0; clip < clips.Length; clip++)
        {
            clips[clip] = MobileActorWriter.BakeClip(model, ClipNames[clip], FramesPerSecond, FewestFrames, MostFrames, body?.Clips[clip])
                ?? throw new InvalidDataException($"{relativePath} has no {ClipNames[clip]} motion.");
        }

        return new MobilePart(MobileActorWriter.Batches(model, GltfSceneExport.StandClip), clips);
    }

    internal static MobileSceneBake.Model Reload(SharpGLTF.Schema2.ModelRoot model, string temporary, List<byte[]> textures)
    {
        var path = Path.Combine(temporary, $"{Guid.NewGuid():N}.glb");
        model.SaveGLB(path);
        return new MobileSceneBake.Model(path, textures);
    }

    private static string[] Clips()
    {
        var names = new string[MobileActorFormat.CharacterClips];
        names[MobileActorFormat.Stand] = GltfSceneExport.StandClip;
        names[MobileActorFormat.Walk] = GltfSceneExport.WalkClip;
        names[MobileActorFormat.Run] = GltfSceneExport.RunClip;
        names[MobileActorFormat.SelectStand] = GltfSceneExport.SelectStandClip;
        return names;
    }
}
