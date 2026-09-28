using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace Marshal.Api.Hosting;

/// <summary>
/// Serves the built SPA from the web root: hashed assets as static files, every other non-API GET path as the entry
/// page (deep links), and the Content Security Policy on every response.
/// </summary>
internal static partial class SpaHosting
{
    /// <summary>
    /// Scripts, styles and connections only from the app itself; images also from <c>data:</c>. Model output with
    /// external image URLs therefore cannot make the browser contact another host.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; font-src 'self'; "
        + "object-src 'none'; base-uri 'self'; frame-ancestors 'none'";

    private const string EntryPage = "index.html";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "same-origin";
            return next(context);
        });

    public static IApplicationBuilder UseSpaAssets(this IApplicationBuilder app) =>
        app.UseStaticFiles(new StaticFileOptions
        {
            // Vite puts content-hashed files under assets/; they never change under the same name.
            OnPrepareResponse = context =>
            {
                if (context.Context.Request.Path.StartsWithSegments("/assets", StringComparison.Ordinal))
                    context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            },
        });

    public static IEndpointRouteBuilder MapSpaFallback(this IEndpointRouteBuilder app)
    {
        // Lowest precedence: API routes, the /api fallback and file-like paths (nonfile) never reach it.
        // The client-side router reads the path; the server serves the same page for every one.
        app.MapMethods("{**path:nonfile}", [HttpMethods.Get, HttpMethods.Head], (string? path, HttpContext context, IWebHostEnvironment environment) =>
                ServeEntryPage(context, environment))
            .WithOrder(int.MaxValue)
            .AllowAnonymous()
            .ExcludeFromDescription();
        return app;
    }

    private static async Task ServeEntryPage(HttpContext context, IWebHostEnvironment environment)
    {
        IFileInfo file = environment.WebRootFileProvider.GetFileInfo(EntryPage);
        if (!file.Exists)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        string html;
        await using (Stream stream = file.CreateReadStream())
        using (var reader = new StreamReader(stream))
            html = await reader.ReadToEndAsync(context.RequestAborted);

        // The SPA resolves its assets and router history against <base href>; behind a path base that is not "/".
        string baseHref = HtmlEncoder.Default.Encode(context.Request.PathBase.Value + "/");
        html = BaseElement().Replace(html, $"<base href=\"{baseHref}\" />", 1);

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.WriteAsync(html, context.RequestAborted);
    }

    [GeneratedRegex("""<base\s+href="[^"]*"\s*/?>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BaseElement();
}
