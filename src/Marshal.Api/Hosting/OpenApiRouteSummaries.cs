using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Marshal.Api.Hosting;

/// <summary>
/// Names each operation by its route below the version base (<c>/conversation/turns</c> for
/// <c>POST /api/v1/conversation/turns</c>); what the operation does is its description (<c>WithDescription</c>).
/// </summary>
internal sealed partial class OpenApiRouteSummaries : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // RelativePath has the version substituted; drop constraints, defaults and catch-all marks like the document's path keys.
        string path = "/" + RouteParameter().Replace(context.Description.RelativePath?.Trim('/') ?? "", "{$1}");
        string versionBase = "/api/" + context.DocumentName;
        operation.Summary = path.StartsWith(versionBase + "/", StringComparison.Ordinal) ? path[versionBase.Length..] : path;
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"\{\*{0,2}([^}:?=]+)[^}]*\}")]
    private static partial Regex RouteParameter();
}
