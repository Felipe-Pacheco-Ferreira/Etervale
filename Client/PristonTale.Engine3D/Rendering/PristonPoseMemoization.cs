using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonPoseMemoization
{
    internal static FrameTiming ProfileTiming { get; set; }

    private readonly Dictionary<PristonModel, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly List<Entry> _active = [];

    internal int Count => _entries.Count;

    internal Entry Retain(PristonModel model)
    {
        if (!_entries.TryGetValue(model, out var entry))
        {
            entry = new Entry(model);
            _entries.Add(model, entry);
            _active.Add(entry);
        }
        entry.References++;
        return entry;
    }

    internal void Release(Entry entry)
    {
        if (--entry.References == 0)
        {
            _entries.Remove(entry.Model);
            _active.Remove(entry);
            entry.Valid = false;
        }
    }

    internal void Invalidate()
    {
        for (var index = 0; index < _active.Count; index++)
        {
            _active[index].Valid = false;
        }
    }

    internal void Clear()
    {
        Invalidate();
        _entries.Clear();
        _active.Clear();
    }

    internal sealed class Entry(PristonModel model)
    {
        internal PristonModel Model { get; } = model;
        private readonly Matrix[] _raw = new Matrix[model.Objects.Count];
        private int _frame;
        private int _rotationEnd;
        internal int References;
        internal bool Valid;

        internal bool Copy(float frame, float rotationEnd, Matrix[] destination)
        {
            var hit = Valid && float.IsFinite(frame) && !float.IsNaN(rotationEnd)
                && _frame == BitConverter.SingleToInt32Bits(frame) && _rotationEnd == BitConverter.SingleToInt32Bits(rotationEnd);
            ProfileTiming?.RecordPoseMemoization(hit);
            if (hit)
            {
                _raw.CopyTo(destination, 0);
            }
            return hit;
        }

        internal void Store(float frame, float rotationEnd, Matrix[] raw)
        {
            raw.CopyTo(_raw, 0);
            _frame = BitConverter.SingleToInt32Bits(frame);
            _rotationEnd = BitConverter.SingleToInt32Bits(rotationEnd);
            Valid = float.IsFinite(frame) && !float.IsNaN(rotationEnd);
        }
    }
}
