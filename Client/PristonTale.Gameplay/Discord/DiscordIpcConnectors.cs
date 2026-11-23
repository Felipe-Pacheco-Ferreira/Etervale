using System.IO.Pipes;
using System.Net.Sockets;

namespace PristonTale.Gameplay.Discord;

// Opens the Discord client's local IPC endpoint: the first of discord-ipc-0 to discord-ipc-9 that answers. Null when
// no Discord client is running.
public interface IDiscordIpcConnector
{
    Task<Stream> TryConnectAsync(CancellationToken cancellation);
}

public static class DiscordIpcConnectors
{
    public const int EndpointCount = 10;
    public const string EndpointPrefix = "discord-ipc-";

    // The connector of this platform; null where the game has no local Discord (the browser, Android).
    public static IDiscordIpcConnector ForThisPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPipeConnector();
        }

        return OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? new UnixSocketConnector(UnixSocketConnector.CandidateDirectories()) : null;
    }

    public static string EndpointName(int index) => EndpointPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Windows: the named pipes \\.\pipe\discord-ipc-N.
    private sealed class WindowsPipeConnector : IDiscordIpcConnector
    {
        private const int ConnectMilliseconds = 100;

        public async Task<Stream> TryConnectAsync(CancellationToken cancellation)
        {
            for (var index = 0; index < EndpointCount; index++)
            {
                var pipe = new NamedPipeClientStream(".", EndpointName(index), PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(ConnectMilliseconds, cancellation).ConfigureAwait(false);
                    return pipe;
                }
                catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                }
            }

            return null;
        }
    }
}

// Linux and macOS: the Unix socket discord-ipc-N in the user's runtime or temporary directory, or in the folder a
// sandboxed Discord (Flatpak, Snap) uses inside it. The directories come from .NET's temporary path, the per-user
// runtime folders under /run/user and /tmp; no environment variable is read here.
internal sealed class UnixSocketConnector(IReadOnlyList<string> directories) : IDiscordIpcConnector
{
    private const string RuntimeRoot = "/run/user";
    private const string SharedTemporary = "/tmp";
    private static readonly string[] SandboxFolders = [Path.Combine("app", "com.discordapp.Discord"), "snap.discord"];

    public IReadOnlyList<string> Directories { get; } = directories;

    public async Task<Stream> TryConnectAsync(CancellationToken cancellation)
    {
        foreach (var directory in Directories)
        {
            for (var index = 0; index < DiscordIpcConnectors.EndpointCount; index++)
            {
                var path = Path.Combine(directory, DiscordIpcConnectors.EndpointName(index));
                if (!File.Exists(path))
                {
                    continue;
                }

                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellation).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception exception) when (exception is SocketException or IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    socket.Dispose();
                    cancellation.ThrowIfCancellationRequested();
                }
            }
        }

        return null;
    }

    public static IReadOnlyList<string> CandidateDirectories()
    {
        var roots = new List<string> { Path.GetTempPath() };
        try
        {
            if (Directory.Exists(RuntimeRoot))
            {
                roots.AddRange(Directory.EnumerateDirectories(RuntimeRoot));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        roots.Add(SharedTemporary);
        var directories = new List<string>();
        foreach (var root in roots)
        {
            var trimmed = Path.TrimEndingDirectorySeparator(root);
            AddOnce(directories, trimmed);
            foreach (var folder in SandboxFolders)
            {
                AddOnce(directories, Path.Combine(trimmed, folder));
            }
        }

        return directories;
    }

    private static void AddOnce(List<string> directories, string directory)
    {
        if (!directories.Contains(directory, StringComparer.Ordinal))
        {
            directories.Add(directory);
        }
    }
}
