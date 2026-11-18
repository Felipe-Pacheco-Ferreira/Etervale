using System.Buffers.Binary;
using PristonTale.Assets;

namespace PristonTale.Engine3D.Formats;

public sealed partial class PristonSmdReader
{
    internal const string PreviewSkeletonSuffix = ".preview.smb";
    private const int GeometryCountsOffset = 84;
    private const int PhysiqueFlagOffset = 16;
    private const int SegmentEntrySize = sizeof(int) * 4;
    private const int SegmentTableSize = MotionSegmentTableEntries * SegmentEntrySize;
    private const int KeyCountsOffset = Obj3DSize - sizeof(int) - 3 * SegmentTableSize - 3 * sizeof(int);

    // Write the legacy SMB layout with preview key windows only. The original indices and frame offsets in the INX
    // and file header remain usable. Each object's individual segment table is remapped to its compact key arrays.
    internal static void ExportPreviewSkeleton(string sourcePath, PristonModelInfo motions, Stream destination)
    {
        var data = AssetMask.ReadSmd(sourcePath);
        var cursor = new BinaryCursor(data);
        var header = ReadHeader(cursor);
        ValidateModelHeader(data, header, sourcePath);
        if (!IsModelHeader(header.Kind) || header.MaterialCount != 0 || header.FrameRangeCount == 0)
        {
            throw new InvalidDataException($"Cannot compact '{sourcePath}' as a character motion skeleton.");
        }

        var previewFrames = PreviewFrames(motions, EffectiveFrameRanges(header));
        var filePrefix = data.AsSpan(0, header.FirstObjectOffset).ToArray();
        var sourceEnd = header.FirstObjectOffset;
        destination.Write(filePrefix);
        for (var index = 0; index < header.ObjectCount; index++)
        {
            var entryOffset = FileHeaderSize + index * ObjectInfoSize;
            cursor.Position = entryOffset;
            var info = ReadObjectInfo(cursor);
            var outputStart = checked((int)destination.Position);
            WritePreviewObject(data, cursor, info, sourcePath, previewFrames, destination);
            var outputLength = checked((int)destination.Position - outputStart);
            BinaryPrimitives.WriteInt32LittleEndian(filePrefix.AsSpan(entryOffset + 32), outputLength);
            BinaryPrimitives.WriteInt32LittleEndian(filePrefix.AsSpan(entryOffset + 36), outputStart);
            sourceEnd = checked(info.Offset + info.Length);
        }

        destination.Write(data.AsSpan(sourceEnd));
        var outputEnd = destination.Position;
        destination.Position = 0;
        destination.Write(filePrefix);
        destination.Position = outputEnd;
    }

    private static void WritePreviewObject(byte[] data, BinaryCursor cursor, ObjectInfo info, string sourcePath,
        PreviewInterval[] frames, Stream destination)
    {
        cursor.Position = info.Offset + PhysiqueFlagOffset;
        var hasPhysique = cursor.UInt32() != 0;
        cursor.Position = info.Offset + GeometryCountsOffset;
        var vertexCount = cursor.Int32();
        var faceCount = cursor.Int32();
        var linkCount = cursor.Int32();
        cursor.Position = info.Offset + KeyCountsOffset;
        var rotationCount = cursor.Int32();
        var positionCount = cursor.Int32();
        var scaleCount = cursor.Int32();
        var rotationSegments = ReadMotionSegments(cursor);
        var positionSegments = ReadMotionSegments(cursor);
        var scaleSegments = ReadMotionSegments(cursor);
        var segmentCount = cursor.Int32();
        if (segmentCount is < 0 or > MotionSegmentTableEntries)
        {
            throw new InvalidDataException($"{sourcePath}:{info.Name} has invalid TmFrameCnt {segmentCount}.");
        }

        ValidateCount(vertexCount, $"{sourcePath}:{info.Name} vertices");
        ValidateCount(faceCount, $"{sourcePath}:{info.Name} faces");
        ValidateCount(linkCount, $"{sourcePath}:{info.Name} texture links");
        ValidateCount(rotationCount, $"{sourcePath}:{info.Name} rotation keys");
        ValidateCount(positionCount, $"{sourcePath}:{info.Name} position keys");
        ValidateCount(scaleCount, $"{sourcePath}:{info.Name} scale keys");
        ValidateObjectLength(sourcePath, info, vertexCount, faceCount, linkCount, rotationCount, positionCount, scaleCount, hasPhysique);
        if (segmentCount == 0)
        {
            destination.Write(data.AsSpan(info.Offset, info.Length));
            return;
        }

        var objectPrefixSize = checked(Obj3DSize + vertexCount * ModelVertexSize + faceCount * ModelFaceSize + linkCount * TexLinkSize);
        var objectPrefix = data.AsSpan(info.Offset, objectPrefixSize).ToArray();
        cursor.Position = info.Offset + objectPrefixSize;
        var rotation = PlanPreviewSeries(cursor, rotationCount, RotationKeySize, frames, rotationSegments, segmentCount);
        var position = PlanPreviewSeries(cursor, positionCount, PositionKeySize, frames, positionSegments, segmentCount);
        var scale = PlanPreviewSeries(cursor, scaleCount, ScaleKeySize, frames, scaleSegments, segmentCount);
        var previousRotationStart = cursor.Position;
        cursor.Position = checked(cursor.Position + rotationCount * MatrixSize);

        WritePreviewChannelHeader(objectPrefix, 0, rotation.Plan);
        WritePreviewChannelHeader(objectPrefix, 1, position.Plan);
        WritePreviewChannelHeader(objectPrefix, 2, scale.Plan);
        destination.Write(objectPrefix);
        CopyPreviewSeries(destination, data, rotation.Start, RotationKeySize, rotation.Plan);
        CopyPreviewSeries(destination, data, position.Start, PositionKeySize, position.Plan);
        CopyPreviewSeries(destination, data, scale.Start, ScaleKeySize, scale.Plan);
        CopyPreviewSeries(destination, data, previousRotationStart, MatrixSize, rotation.Plan);
        destination.Write(data.AsSpan(cursor.Position, checked(info.Offset + info.Length - cursor.Position)));
    }

    private static (int Start, PreviewKeyPlan Plan) PlanPreviewSeries(BinaryCursor cursor, int count, int size,
        PreviewInterval[] frames, PristonMotionSegment[] segments, int segmentCount)
    {
        var start = cursor.Position;
        var plan = PreviewKeys(cursor, count, size, frames, SliceSegments(segments, segmentCount));
        cursor.Position = checked(start + count * size);
        return (start, plan);
    }

    private static void WritePreviewChannelHeader(Span<byte> header, int channel, PreviewKeyPlan plan)
    {
        BinaryPrimitives.WriteInt32LittleEndian(header[(KeyCountsOffset + channel * sizeof(int))..], plan.Count);
        var segmentBytes = header.Slice(KeyCountsOffset + sizeof(int) * 3 + channel * SegmentTableSize, SegmentTableSize);
        segmentBytes.Clear();
        for (var index = 0; index < plan.Segments.Length; index++)
        {
            var segment = plan.Segments[index];
            var record = segmentBytes.Slice(index * SegmentEntrySize, SegmentEntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(record, segment.StartFrame);
            BinaryPrimitives.WriteInt32LittleEndian(record[4..], segment.EndFrame);
            BinaryPrimitives.WriteInt32LittleEndian(record[8..], segment.KeyIndex);
            BinaryPrimitives.WriteInt32LittleEndian(record[12..], segment.KeyCount);
        }
    }

    private static void CopyPreviewSeries(Stream destination, byte[] source, int start, int size, PreviewKeyPlan plan)
    {
        foreach (var range in plan.Ranges)
        {
            destination.Write(source.AsSpan(start + range.Start * size, range.Count * size));
        }
    }
}
