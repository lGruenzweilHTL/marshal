using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Execution;
using Marshal.Api.Problems;

namespace Marshal.Api.Displays;

/// <summary>
/// Rendition pages for displays: anonymous, authorized only by the signed path, with range requests and <c>HEAD</c>
/// (DLNA renderers probe and seek). Anything wrong with the link looks like a missing artifact.
/// </summary>
internal static class DisplayEndpoints
{
    public static RouteGroupBuilder MapDisplayEndpoints(this RouteGroupBuilder group)
    {
        group.MapMethods("/artifacts/{id:guid}/display/{page:int}/{file}", [HttpMethods.Get, HttpMethods.Head], DisplayAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();
        return group;
    }

    private static async Task<IResult> DisplayAsync(
        Guid id, int page, string file, ApiDisplayLinks links, IArtifactRenditions renditions, IExecutionContextAccessor executionContext, HttpContext context)
    {
        var artifact = ArtifactId.From(id);
        int dot = file.LastIndexOf('.');
        if (dot <= 0 || links.Verify(artifact, page, file[..dot]) is not { } owner)
            return NotFound(id);

        using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Web, IsInteractive = false });
        Result<ArtifactRendition> rendition = await renditions.DescribeAsync(artifact, context.RequestAborted);
        if (rendition.IsFailure || rendition.Value.Extension != file[(dot + 1)..] || page > rendition.Value.Pages)
            return NotFound(id);

        Result<Stream> content = await renditions.OpenAsync(artifact, page, context.RequestAborted);
        if (content.IsFailure)
            return NotFound(id);

        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = "private, max-age=300";
        return TypedResults.Stream(content.Value, rendition.Value.MediaType, enableRangeProcessing: true);
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult NotFound(Guid id) => new Error("artifacts.not_found", $"There is no artifact '{id}'.", ErrorType.NotFound).ToProblem();
}
