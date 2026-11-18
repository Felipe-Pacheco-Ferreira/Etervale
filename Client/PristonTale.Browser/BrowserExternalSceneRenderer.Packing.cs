using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Browser;

internal sealed partial class BrowserExternalSceneRenderer
{
    private void PrepareUpdates()
    {
        _actorCount = 0;
        _actorBoneCount = 0;
        _meshCount = 0;
        _meshBoneCount = 0;
        foreach (var entry in _actors.Values)
        {
            if (!entry.Dirty)
            {
                continue;
            }
            _actorCount++;
            if (entry.PoseDirty)
            {
                _actorBoneCount += entry.Bones.Length;
            }
        }
        foreach (var entry in _meshes.Values)
        {
            if (!entry.Dirty)
            {
                continue;
            }
            _meshCount++;
            _meshBoneCount += entry.Bones.Length;
        }
        EnsureCapacity(ref _actorMeta, _actorCount * 4);
        EnsureCapacity(ref _actorStates, _actorCount * ActorStride);
        EnsureCapacity(ref _actorBones, _actorBoneCount);
        EnsureCapacity(ref _meshMeta, _meshCount * 3);
        EnsureCapacity(ref _meshBones, _meshBoneCount);
        var actorIndex = 0;
        var boneIndex = 0;
        foreach (var entry in _actors.Values)
        {
            if (!entry.Dirty)
            {
                continue;
            }
            var meta = actorIndex * 4;
            _actorMeta[meta] = entry.Id;
            _actorMeta[meta + 1] = boneIndex;
            _actorMeta[meta + 2] = entry.PoseDirty ? entry.Bones.Length : 0;
            _actorMeta[meta + 3] = unchecked((int)entry.State.PoseRevision);
            if (entry.PoseDirty)
            {
                entry.Bones.CopyTo(_actorBones, boneIndex);
                boneIndex += entry.Bones.Length;
            }
            PackActor(entry.State, entry.OverlayTexture, _actorStates.AsSpan(actorIndex * ActorStride, ActorStride));
            actorIndex++;
        }
        var meshIndex = 0;
        boneIndex = 0;
        foreach (var pair in _meshes)
        {
            if (!pair.Value.Dirty)
            {
                continue;
            }
            var meta = meshIndex++ * 3;
            _meshMeta[meta] = pair.Key;
            _meshMeta[meta + 1] = boneIndex;
            _meshMeta[meta + 2] = pair.Value.Bones.Length;
            pair.Value.Bones.CopyTo(_meshBones, boneIndex);
            boneIndex += pair.Value.Bones.Length;
        }
    }

    private void CompleteUpdates()
    {
        foreach (var entry in _actors.Values)
        {
            entry.Dirty = false;
            entry.PoseDirty = false;
        }
        foreach (var entry in _meshes.Values)
        {
            entry.Dirty = false;
        }
    }

    internal static void PackActor(in ExternalActorState state, int overlayTexture, Span<float> destination)
    {
        destination[0] = state.Visible ? 1f : 0f;
        PackVector(destination, 1, state.ColorAdd);
        destination[4] = state.Outline.X;
        destination[5] = state.Outline.Y;
        destination[6] = state.Outline.Z;
        destination[7] = state.Outline.W;
        PackVector(destination, 8, state.Tint);
        destination[11] = state.Opacity;
        destination[12] = state.CastsShadow ? 1f : 0f;
        destination[13] = state.Wobble ? 1f : 0f;
        destination[14] = state.Scale;
        destination[15] = state.FloorLight;
        PackVector(destination, 16, state.Bounds.Min);
        PackVector(destination, 19, state.Bounds.Max);
        destination[22] = overlayTexture;
        destination[23] = state.OverlayForm;
    }

    internal static void PackFrame(in ExternalSceneFrame frame, Span<float> destination)
    {
        PackMatrix(destination, 0, frame.View);
        PackMatrix(destination, 16, frame.Projection);
        PackVector(destination, 32, frame.CameraPosition);
        PackVector(destination, 35, frame.CameraTarget);
        PackVector(destination, 38, frame.Ambient);
        PackVector(destination, 41, frame.GroundAmbient);
        PackVector(destination, 44, frame.SunDirection);
        PackVector(destination, 47, frame.SunColor);
        destination[50] = frame.Background.R / 255f;
        destination[51] = frame.Background.G / 255f;
        destination[52] = frame.Background.B / 255f;
        destination[53] = frame.Background.A / 255f;
        destination[54] = (float)frame.Fog.Mode;
        destination[55] = frame.Fog.StartDistance;
        destination[56] = frame.Fog.Density;
        destination[57] = frame.Fog.MaxOpacity;
        destination[58] = frame.Fog.InScatter;
        destination[59] = frame.Fog.InScatterAnisotropy;
        PackVector(destination, 60, frame.Fog.Color);
        destination[63] = frame.TimeMilliseconds / 1000f;
        destination[64] = frame.DeltaSeconds;
        destination[65] = frame.Underground ? 1f : 0f;
        destination[66] = frame.ShadowsEnabled ? 1f : 0f;
        destination[67] = frame.PointLights.Count;
        for (var index = 0; index < frame.PointLights.Count; index++)
        {
            var light = frame.PointLights[index];
            var at = FrameHeader + index * 8;
            PackVector(destination, at, light.Position);
            destination[at + 3] = light.Radius;
            PackVector(destination, at + 4, light.Color);
            destination[at + 7] = light.Intensity;
        }
    }

    internal static void PackMatrix(Span<float> destination, int at, Matrix matrix)
    {
        // XNA row-vector fields become the equivalent Three column-vector matrix without a second transpose.
        destination[at] = matrix.M11;
        destination[at + 1] = matrix.M12;
        destination[at + 2] = matrix.M13;
        destination[at + 3] = matrix.M14;
        destination[at + 4] = matrix.M21;
        destination[at + 5] = matrix.M22;
        destination[at + 6] = matrix.M23;
        destination[at + 7] = matrix.M24;
        destination[at + 8] = matrix.M31;
        destination[at + 9] = matrix.M32;
        destination[at + 10] = matrix.M33;
        destination[at + 11] = matrix.M34;
        destination[at + 12] = matrix.M41;
        destination[at + 13] = matrix.M42;
        destination[at + 14] = matrix.M43;
        destination[at + 15] = matrix.M44;
    }

    private static void PackVector(Span<float> destination, int at, Vector3 vector)
    {
        destination[at] = vector.X;
        destination[at + 1] = vector.Y;
        destination[at + 2] = vector.Z;
    }
}
