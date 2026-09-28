namespace Marshal.Api.Versioning;

/// <summary>
/// <c>GET /api/versions</c>: unversioned, unauthenticated version discovery.
/// </summary>
internal static class VersionEndpoints
{
    public static IEndpointRouteBuilder MapVersionDiscovery(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/versions", () => TypedResults.Ok(new ApiVersionsResponse(
                [.. ApiVersions.Supported.Select(version => version.ToString())],
                [.. ApiVersions.Deprecated.Select(version => version.ToString())])))
            .AllowAnonymous()
            .ExcludeFromDescription();

        // URL-segment versioning answers an unsupported version with an empty 404; name the supported versions.
        app.MapFallback("/api/{**path}", (string path, HttpContext context) =>
            {
                string supported = string.Join(", ", ApiVersions.Supported.Select(version => version.ToString()));
                context.Response.Headers["api-supported-versions"] = supported;
                return TypedResults.Problem(
                    detail: $"Unknown API route '/api/{path}' or unsupported API version. Supported versions: {supported}.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "api.unsupported_version",
                    extensions: new Dictionary<string, object?> { ["code"] = "api.unsupported_version", ["supportedVersions"] = ApiVersions.Supported.Select(version => version.ToString()).ToArray() });
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
        return app;
    }
}

/// <summary>Supported and deprecated API versions.</summary>
internal sealed record ApiVersionsResponse(IReadOnlyList<string> Supported, IReadOnlyList<string> Deprecated);
