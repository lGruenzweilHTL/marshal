using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Artifacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Marshal.Api.Artifacts;

/// <summary>
/// The artifact store over HTTP: list, upload, metadata, content (session or token, and signed links without sign-in),
/// delete, keeping a printer snapshot and executor uploads. Content is served with the detected media type and
/// <c>nosniff</c>; HTML and SVG only under a sandbox CSP without scripts, and anything unknown as a download.
/// </summary>
internal static class ArtifactEndpoints
{
    /// <summary>CSP for HTML and SVG content: no scripts, forms, popups or same-origin access; framed only by Marshal.</summary>
    public const string SandboxPolicy = "sandbox; default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; frame-ancestors 'self'";

    // Multipart framing around one file.
    private const long MultipartOverhead = 64 * 1024;

    private static readonly AuthorizationPolicy _callbackToken = new AuthorizationPolicyBuilder(MarshalAuthSchemes.ExecutorCallback)
        .RequireAuthenticatedUser()
        .RequireClaim(MarshalClaims.Task)
        .Build();

    public static RouteGroupBuilder MapArtifactEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder artifacts = group.MapGroup("/artifacts").WithTags("Artifacts").RequireAuthorization(Policies.Human);
        artifacts.MapGet("/", ListAsync).WithName("ListArtifacts")
            .WithSummary("The owner's artifacts, newest first, filtered by kind, source and name, with the quota usage.");
        artifacts.MapPost("/", UploadAsync).WithName("UploadArtifact")
            .WithSummary("Uploads one file (multipart field 'file'), at most artifacts.maxUploadMb.")
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data");
        artifacts.MapGet("/{id:guid}", GetAsync).WithName("GetArtifact");
        artifacts.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteArtifact");
        group.MapGet("/artifacts/{id:guid}/content", ContentAsync).WithName("GetArtifactContent").WithTags("Artifacts")
            .WithSummary("The artifact's content (session or token).")
            .RequireAuthorization();
        group.MapGet("/artifacts/{id:guid}/content/signed", SignedContentAsync).WithName("GetSignedArtifactContent").WithTags("Artifacts")
            .WithSummary("The artifact's content through a signed, expiring link; no sign-in.")
            .AllowAnonymous();
        group.MapPost("/printers/{printer}/snapshots/{snapshot}/keep", KeepSnapshotAsync).WithName("KeepPrinterSnapshot").WithTags("Printers")
            .WithSummary("Copies a printer snapshot into the artifact store, outside the snapshot retention.")
            .RequireAuthorization(Policies.Human);
        group.MapPost("/executor/callbacks/{sessionId:guid}/artifacts", ExecutorUploadAsync)
            .RequireAuthorization(_callbackToken)
            .ExcludeFromDescription();
        return group;
    }

    // Kind and source arrive in lowercase (the JSON names); enum binding is case-sensitive, so they are parsed here.
    private static async Task<Results<Ok<ArtifactList>, ProblemHttpResult>> ListAsync(string? kind, string? source, string? query, IArtifactStore store, IArtifactLinks links, CancellationToken ct)
    {
        if (!TryParse(kind, out ArtifactKind? kindFilter) || !TryParse(source, out ArtifactSource? sourceFilter))
            return new Error("artifacts.invalid_filter", "Unknown kind or source.", ErrorType.Validation).ToProblem();

        IReadOnlyList<ArtifactRecord> found = await store.ListAsync(new ArtifactQuery(kindFilter, sourceFilter, query), ct);
        ArtifactUsage usage = await store.UsageAsync(ct);
        return TypedResults.Ok(new ArtifactList([.. found.Select(artifact => ArtifactResponse.From(artifact, links))], usage.Used, usage.Quota));
    }

    private static bool TryParse<TEnum>(string? value, out TEnum? parsed)
        where TEnum : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrEmpty(value))
            return true;
        if (!Enum.TryParse(value, ignoreCase: true, out TEnum result) || int.TryParse(value, out _))
            return false;

        parsed = result;
        return true;
    }

    private static async Task<Results<Ok<ArtifactResponse>, ProblemHttpResult>> GetAsync(Guid id, IArtifactStore store, IArtifactLinks links, CancellationToken ct)
    {
        Result<ArtifactRecord> artifact = await store.GetAsync(ArtifactId.From(id), ct);
        return artifact.IsSuccess ? TypedResults.Ok(ArtifactResponse.From(artifact.Value, links)) : artifact.Error.ToProblem();
    }

    private static async Task<Results<Created<ArtifactResponse>, ProblemHttpResult>> UploadAsync(
        HttpContext context, IArtifactStore store, IArtifactLinks links, IOptions<ArtifactOptions> options, ClaimsPrincipal user, IAuditLog audit)
    {
        CancellationToken ct = context.RequestAborted;
        long limit = options.Value.MaxUploadMb * 1024L * 1024L;
        if (context.Request.ContentLength > limit + MultipartOverhead)
            return TooLarge(options.Value);
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
            bodySize.MaxRequestBodySize = limit + MultipartOverhead;

        // Streams the first file part straight into the store; nothing is buffered in memory or on disk first.
        string? boundary = MediaTypeHeaderValue.TryParse(context.Request.ContentType, out MediaTypeHeaderValue? type) ? HeaderUtilities.RemoveQuotes(type.Boundary).Value : null;
        if (string.IsNullOrEmpty(boundary) || type?.MediaType.Value?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) != true)
            return new Error("artifacts.invalid_upload", "Send the file as multipart/form-data in a field named 'file'.", ErrorType.Validation).ToProblem();

        var reader = new MultipartReader(boundary, context.Request.Body);
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue? disposition)
                || !disposition.IsFileDisposition())
            {
                continue;
            }

            string name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? "";
            Result<ArtifactRecord> created;
            try
            {
                created = await store.CreateAsync(new ArtifactDraft(name, ArtifactSource.Upload), section.Body, ct);
            }
            catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return TooLarge(options.Value);
            }

            if (created.IsFailure)
                return created.Error.Code == "artifacts.too_large" ? TooLarge(options.Value) : created.Error.ToProblem();

            (AuditActorKind kind, string actor) = user.Actor();
            await audit.RecordAsync(new AuditEntry(kind, actor, "artifact.upload", created.Value.Id.ToString(), new { name = created.Value.Name, size = created.Value.Size }), ct);
            var response = ArtifactResponse.From(created.Value, links);
            return TypedResults.Created(response.Url.Replace("/content", "", StringComparison.Ordinal), response);
        }

        return new Error("artifacts.invalid_upload", "Send the file as multipart/form-data in a field named 'file'.", ErrorType.Validation).ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, IArtifactStore store, ClaimsPrincipal user, IAuditLog audit, CancellationToken ct)
    {
        Result deleted = await store.DeleteAsync(ArtifactId.From(id), ct);
        if (deleted.IsFailure)
            return deleted.Error.ToProblem();

        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, "artifact.delete", id.ToString()), ct);
        return TypedResults.NoContent();
    }

    private static Task<IResult> ContentAsync(Guid id, IArtifactStore store, HttpContext context) => ServeAsync(ArtifactId.From(id), store, context);

    // An invalid, expired or foreign signature looks like a missing artifact.
    private static async Task<IResult> SignedContentAsync(Guid id, string? signature, ApiArtifactLinks links, IArtifactStore store, IExecutionContextAccessor executionContext, HttpContext context)
    {
        var artifact = ArtifactId.From(id);
        if (links.Verify(artifact, signature) is not { } owner)
            return new Error("artifacts.not_found", $"There is no artifact '{id}'.", ErrorType.NotFound).ToProblem();

        using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Web, IsInteractive = false });
        return await ServeAsync(artifact, store, context);
    }

    private static async Task<IResult> ServeAsync(ArtifactId id, IArtifactStore store, HttpContext context)
    {
        Result<(ArtifactRecord Artifact, Stream Content)> opened = await store.OpenReadAsync(id, context.RequestAborted);
        if (opened.IsFailure)
            return opened.Error.ToProblem();

        (ArtifactRecord artifact, Stream content) = opened.Value;
        IHeaderDictionary headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "private, max-age=300";
        if (artifact.Kind is ArtifactKind.Html or ArtifactKind.Svg)
            headers.ContentSecurityPolicy = SandboxPolicy;

        var disposition = new ContentDispositionHeaderValue(artifact.Kind == ArtifactKind.Other ? "attachment" : "inline");
        disposition.SetHttpFileName(artifact.Name);
        headers.ContentDisposition = disposition.ToString();
        return TypedResults.Stream(content, artifact.MediaType, enableRangeProcessing: true);
    }

    private static async Task<Results<Created<ArtifactResponse>, ProblemHttpResult>> KeepSnapshotAsync(
        string printer, string snapshot, IPrinterDirectory directory, ISnapshotSource snapshots, IArtifactStore store, IArtifactLinks links,
        ClaimsPrincipal user, IAuditLog audit, CancellationToken ct)
    {
        if (directory.Printers.FirstOrDefault(info => string.Equals(info.Name, printer, StringComparison.OrdinalIgnoreCase)) is not { } found)
            return new Error("printer.not_found", $"There is no printer '{printer}'.", ErrorType.NotFound).ToProblem();

        Result<byte[]> image = await snapshots.OpenAsync(found.Name, snapshot, ct);
        if (image.IsFailure)
            return image.Error.ToProblem();

        Result<ArtifactRecord> kept = await store.CreateAsync(
            new ArtifactDraft($"{found.Name}-{snapshot}.jpg", ArtifactSource.Printer, $"printer:{found.Name}/snapshot:{snapshot}"), new MemoryStream(image.Value), ct);
        if (kept.IsFailure)
            return kept.Error.ToProblem();

        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, "printer.snapshot_kept", found.Name, new { snapshot, artifact = kept.Value.Id.ToString() }), ct);
        var response = ArtifactResponse.From(kept.Value, links);
        return TypedResults.Created(response.Url.Replace("/content", "", StringComparison.Ordinal), response);
    }

    // One file per request, the name in the X-Artifact-Name header; refusals carry the reason for the session notes.
    private static async Task<Results<Created<ArtifactResponse>, ProblemHttpResult>> ExecutorUploadAsync(
        Guid sessionId, HttpContext context, ClaimsPrincipal user, Abstractions.Executors.IExecutorCallbacks callbacks, IArtifactStore store, IArtifactLinks links,
        IOptions<ArtifactOptions> options)
    {
        CancellationToken ct = context.RequestAborted;
        if (!ApiTokenId.TryParse(user.FindFirstValue(MarshalClaims.TokenId), null, out ApiTokenId token))
            return new Error("executor.wrong_session", "This callback token belongs to another session.", ErrorType.Forbidden).ToProblem();

        Result<Abstractions.Tasks.TaskId> task = await callbacks.AuthorizeUploadAsync(sessionId, token, ct);
        if (task.IsFailure)
            return task.Error.ToProblem();

        long limit = options.Value.MaxUploadMb * 1024L * 1024L;
        if (context.Request.ContentLength > limit)
            return TooLarge(options.Value);
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
            bodySize.MaxRequestBodySize = limit + 1;

        string name = Uri.UnescapeDataString(context.Request.Headers["X-Artifact-Name"].ToString());
        Result<ArtifactRecord> created = await store.CreateAsync(new ArtifactDraft(name, ArtifactSource.Executor, $"task:{task.Value}", Tainted: true), context.Request.Body, ct);
        if (created.IsFailure)
            return created.Error.Code == "artifacts.too_large" ? TooLarge(options.Value) : created.Error.ToProblem();

        var response = ArtifactResponse.From(created.Value, links);
        return TypedResults.Created(response.Url.Replace("/content", "", StringComparison.Ordinal), response);
    }

    private static ProblemHttpResult TooLarge(ArtifactOptions options) => TypedResults.Problem(
        detail: $"The file is larger than the limit of {options.MaxUploadMb} MB.",
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "artifacts.too_large",
        extensions: new Dictionary<string, object?> { ["code"] = "artifacts.too_large" });
}

/// <summary>An artifact.</summary>
/// <param name="Id">Artifact id.</param>
/// <param name="Name">File name.</param>
/// <param name="Kind">Detected kind; picks the viewer.</param>
/// <param name="MediaType">Detected media type.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="SourceReference">Task, message or printer snapshot it came from.</param>
/// <param name="Tainted">Content or name can carry third-party text.</param>
/// <param name="CreatedAt">When it was stored.</param>
/// <param name="Url">Content URL (session or token).</param>
public sealed record ArtifactResponse(
    string Id, string Name, ArtifactKind Kind, string MediaType, long Size, ArtifactSource Source, string? SourceReference, bool Tainted, DateTimeOffset CreatedAt, string Url)
{
    internal static ArtifactResponse From(ArtifactRecord artifact, IArtifactLinks links) => new(
        artifact.Id.ToString(), artifact.Name, artifact.Kind, artifact.MediaType, artifact.Size, artifact.Source, artifact.SourceReference, artifact.Tainted,
        artifact.CreatedAt, links.Content(artifact.Id));
}

/// <summary>Artifacts with the owner's storage usage.</summary>
/// <param name="Items">Artifacts, newest first.</param>
/// <param name="UsedBytes">Storage used.</param>
/// <param name="QuotaBytes">Storage allowed (<c>artifacts.quotaMb</c>).</param>
public sealed record ArtifactList(IReadOnlyList<ArtifactResponse> Items, long UsedBytes, long QuotaBytes);
