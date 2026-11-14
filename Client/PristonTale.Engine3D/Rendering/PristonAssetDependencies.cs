using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

// A file a stage or a model reads, by the same readers, material selection and texture resolution the renderer uses.
// Path is the file on disk, or empty for a texture that resolves to no file; Opacity marks an opacity map.
public readonly record struct AssetDependency(string Path, bool Opacity);

public static class PristonAssetDependencies
{
    // The textures a field's stage and its stage objects draw.
    public static List<AssetDependency> StageTextures(string root, string stageSmd, IReadOnlyList<string> objectSmds)
    {
        var reader = new PristonSmdReader();
        var found = new List<AssetDependency>();
        var stagePath = PristonTale.Assets.AssetFiles.Resolve(Path.Combine(root, stageSmd));
        Textures(Path.GetDirectoryName(stagePath)!, PristonMaterialUsage.ForStage(reader.ReadStage(stagePath)), found);
        foreach (var model in objectSmds)
        {
            var modelPath = PristonTale.Assets.AssetFiles.Resolve(Path.Combine(root, model));
            Textures(Path.GetDirectoryName(modelPath)!, PristonMaterialUsage.ForModel(reader.ReadModel(modelPath)), found);
        }

        return found;
    }

    // Every file a field's stage reads: the stage, its objects and their textures.
    public static List<AssetDependency> StageFiles(string root, string stageSmd, IReadOnlyList<string> objectSmds)
    {
        var found = new List<AssetDependency> { new(PristonTale.Assets.AssetFiles.Resolve(Path.Combine(root, stageSmd)), false) };
        foreach (var model in objectSmds)
        {
            found.Add(new AssetDependency(PristonTale.Assets.AssetFiles.Resolve(Path.Combine(root, model)), false));
        }

        found.AddRange(StageTextures(root, stageSmd, objectSmds));
        return found;
    }

    // Every file an actor reads from its model info (infoPath, on disk): the model info, the motion model info it links to,
    // the mesh, the skeleton, the textures the drawn objects use, and the same for its sub model.
    public static List<AssetDependency> ActorFiles(string root, string infoPath)
    {
        var found = new List<AssetDependency>();
        var loader = new PristonActorLoader();
        var reader = new PristonInxReader();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(infoPath);
        while (pending.Count > 0)
        {
            var path = pending.Pop();
            if (!seen.Add(path))
            {
                continue;
            }

            var visual = reader.Read(path);
            var actor = loader.Load(root, path);
            found.Add(new AssetDependency(path, false));
            var mesh = PristonModelPaths.Mesh(root, visual.ModelFile);
            found.Add(new AssetDependency(mesh, false));
            if (actor.Gltf is null)
            {
                Add(actor.Info.Path);
                Add(actor.Skeleton.Path);
                Textures(Path.GetDirectoryName(mesh)!, PristonMaterialUsage.ForActor(actor), found);
            }

            if (PristonModelPaths.SubModelInfo(root, visual) is { } sub)
            {
                pending.Push(sub);
            }
        }

        return found;

        void Add(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                found.Add(new AssetDependency(path, false));
            }
        }
    }

    private static void Textures(string directory, IEnumerable<PristonMaterial> materials, List<AssetDependency> found)
    {
        foreach (var request in PristonTextureLoader.TextureRequests(materials))
        {
            found.Add(new AssetDependency(PristonTextureLoader.ResolvePath(directory, request.Name), false));
            if (!string.IsNullOrWhiteSpace(request.Opacity))
            {
                found.Add(new AssetDependency(PristonTextureLoader.ResolvePath(directory, request.Opacity), true));
            }
        }
    }
}
