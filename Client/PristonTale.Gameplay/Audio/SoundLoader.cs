namespace PristonTale.Gameplay.Audio;

// Sounds by asset-relative path, each created once from its file. Load reads and creates at once (files on disk). Get
// never waits: it returns the sound when its read has finished and otherwise starts the read and returns null, so a
// sound whose file is still downloading is skipped and a later Get on the game thread creates it. A file that cannot be
// read stays null and is reported once.
internal sealed class SoundLoader<T> where T : class
{
    private readonly Func<string, Task<byte[]>> _read;
    private readonly Func<byte[], T> _create;
    private readonly Action<string, Exception> _failed;
    private readonly Dictionary<string, T> _created = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<byte[]>> _reading = new(StringComparer.OrdinalIgnoreCase);

    public SoundLoader(Func<string, Task<byte[]>> read, Func<byte[], T> create, Action<string, Exception> failed)
    {
        _read = read;
        _create = create;
        _failed = failed;
    }

    public IEnumerable<T> Created => _created.Values.Where(static sound => sound is not null);

    // Only for a source whose reads complete synchronously; a failed read throws.
    public T Load(string path)
    {
        if (!_created.TryGetValue(path, out var sound))
        {
            sound = _create(_read(path).GetAwaiter().GetResult());
            _created.Add(path, sound);
        }

        return sound;
    }

    public void Request(string path)
    {
        if (!string.IsNullOrEmpty(path) && !_created.ContainsKey(path) && !_reading.ContainsKey(path))
        {
            _reading.Add(path, _read(path));
        }
    }

    public T Get(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (_created.TryGetValue(path, out var sound))
        {
            return sound;
        }

        if (!_reading.TryGetValue(path, out var reading))
        {
            Request(path);
            return null;
        }

        if (!reading.IsCompleted)
        {
            return null;
        }

        _reading.Remove(path);
        try
        {
            sound = _create(reading.GetAwaiter().GetResult());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException
            or OperationCanceledException or ArgumentException or EndOfStreamException or NotSupportedException)
        {
            _failed(path, error);
        }

        _created.Add(path, sound);
        return sound;
    }

    public void Clear()
    {
        _created.Clear();
        _reading.Clear();
    }
}
