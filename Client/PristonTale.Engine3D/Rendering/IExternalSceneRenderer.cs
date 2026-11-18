using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

public interface IExternalSceneRenderer : IDisposable
{
    void Configure(ClientSettings settings);
    Task PrepareMaterialsAsync(string directory, IEnumerable<PristonMaterial> materials, CancellationToken cancellationToken = default);
    Task PrepareTextureAsync(string path);
    void CreateMesh(int id, ExternalSceneMesh mesh);
    void RemoveMesh(int id);
    void CreateStage(int id, int[] meshIds);
    void RemoveStage(int id);
    void UpdateMeshPose(int id, ReadOnlySpan<Vector4> boneRows);
    void SetSkyLayers(int[] currentMeshes, int[] previousMeshes, float previousOpacity, float height, float brightness);
    void CreateActor(ActorHandle handle, int[] meshIds, int boneCount);
    void RemoveActor(ActorHandle handle);
    void UpdateActor(ActorHandle handle, in ExternalActorState state, ReadOnlySpan<Vector4> boneRows);
    void Draw(in ExternalSceneFrame frame);
    void Compose();
    void DrawPortrait(ReadOnlySpan<ActorHandle> handles, Matrix view, Matrix projection, Vector3 cameraPosition, in PristonPortraitLighting lighting);
}

public sealed record ExternalSceneMesh(
    string AssetDirectory,
    PristonMaterial Material,
    float[] Positions,
    float[] Normals,
    float[] Colors,
    float[] Uv,
    float[] Uv1,
    float[] Uv2,
    int[] Indices,
    float[] BoneIndices,
    int BoneCount,
    bool IsSky = false);

public readonly record struct ExternalActorState(
    bool Visible,
    Vector3 ColorAdd,
    Vector4 Outline,
    Vector3 Tint,
    float Opacity,
    bool CastsShadow,
    bool Wobble,
    float Scale,
    float FloorLight,
    string Overlay,
    uint OverlayForm,
    BoundingBox Bounds,
    uint PoseRevision);

public readonly record struct ExternalSceneFrame(
    Matrix View,
    Matrix Projection,
    Vector3 CameraPosition,
    Vector3 CameraTarget,
    Vector3 Ambient,
    Vector3 GroundAmbient,
    Vector3 SunDirection,
    Vector3 SunColor,
    IReadOnlyList<PristonPointLight> PointLights,
    Color Background,
    PristonDistanceFog Fog,
    uint TimeMilliseconds,
    float DeltaSeconds,
    bool Underground,
    // Effective directional shadow eligibility; Configure retains the user's shadow preference.
    bool ShadowsEnabled);
