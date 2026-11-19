using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World.Fields;

namespace PristonTale.Gameplay.World;

// The browser client downloads the files a field needs before entering it and the rest of the world wave after the
// field's first frame. The rest: other class families' full motion banks, other fields' minimaps, and screens' art the
// world does not draw. A read that misses one of them waits for Pending and tries once more. The desktop and Android
// clients have every file and never defer any.
internal static class DeferredWorldFiles
{
    private const string Assets = "assets/";
    private const string MotionFolder = Assets + "char/tmABCD/";
    private const string MotionExtension = ".smb";
    private const string PreviewMotionExtension = ".preview.smb";
    private const string ChatBubbleFolder = Assets + "StartImage/MessageBox/ChatBox/";
    private static readonly string StartFieldMap = Assets + HudArt.FieldMap(RicartenField.Definition.Id);
    private static readonly string FieldMapFolder = StartFieldMap[..(StartFieldMap.LastIndexOf('/') + 1)];
    private static readonly string[] UnusedInWorld = [Assets + "image/GuideMap/", Assets + "cSelect/", Assets + "StartImage/"];

    private static Func<Task> _fetch;
    private static TaskCompletionSource _pending;

    // Completes once the deferred files are written, or once their download gave up; never faults.
    internal static Task Pending => _pending?.Task ?? Task.CompletedTask;

    // A class family's full bank, beside its preview skeleton: m4.preview.smb gives m4.smb.
    internal static string FullMotionBank(string previewMotionPath) =>
        previewMotionPath.EndsWith(PreviewMotionExtension, StringComparison.OrdinalIgnoreCase)
            ? previewMotionPath[..^PreviewMotionExtension.Length] + MotionExtension : null;

    // A full motion bank directly in the character folder: m1.smb to m8.smb.
    internal static bool IsFullMotionBank(string path) =>
        path.Length == MotionFolder.Length + 6
        && path.StartsWith(MotionFolder, StringComparison.OrdinalIgnoreCase)
        && path[MotionFolder.Length] is 'm' or 'M'
        && path[MotionFolder.Length + 1] is >= '1' and <= '8'
        && path.EndsWith(MotionExtension, StringComparison.OrdinalIgnoreCase);

    // ownMotionBank: the entering character's bank, or null to keep every bank in the entry wave.
    internal static bool CanWait(string path, string ownMotionBank)
    {
        if (IsFullMotionBank(path))
        {
            return ownMotionBank is not null && !path.Equals(ownMotionBank, StringComparison.OrdinalIgnoreCase);
        }

        if (path.StartsWith(FieldMapFolder, StringComparison.OrdinalIgnoreCase))
        {
            return !path.Equals(StartFieldMap, StringComparison.OrdinalIgnoreCase);
        }

        if (path.StartsWith(ChatBubbleFolder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var folder in UnusedInWorld)
        {
            if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Registers the download to run once the field shows. A later entry before it ran replaces it.
    internal static void Defer(Func<Task> fetch)
    {
        _fetch = fetch;
        if (_pending is null || _pending.Task.IsCompleted)
        {
            _pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    // Called when a field's first frame is drawn; the desktop and Android clients registered nothing.
    internal static void Begin()
    {
        var fetch = _fetch;
        _fetch = null;
        if (fetch is not null)
        {
            _ = RunAsync(fetch, _pending);
        }
    }

    private static async Task RunAsync(Func<Task> fetch, TaskCompletionSource pending)
    {
        try
        {
            await fetch();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
        {
            Console.Error.WriteLine($"[World] deferred files were not all downloaded: {error.Message}");
        }
        finally
        {
            pending.TrySetResult();
        }
    }
}
