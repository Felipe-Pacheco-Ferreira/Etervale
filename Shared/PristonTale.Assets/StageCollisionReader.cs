using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using PristonTale.Contracts.Units;
using PristonTale.Geometry;

namespace PristonTale.Assets;

// Reads only what collision needs from a stage SMD: the file header, the stage header counts, each material record's
// MeshState and the stage vertices and faces. Positions are Priston world units
// (fixed point / 256) in the file's own axes, which are the server's coordinates.
public static class StageCollisionReader
{
    // Check-face flag: faces of materials with this MeshState bit block and support characters.
    public const uint CheckFace = 0x00000001;

    private const int StageCountsOffset = 4 + SmdLayout.AreaWidth * SmdLayout.AreaWidth * 4 + 12 * 4;
    // Material record: 104 bytes of texture slots and states, eleven scalars and the diffuse colour, then UseState at 164.
    private const int MaterialMeshStateOffset = 168;

    public readonly record struct StageLamp(Vector3 Position, float Range, Vector3 Color);

    // A masked stage (AssetMask) is unmasked in place.
    public static CollisionMesh Read(byte[] data) => new(ReadTriangles(data));

    public static IReadOnlyList<Triangle> ReadTriangles(byte[] data) => ReadTriangles(data, out _, includeLamps: false);

    public static IReadOnlyList<Triangle> ReadTriangles(byte[] data, out StageLamp[] lamps) =>
        ReadTriangles(data, out lamps, includeLamps: true);

    private static IReadOnlyList<Triangle> ReadTriangles(byte[] data, out StageLamp[] lamps, bool includeLamps)
    {
        if (data.Length < SmdLayout.FileHeaderSize + SmdLayout.StageHeaderSize)
        {
            throw new InvalidDataException("Stage SMD is shorter than its headers.");
        }

        if (AssetMask.IsMaskedSmd(data))
        {
            AssetMask.Unmask(data);
        }

        var signature = Encoding.ASCII.GetString(data, 0, SmdLayout.SignatureSize).TrimEnd('\0');
        if (signature is not (SmdLayout.StageSignature or SmdLayout.StageSignature73))
        {
            throw new InvalidDataException($"Not a stage SMD: '{signature}'.");
        }

        var materialCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(28));
        var counts = data.AsSpan(SmdLayout.FileHeaderSize + StageCountsOffset);
        var vertexCount = BinaryPrimitives.ReadInt32LittleEndian(counts);
        var faceCount = BinaryPrimitives.ReadInt32LittleEndian(counts[4..]);
        if (vertexCount < 0 || faceCount < 0 || materialCount < 0 || materialCount > 10_000)
        {
            throw new InvalidDataException("Stage SMD has invalid counts.");
        }

        var offset = SmdLayout.FileHeaderSize + SmdLayout.StageHeaderSize;
        var checkFace = new bool[materialCount];
        if (materialCount > 0)
        {
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(Slice(data, offset + 8, 4));
            if (declared != materialCount)
            {
                throw new InvalidDataException($"Stage SMD declares {materialCount} materials but its group holds {declared}.");
            }

            offset += SmdLayout.MaterialGroupSize;
            for (var index = 0; index < materialCount; index++)
            {
                var material = Slice(data, offset, SmdLayout.MaterialSize);
                var inUse = BinaryPrimitives.ReadUInt32LittleEndian(material) != 0;
                checkFace[index] = (BinaryPrimitives.ReadUInt32LittleEndian(material[MaterialMeshStateOffset..]) & CheckFace) != 0;
                offset += SmdLayout.MaterialSize;
                if (inUse)
                {
                    // In-use materials are followed by a length-prefixed table of texture names.
                    var tableLength = BinaryPrimitives.ReadInt32LittleEndian(Slice(data, offset, 4));
                    if (tableLength < 0)
                    {
                        throw new InvalidDataException("Stage SMD material string table has a negative length.");
                    }

                    offset += 4 + tableLength;
                }
            }
        }

        var vertices = new Vector3[vertexCount];
        for (var index = 0; index < vertexCount; index++)
        {
            var vertex = Slice(data, offset + index * SmdLayout.StageVertexSize, SmdLayout.StageVertexSize);
            vertices[index] = new Vector3(
                BinaryPrimitives.ReadInt32LittleEndian(vertex[8..]) / GameUnits.FixedScale,
                BinaryPrimitives.ReadInt32LittleEndian(vertex[12..]) / GameUnits.FixedScale,
                BinaryPrimitives.ReadInt32LittleEndian(vertex[16..]) / GameUnits.FixedScale);
        }

        offset += vertexCount * SmdLayout.StageVertexSize;
        var triangles = new List<Triangle>(faceCount);
        for (var index = 0; index < faceCount; index++)
        {
            var face = Slice(data, offset + index * SmdLayout.StageFaceSize, SmdLayout.StageFaceSize);
            int a = BinaryPrimitives.ReadUInt16LittleEndian(face[8..]);
            int b = BinaryPrimitives.ReadUInt16LittleEndian(face[10..]);
            int c = BinaryPrimitives.ReadUInt16LittleEndian(face[12..]);
            int material = BinaryPrimitives.ReadUInt16LittleEndian(face[14..]);
            if (a < vertexCount && b < vertexCount && c < vertexCount && material < materialCount && checkFace[material])
            {
                triangles.Add(new Triangle(vertices[a], vertices[b], vertices[c]));
            }
        }

        lamps = includeLamps
            ? ReadLamps(data, counts, checked(offset + faceCount * SmdLayout.StageFaceSize))
            : [];
        return triangles;
    }

    private static StageLamp[] ReadLamps(byte[] data, ReadOnlySpan<byte> counts, int offset)
    {
        const int textureLinkSize = 32;
        const int lightSize = 28;
        const int lensFlag = 0x2;
        const int dynamicFlag = 0x80000;
        var linkCount = BinaryPrimitives.ReadInt32LittleEndian(counts[8..]);
        var lightCount = BinaryPrimitives.ReadInt32LittleEndian(counts[12..]);
        if (linkCount < 0 || lightCount < 0)
        {
            throw new InvalidDataException("Stage SMD has invalid light or texture link counts.");
        }

        offset = checked(offset + linkCount * textureLinkSize);
        var records = Slice(data, offset, checked(lightCount * lightSize));
        var lamps = new List<StageLamp>(lightCount);
        for (var index = 0; index < lightCount; index++)
        {
            var record = records[(index * lightSize)..];
            var flags = BinaryPrimitives.ReadInt32LittleEndian(record);
            if ((flags & lensFlag) != 0 && (flags & dynamicFlag) == 0)
            {
                continue;
            }

            var position = new Vector3(WorldUnits(record[4..]), WorldUnits(record[8..]), WorldUnits(record[12..]));
            var range = WorldUnits(record[16..]);
            var color = new Vector3(ColorChannel(record[20..]), ColorChannel(record[22..]), ColorChannel(record[24..]));
            lamps.Add(new StageLamp(position, range > 1f ? range : 600f, color));
        }
        return lamps.ToArray();
    }

    private static float WorldUnits(ReadOnlySpan<byte> bytes) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes) / GameUnits.FixedScale;

    private static float ColorChannel(ReadOnlySpan<byte> bytes) =>
        Math.Clamp((int)BinaryPrimitives.ReadInt16LittleEndian(bytes), 0, 255) / 255f;

    private static ReadOnlySpan<byte> Slice(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("Stage SMD ends inside a record.");
        }

        return data.AsSpan(offset, length);
    }
}
