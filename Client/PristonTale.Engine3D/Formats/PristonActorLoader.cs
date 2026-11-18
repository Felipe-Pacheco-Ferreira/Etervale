using PristonTale.Engine3D.IO;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Units;
using PristonTale.Assets;
using PristonTale.Engine3D.Rendering.Gltf;

namespace PristonTale.Engine3D.Formats;

public sealed class PristonActorLoader
{
    private readonly PristonInxReader _inxReader;
    private readonly PristonSmdReader _smdReader;
    private readonly bool _previewMotionsOnly;

    public PristonActorLoader() : this(null) { }

    public PristonActorLoader(PristonAssetCache cache) : this(cache, false) { }

    public PristonActorLoader(PristonAssetCache cache, bool previewMotionsOnly)
    {
        _inxReader = cache != null ? new PristonInxReader(cache) : new PristonInxReader();
        _smdReader = cache != null ? new PristonSmdReader(cache) : new PristonSmdReader();
        _previewMotionsOnly = previewMotionsOnly;
    }

    // infoPath: the model info (.inx), or any name of it (PristonModelPaths.Info). Its model resolves through
    // PristonModelPaths.Mesh: a glTF model is drawn with its own clips, a legacy one with its motion file's skeleton.
    public PristonActor Load(string assetRoot, string infoPath)
    {
        var info = _inxReader.Read(infoPath);
        var model = ResolveCachePath(PristonModelPaths.Mesh(assetRoot, RequirePath(info.ModelFile)), info.ModelFile);
        if (PristonModelPaths.IsGltf(model))
        {
            var source = GltfActorSource.Load(model);
            return new PristonActor(info, source.Mesh, source.Skeleton, []) { Gltf = source };
        }

        var motionInfo = ResolveMotionInfo(assetRoot, info);
        var motionPath = ResolveMotionPath(assetRoot, motionInfo);
        var skeleton = _previewMotionsOnly && Path.GetExtension(motionPath).Equals(".smb", StringComparison.OrdinalIgnoreCase)
            && !motionPath.EndsWith(PristonSmdReader.PreviewSkeletonSuffix, StringComparison.OrdinalIgnoreCase)
            ? _smdReader.ReadPreviewSkeleton(motionPath, motionInfo)
            : _smdReader.ReadModel(motionPath);
        var mesh = PristonPhysiqueRepair.Repair(_smdReader.ReadModel(model), skeleton);
        // Shared armor meshes require the INX high-LOD filter; models without groups render all objects.
        var renderObjectNames = SelectRenderObjectNames(info);
        return new PristonActor(motionInfo, mesh, skeleton, renderObjectNames);
    }

    public PristonActor LoadSkinned(string meshSmdPath, string skeletonSmbPath)
    {
        var skeleton = _smdReader.ReadModel(skeletonSmbPath);
        var mesh = PristonPhysiqueRepair.Repair(_smdReader.ReadModel(meshSmdPath), skeleton);
        return new PristonActor(CreateSyntheticIdleInfo(skeleton.LastFrame), mesh, skeleton, []);
    }

    // A glTF model without a model-info file: its first clip loops as Stand (the rest pose without clips).
    public static PristonActor LoadGltf(string path)
    {
        var source = GltfActorSource.Load(path);
        var end = source.Skeleton.FrameRanges.Count > 0 ? source.Skeleton.FrameRanges[0].EndFrame : GameUnits.FrameTicks;
        return new PristonActor(CreateSyntheticIdleInfo(end), source.Mesh, source.Skeleton, []) { Gltf = source };
    }

    public PristonActor LoadRigid(string smdPath)
    {
        var mesh = _smdReader.ReadModel(smdPath);
        return new PristonActor(CreateSyntheticIdleInfo(GameUnits.FrameTicks), mesh, mesh, []);
    }

    // Synthetic opening actors need a non-empty Stand motion spanning the full skeleton clip.
    private static PristonModelInfo CreateSyntheticIdleInfo(int skeletonLastFrameTicks)
    {
        var endFrame = (uint)Math.Max(1, skeletonLastFrameTicks / GameUnits.FrameTicks);
        var idle = new PristonMotionInfo(
            State: (uint)MotionCode.Stand,
            StartFrame: 0,
            EndFrame: endFrame,
            EventFrames: [],
            ItemCodeCount: 0,
            JobCodeBit: 0,
            MapPosition: 0,
            Repeat: 1,
            KeyCode: 0,
            MotionFrame: 0);

        var motions = new PristonMotionInfo[PristonFormatConstants.CharacterMotionBase + 1];
        for (var i = 0; i < motions.Length; i++)
            motions[i] = idle;

        var empty = new PristonModelGroup(0, []);
        return new PristonModelInfo("", "", "", "", empty, empty, empty,
            motions, (uint)motions.Length, "", "", "", [], 0);
    }

    private static IReadOnlyList<string> SelectRenderObjectNames(PristonModelInfo info)
    {
        var high = NonEmptyNames(info.HighModel);
        if (high.Count > 0)
        {
            return high;
        }

        return NonEmptyNames(info.DefaultModel);
    }

    private static List<string> NonEmptyNames(PristonModelGroup group)
    {
        var names = new List<string>(group.ModelNames.Count);
        foreach (var name in group.ModelNames)
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        return names;
    }

    // A motion file (.smb), or the motion file of the model info its name links to (an .in link names one).
    private string ResolveMotionPath(string assetRoot, PristonModelInfo info)
    {
        var sourcePath = info.MotionFile;
        var direct = PristonModelPaths.Motion(assetRoot, RequirePath(sourcePath));
        if (PreviewMotionPath(info, direct) is { } preview)
            return preview;

        if (File.Exists(direct))
            return direct;

        if (LinkedInfo(assetRoot, sourcePath) is { } link && !string.IsNullOrWhiteSpace(link.MotionFile))
        {
            var linked = PristonModelPaths.Motion(assetRoot, link.MotionFile);
            return PreviewMotionPath(info, linked) ?? ResolveCachePath(linked, link.MotionFile);
        }

        return ResolveCachePath(direct, sourcePath);
    }

    private string PreviewMotionPath(PristonModelInfo info, string fullPath)
    {
        if (!_previewMotionsOnly || !Path.GetExtension(fullPath).Equals(".smb", StringComparison.OrdinalIgnoreCase))
            return null;

        // Each published companion is made with the corresponding m#Bip motion table. A different INX can describe
        // different ranges of the same skeleton, so it still requires the original motion asset.
        var name = Path.GetFileNameWithoutExtension(fullPath);
        if (name.Length != 2 || name[0] is not ('m' or 'M') || name[1] is < '1' or > '8'
            || !Path.GetFileNameWithoutExtension(info.Path).Equals(name + "Bip", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var preview = PristonModelPaths.WithExtension(fullPath, PristonSmdReader.PreviewSkeletonSuffix);
        var resolved = AssetFiles.Resolve(preview);
        return File.Exists(resolved) ? resolved : null;
    }

    private PristonModelInfo ResolveMotionInfo(string assetRoot, PristonModelInfo visualInfo)
    {
        if (visualInfo.HasUsableMotions)
            return visualInfo;

        return LinkedInfo(assetRoot, visualInfo.MotionFile) is { HasUsableMotions: true } link ? link : visualInfo;
    }

    // The model info a name links to, when one (compiled or a descriptor) exists.
    private PristonModelInfo LinkedInfo(string assetRoot, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        var path = PristonModelPaths.Info(AssetFiles.Combine(assetRoot, reference));
        return File.Exists(path) ? _inxReader.Read(path) : null;
    }

    private static string RequirePath(string reference) =>
        string.IsNullOrWhiteSpace(reference) ? throw new InvalidDataException("Empty Priston actor source path.") : reference;

    private static string ResolveCachePath(string resolved, string reference) =>
        File.Exists(resolved) ? resolved : throw new FileNotFoundException($"Cannot resolve Priston actor file '{reference}'.", resolved);
}
