using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Api.Hosting;
using Marshal.Api.Versioning;

namespace Marshal.Api.Cli;

/// <summary>
/// <c>--export-openapi &lt;file&gt;</c>: writes the v1 OpenAPI document without starting the server (the document endpoint
/// is invoked in-process), so the SPA build can generate its API types without a running instance.
/// </summary>
internal static class OpenApiExport
{
    private const string Flag = "--export-openapi";

    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };

    public static bool IsRequested(string[] args) => args is [Flag, ..];

    public static async Task<int> RunAsync(string[] args, IDictionary environment, TextWriter? error = null)
    {
        if (args is not [Flag, { } path])
        {
            await (error ?? Console.Error).WriteLineAsync($"Usage: marshal {Flag} <file>");
            return 2;
        }

        await using WebApplication app = MarshalApp.Create([], environment);
        // Normally done when the server starts: registers the mapped endpoints with routing, which is where the
        // (versioned) API explorer finds them.
        app.UseEndpoints(_ => { });
        string requestPath = $"/openapi/{ApiVersions.DocumentName(ApiVersions.V1)}.json";
        RouteEndpoint endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText?.StartsWith("/openapi/", StringComparison.Ordinal) == true);

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = requestPath;
        context.Request.RouteValues["documentName"] = ApiVersions.DocumentName(ApiVersions.V1);
        context.Response.Body = body;
        await endpoint.RequestDelegate!(context);

        // The server list reflects the request host; the SPA calls the API same-origin, so it is left out.
        JsonObject document = JsonNode.Parse(body.ToArray())!.AsObject();
        document.Remove("servers");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, document.ToJsonString(_indented) + "\n");
        return 0;
    }
}
