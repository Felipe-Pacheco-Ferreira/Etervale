using System.ComponentModel;
using System.Diagnostics;

namespace PristonTale.Gameplay.UI;

internal static class DesktopCommunityLinks
{
    internal static Action<string> BrowserOpen { get; set; }

    public static Uri Parse(string value) =>
        (BrowserOpen is not null || OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) &&
        Uri.TryCreate(value, UriKind.Absolute, out var address) &&
        address.Scheme == Uri.UriSchemeHttps && address.UserInfo.Length == 0
            ? address : null;

    public static void Open(Uri address)
    {
        if (OperatingSystem.IsBrowser())
        {
            BrowserOpen?.Invoke(address.AbsoluteUri);
            return;
        }

        try
        {
            using var browser = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Console.Error.WriteLine("Could not open the community page.");
        }
    }
}
