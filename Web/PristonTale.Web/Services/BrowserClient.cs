using Microsoft.AspNetCore.StaticFiles;
using PristonTale.Web.Hosting;

namespace PristonTale.Web.Services;

// The browser client (Client/PristonTale.Browser, published into Play:ClientPath by its publish script), served under
// /play and started by the /jogar page. It is offered only once its files are there and Play:GameEndpoint names the game
// server's WebSocket edge; publishing a new build needs no restart.
public sealed class BrowserClient(WebSettings settings)
{
    public const string RequestPath = "/play";
    private const string EntryFile = "main.js";
    public const string EntryScript = RequestPath + "/" + EntryFile;
    private const string FrameworkFolder = "_framework";
    private const string Loader = "dotnet.js";

    public string Root { get; } = WebSettings.BesideExecutable(settings.Play.ClientPath);

    public string Endpoint => settings.Play.GameEndpoint;

    public bool IsAvailable => Endpoint.Length > 0 && File.Exists(Path.Combine(Root, EntryFile)) && File.Exists(Path.Combine(Root, FrameworkFolder, Loader));

    // The served types: the web's own plus MonoGame's built content (.xnb) and the original game files the client fetches
    // (the music's .bgm among them).
    public static FileExtensionContentTypeProvider ContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        foreach (var extension in new[] { ".ase", ".bgm", ".cam", ".ini", ".inx", ".msg", ".sin", ".smb", ".smd", ".tga", ".xnb" })
        {
            provider.Mappings[extension] = "application/octet-stream";
        }
        return provider;
    }

    // The runtime's assemblies and native module carry a content hash in their names and never change; the loader and
    // the entry script keep their names across builds, so the browser revalidates them.
    // A game file asked for with its hash (?v=, from bootstrap.txt) never changes under that address either.
    public const string VersionQuery = "v";

    public static bool IsFingerprinted(string requestPath) =>
        requestPath.StartsWith(RequestPath + "/" + FrameworkFolder + "/", StringComparison.Ordinal) && !requestPath.EndsWith("/" + Loader, StringComparison.Ordinal);
}
