namespace PristonTale.Web.Hosting;

// The headers every response carries. The site runs no script at all, so the policy allows none; styles, images and
// fonts come only from the site itself, and no other site may frame it.
public sealed class SecurityHeaders(RequestDelegate next)
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; img-src 'self'; style-src 'self'; font-src 'self'; manifest-src 'self'; base-uri 'none'; "
        + "form-action 'self'; frame-ancestors 'none'; upgrade-insecure-requests";

    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            Apply(((HttpContext)state).Response.Headers);
            return Task.CompletedTask;
        }, context);
        return next(context);
    }

    public static void Apply(IHeaderDictionary headers)
    {
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
    }
}
