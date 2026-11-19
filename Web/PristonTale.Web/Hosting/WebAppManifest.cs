namespace PristonTale.Web.Hosting;

// The web app manifest: "Adicionar à tela inicial" opens the play page full screen, without the browser's bars, lying
// down (landscape), as a mobile game.
internal static class WebAppManifest
{
    public const string Path = "/manifest.webmanifest";
    public const string ContentType = "application/manifest+json";
    private const string Background = "#0a0c11";

    public static IResult Result(string siteName) => Results.Json(new
    {
        name = siteName,
        short_name = siteName,
        description = "MMORPG de fantasia, direto no navegador.",
        lang = "pt-BR",
        id = SecurityHeaders.PlayPagePath,
        start_url = SecurityHeaders.PlayPagePath,
        scope = SecurityHeaders.PlayPagePath,
        display = "fullscreen",
        display_override = new[] { "fullscreen", "standalone" },
        orientation = "landscape",
        background_color = Background,
        theme_color = Background,
        icons = new[]
        {
            new { src = "/img/icon-192.png", sizes = "192x192", type = "image/png", purpose = "any" },
            new { src = "/img/icon-512.png", sizes = "512x512", type = "image/png", purpose = "any" }
        }
    }, contentType: ContentType);
}
