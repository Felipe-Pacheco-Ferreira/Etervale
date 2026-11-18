using System.Runtime.InteropServices.JavaScript;

namespace PristonTale.Browser;

internal sealed partial class BrowserExternalSceneRenderer
{
    private const string Module = "noxius-three-scene";

    [JSImport("createScene", Module)]
    private static partial void CreateScene(int scene);

    [JSImport("configureScene", Module)]
    private static partial void ConfigureScene(int scene, string json);

    [JSImport("createSceneTexture", Module)]
    private static partial void CreateSceneTexture(int scene, int id, int width, int height, [JSMarshalAs<JSType.MemoryView>] Span<byte> rgba);

    [JSImport("createMesh", Module)]
    private static partial void CreateMeshPacked(int scene, int id, string json, [JSMarshalAs<JSType.MemoryView>] Span<byte> vertices,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> indices);

    [JSImport("removeMesh", Module)]
    private static partial void RemoveMeshCore(int scene, int id);

    [JSImport("createStage", Module)]
    private static partial void CreateStageCore(int scene, int id, [JSMarshalAs<JSType.Array<JSType.Number>>] int[] meshes);

    [JSImport("removeStage", Module)]
    private static partial void RemoveStageCore(int scene, int id);

    [JSImport("createActor", Module)]
    private static partial void CreateActorCore(int scene, int id, [JSMarshalAs<JSType.Array<JSType.Number>>] int[] meshes, int bones);

    [JSImport("removeActor", Module)]
    private static partial void RemoveActorCore(int scene, int id);

    [JSImport("setSkyLayers", Module)]
    private static partial void SetSkyLayersCore(int scene, [JSMarshalAs<JSType.Array<JSType.Number>>] int[] current,
        [JSMarshalAs<JSType.Array<JSType.Number>>] int[] previous, double previousOpacity, double height, double brightness);

    [JSImport("setSkyParameters", Module)]
    private static partial void SetSkyParameters(int scene, double previousOpacity, double height, double brightness);

    [JSImport("flushScenePacked", Module)]
    private static partial void FlushScenePacked(int scene, [JSMarshalAs<JSType.MemoryView>] Span<byte> actorMeta,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> actorStates, [JSMarshalAs<JSType.MemoryView>] Span<byte> actorBones,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> meshMeta, [JSMarshalAs<JSType.MemoryView>] Span<byte> meshBones);

    [JSImport("drawFramePacked", Module)]
    private static partial void DrawFramePacked(int scene, [JSMarshalAs<JSType.MemoryView>] Span<byte> frame, [JSMarshalAs<JSType.MemoryView>] Span<byte> actorMeta,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> actorStates, [JSMarshalAs<JSType.MemoryView>] Span<byte> actorBones,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> meshMeta, [JSMarshalAs<JSType.MemoryView>] Span<byte> meshBones);

    [JSImport("drawPortrait", Module)]
    private static partial void DrawPortraitCore(int scene, [JSMarshalAs<JSType.Array<JSType.Number>>] int[] actors, [JSMarshalAs<JSType.MemoryView>] Span<byte> frame);

    [JSImport("disposeScene", Module)]
    private static partial void DisposeScene(int scene);

    [JSImport("composeScene", Module)]
    private static partial void ComposeSceneCore(int scene);
}
