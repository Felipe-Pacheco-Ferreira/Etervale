namespace PristonTale.Mobile.CharacterSelect;

// The height of the baked stage's surface under a planar point: the highest one at or below a probe height. Answers
// are kept, since the screen asks about the same few standing places every frame.
internal sealed class StageFloor(SceneBatch[] stage)
{
    private const int FloatsPerVertex = 8;
    private const int MostKept = 256;

    private readonly Dictionary<(float X, float Z), (bool Found, float Y)> _kept = new();

    public bool TryHeight(float x, float z, float probeY, out float height)
    {
        if (!_kept.TryGetValue((x, z), out var kept))
        {
            kept = Find(x, z, probeY);
            if (_kept.Count >= MostKept)
            {
                _kept.Clear();
            }

            _kept[(x, z)] = kept;
        }

        height = kept.Y;
        return kept.Found;
    }

    private (bool Found, float Y) Find(float x, float z, float probeY)
    {
        var found = false;
        var best = float.MinValue;
        foreach (var batch in stage)
        {
            var vertices = batch.Vertices;
            for (var offset = 0; offset + 3 * FloatsPerVertex <= vertices.Length; offset += 3 * FloatsPerVertex)
            {
                var (ax, ay, az) = (vertices[offset], vertices[offset + 1], vertices[offset + 2]);
                var (bx, by, bz) = (vertices[offset + FloatsPerVertex], vertices[offset + FloatsPerVertex + 1], vertices[offset + FloatsPerVertex + 2]);
                var (cx, cy, cz) = (vertices[offset + 2 * FloatsPerVertex], vertices[offset + 2 * FloatsPerVertex + 1], vertices[offset + 2 * FloatsPerVertex + 2]);
                var area = (bx - ax) * (cz - az) - (cx - ax) * (bz - az);
                if (MathF.Abs(area) < 1e-6f)
                {
                    continue;
                }

                var u = ((x - ax) * (cz - az) - (cx - ax) * (z - az)) / area;
                var v = ((bx - ax) * (z - az) - (x - ax) * (bz - az)) / area;
                if (u < 0f || v < 0f || u + v > 1f)
                {
                    continue;
                }

                var y = ay + u * (by - ay) + v * (cy - ay);
                if (y <= probeY && y > best)
                {
                    best = y;
                    found = true;
                }
            }
        }

        return (found, found ? best : 0f);
    }
}
