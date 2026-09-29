using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Memory;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Memory;

/// <summary>
/// The memory view (<c>/memory</c>): chunks (list, search, create, edit, pin, supersede, delete, history), the audit,
/// the NDJSON export, and projects with aliases and correction log. Pinning is only possible here. Every change is
/// audited with the user as actor.
/// </summary>
internal static class MemoryEndpoints
{
    private const int MaxLimit = 500;

    public static RouteGroupBuilder MapMemoryEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder memory = group.MapGroup("/memory").WithTags("Memory").RequireAuthorization(Policies.Human);
        memory.MapGet("/chunks", ListAsync).WithName("ListMemoryChunks")
            .WithSummary("Chunks newest first, or ranked by relevance with query; filter by type, scope, projectId and pinned.");
        memory.MapPost("/chunks", CreateAsync).WithName("CreateMemoryChunk")
            .WithSummary("Writes a chunk through its deterministic id (an existing chunk with the same identity is updated).");
        memory.MapGet("/chunks/{id}", GetAsync).WithName("GetMemoryChunk")
            .WithSummary("A chunk (also when deleted) with its earlier versions and its edges.");
        memory.MapPut("/chunks/{id}", EditAsync).WithName("EditMemoryChunk")
            .WithSummary("Changes content, importance and source; the previous content becomes a version.");
        memory.MapDelete("/chunks/{id}", DeleteAsync).WithName("DeleteMemoryChunk")
            .WithSummary("Soft-deletes a chunk; it is no longer retrieved.");
        memory.MapPut("/chunks/{id}/pin", PinAsync).WithName("PinMemoryChunk")
            .WithSummary("Pins a chunk: it is injected in full in every turn.");
        memory.MapDelete("/chunks/{id}/pin", UnpinAsync).WithName("UnpinMemoryChunk")
            .WithSummary("Unpins a chunk.");
        memory.MapPost("/chunks/{id}/supersede", SupersedeAsync).WithName("SupersedeMemoryChunk")
            .WithSummary("Writes a replacement with a new identity and a supersedes edge to the chunk.");
        memory.MapGet("/chunks/{id}/history", HistoryAsync).WithName("GetMemoryChunkHistory")
            .WithSummary("Predecessors, earlier versions and the current content, oldest first.");
        memory.MapGet("/audit", GetAuditAsync).WithName("GetMemoryAudit")
            .WithSummary("Stale chunks, open contradictions, orphans and dangling edges.");
        memory.MapGet("/export", Export).WithName("ExportMemory")
            .WithSummary("Chunks, versions, edges and projects as NDJSON (without embeddings).");

        memory.MapGet("/projects", ListProjectsAsync).WithName("ListProjects")
            .WithSummary("Live projects with aliases, alias candidates and chunk counts.");
        memory.MapPost("/projects", CreateProjectAsync).WithName("CreateProject")
            .WithSummary("Creates a project; names are unique.");
        memory.MapPut("/projects/{id}", UpdateProjectAsync).WithName("UpdateProject")
            .WithSummary("Renames or re-describes a project.");
        memory.MapDelete("/projects/{id}", DeleteProjectAsync).WithName("DeleteProject")
            .WithSummary("Deletes a project; its chunks stay and show up as orphans in the audit.");
        memory.MapPost("/projects/{id}/aliases", AddAliasAsync).WithName("AddProjectAlias")
            .WithSummary("Adds an active alias (or activates an existing candidate).");
        memory.MapPut("/projects/{id}/aliases/{aliasId}", SetAliasStatusAsync).WithName("SetProjectAliasStatus")
            .WithSummary("Promotes or rejects an alias or alias candidate.");
        memory.MapDelete("/projects/{id}/aliases/{aliasId}", RemoveAliasAsync).WithName("RemoveProjectAlias")
            .WithSummary("Removes an alias or alias candidate.");
        memory.MapGet("/projects/{id}/feedback", FeedbackAsync).WithName("GetProjectFeedback")
            .WithSummary("The project's correction log, newest first.");
        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<MemoryChunk>>, ProblemHttpResult>> ListAsync(
        string? query, string? type, string? scope, ProjectId? projectId, bool? pinned, bool? includeDeleted, int? limit, IMemoryBrowser browser, CancellationToken ct)
    {
        // Query values use the same camelCase names as the JSON (device, general, ...).
        if (!TryParse(type, out ChunkType? typeFilter) || !TryParse(scope, out ChunkScope? scopeFilter))
            return new Error("memory.invalid_filter", "Unknown type or scope.", ErrorType.Validation).ToProblem();

        var filter = new MemoryListFilter(query, typeFilter, scopeFilter, projectId, pinned, includeDeleted ?? false, Math.Clamp(limit ?? 100, 1, MaxLimit));
        return TypedResults.Ok(await browser.ListAsync(filter, ct));
    }

    private static async Task<Results<Created<ChunkWriteResult>, ProblemHttpResult>> CreateAsync(
        ChunkWriteRequest request, ClaimsPrincipal user, IMemoryStore store, IAuditLog audit, CancellationToken ct)
    {
        Result<ChunkWriteResult> written = await store.WriteAsync(request.ToDraft(), ChunkAuthor.User, ct);
        if (written.IsFailure)
            return written.Error.ToProblem();

        await AuditAsync(audit, user, "memory.chunk.create", written.Value.Id, new { request.Title, Outcome = written.Value.Outcome.ToString() }, ct);
        return TypedResults.Created($"/memory/chunks/{written.Value.Id}", written.Value);
    }

    private static async Task<Results<Ok<MemoryChunkDetail>, ProblemHttpResult>> GetAsync(ChunkId id, IMemoryStore store, CancellationToken ct)
    {
        if (await store.GetAsync(id, ct) is not { } chunk)
            return ChunkNotFound();

        return TypedResults.Ok(new MemoryChunkDetail(chunk, await store.GetVersionsAsync(id, ct), await store.GetEdgesAsync(id, ct)));
    }

    private static async Task<Results<Ok<ChunkWriteResult>, ProblemHttpResult>> EditAsync(
        ChunkId id, ChunkEditRequest request, ClaimsPrincipal user, IMemoryStore store, IAuditLog audit, CancellationToken ct)
    {
        if (await store.GetAsync(id, ct) is not { DeletedAt: null } chunk)
            return ChunkNotFound();

        var draft = new ChunkDraft(chunk.Scope, chunk.ProjectId, chunk.Type, chunk.Title, request.Content, request.Importance, request.SourceRef);
        Result<ChunkWriteResult> written = await store.WriteAsync(draft, ChunkAuthor.User, ct);
        if (written.IsFailure)
            return written.Error.ToProblem();

        await AuditAsync(audit, user, "memory.chunk.edit", id, null, ct);
        return TypedResults.Ok(written.Value);
    }

    private static Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(ChunkId id, ClaimsPrincipal user, IMemoryStore store, IAuditLog audit, CancellationToken ct) =>
        ChangeAsync(id, "memory.chunk.delete", () => store.DeleteAsync(id, ct), user, store, audit, ct);

    private static Task<Results<NoContent, ProblemHttpResult>> PinAsync(ChunkId id, ClaimsPrincipal user, IMemoryStore store, IAuditLog audit, CancellationToken ct) =>
        ChangeAsync(id, "memory.chunk.pin", () => store.SetPinnedAsync(id, pinned: true, ct), user, store, audit, ct);

    private static Task<Results<NoContent, ProblemHttpResult>> UnpinAsync(ChunkId id, ClaimsPrincipal user, IMemoryStore store, IAuditLog audit, CancellationToken ct) =>
        ChangeAsync(id, "memory.chunk.unpin", () => store.SetPinnedAsync(id, pinned: false, ct), user, store, audit, ct);

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangeAsync(
        ChunkId id, string action, Func<Task<Result>> change, ClaimsPrincipal user, IMemoryStore store, IAuditLog audit, CancellationToken ct)
    {
        if (await store.GetAsync(id, ct) is null)
            return ChunkNotFound();

        Result result = await change();
        if (result.IsFailure)
            return result.Error.ToProblem();

        await AuditAsync(audit, user, action, id, null, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<ChunkWriteResult>, ProblemHttpResult>> SupersedeAsync(
        ChunkId id, ChunkWriteRequest request, ClaimsPrincipal user, IMemoryStore store, IAuditLog audit, CancellationToken ct)
    {
        if (await store.GetAsync(id, ct) is not { DeletedAt: null })
            return ChunkNotFound();

        Result<ChunkWriteResult> written = await store.SupersedeAsync(id, request.ToDraft(), ChunkAuthor.User, ct);
        if (written.IsFailure)
            return written.Error.ToProblem();

        await AuditAsync(audit, user, "memory.chunk.supersede", id, new { Successor = written.Value.Id.ToString() }, ct);
        return TypedResults.Created($"/memory/chunks/{written.Value.Id}", written.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<MemoryHistoryEntry>>, ProblemHttpResult>> HistoryAsync(ChunkId id, IMemoryBrowser browser, CancellationToken ct) =>
        await browser.GetHistoryAsync(id, ct) is { } history ? TypedResults.Ok(history) : ChunkNotFound();

    private static async Task<Ok<MemoryAuditReport>> GetAuditAsync(IMemoryAudit memoryAudit, CancellationToken ct) =>
        TypedResults.Ok(await memoryAudit.RunAsync(ct));

    private static PushStreamHttpResult Export(IMemoryExporter exporter, CancellationToken ct) =>
        TypedResults.Stream(stream => exporter.ExportAsync(stream, ct), "application/x-ndjson", "marshal-memory.ndjson");

    private static async Task<Ok<IReadOnlyList<ProjectView>>> ListProjectsAsync(IProjectStore projects, CancellationToken ct) =>
        TypedResults.Ok(await projects.ListAsync(ct));

    private static async Task<Results<Created<ProjectView>, ProblemHttpResult>> CreateProjectAsync(
        ProjectRequest request, ClaimsPrincipal user, IProjectStore projects, IAuditLog audit, CancellationToken ct)
    {
        Result<ProjectView> created = await projects.CreateAsync(request.Name, request.Description ?? "", ct);
        if (created.IsFailure)
            return created.Error.ToProblem();

        await AuditAsync(audit, user, "memory.project.create", created.Value.Id.ToString(), new { created.Value.Name }, ct);
        return TypedResults.Created($"/memory/projects/{created.Value.Id}", created.Value);
    }

    private static async Task<Results<Ok<ProjectView>, ProblemHttpResult>> UpdateProjectAsync(
        ProjectId id, ProjectRequest request, ClaimsPrincipal user, IProjectStore projects, IAuditLog audit, CancellationToken ct)
    {
        Result<ProjectView> updated = await projects.UpdateAsync(id, request.Name, request.Description ?? "", ct);
        if (updated.IsFailure)
            return updated.Error.ToProblem();

        await AuditAsync(audit, user, "memory.project.update", id.ToString(), new { updated.Value.Name }, ct);
        return TypedResults.Ok(updated.Value);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteProjectAsync(
        ProjectId id, ClaimsPrincipal user, IProjectStore projects, IAuditLog audit, CancellationToken ct)
    {
        Result deleted = await projects.DeleteAsync(id, ct);
        if (deleted.IsFailure)
            return deleted.Error.ToProblem();

        await AuditAsync(audit, user, "memory.project.delete", id.ToString(), null, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<ProjectAliasView>, ProblemHttpResult>> AddAliasAsync(
        ProjectId id, AliasRequest request, ClaimsPrincipal user, IProjectStore projects, IAuditLog audit, CancellationToken ct)
    {
        Result<ProjectAliasView> alias = await projects.AddAliasAsync(id, request.Phrase, ct);
        if (alias.IsFailure)
            return alias.Error.ToProblem();

        await AuditAsync(audit, user, "memory.alias.add", id.ToString(), new { alias.Value.Phrase }, ct);
        return TypedResults.Created($"/memory/projects/{id}/aliases/{alias.Value.Id}", alias.Value);
    }

    private static async Task<Results<Ok<ProjectAliasView>, ProblemHttpResult>> SetAliasStatusAsync(
        ProjectId id, Guid aliasId, AliasStatusRequest request, ClaimsPrincipal user, IProjectStore projects, IAuditLog audit, CancellationToken ct)
    {
        Result<ProjectAliasView> alias = await projects.SetAliasStatusAsync(id, aliasId, request.Status, ct);
        if (alias.IsFailure)
            return alias.Error.ToProblem();

        await AuditAsync(audit, user, "memory.alias.update", id.ToString(), new { alias.Value.Phrase, Status = request.Status.ToString() }, ct);
        return TypedResults.Ok(alias.Value);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAliasAsync(
        ProjectId id, Guid aliasId, ClaimsPrincipal user, IProjectStore projects, IAuditLog audit, CancellationToken ct)
    {
        Result removed = await projects.RemoveAliasAsync(id, aliasId, ct);
        if (removed.IsFailure)
            return removed.Error.ToProblem();

        await AuditAsync(audit, user, "memory.alias.remove", id.ToString(), new { AliasId = aliasId }, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<ProjectFeedbackView>>, ProblemHttpResult>> FeedbackAsync(ProjectId id, IProjectStore projects, CancellationToken ct)
    {
        Result<IReadOnlyList<ProjectFeedbackView>> feedback = await projects.GetFeedbackAsync(id, ct);
        return feedback.IsSuccess ? TypedResults.Ok(feedback.Value) : feedback.Error.ToProblem();
    }

    private static Task AuditAsync(IAuditLog audit, ClaimsPrincipal user, string action, ChunkId target, object? details, CancellationToken ct) =>
        AuditAsync(audit, user, action, target.ToString(), details, ct);

    private static async Task AuditAsync(IAuditLog audit, ClaimsPrincipal user, string action, string target, object? details, CancellationToken ct)
    {
        (AuditActorKind actorKind, string actorId) = user.Actor();
        await audit.RecordAsync(new AuditEntry(actorKind, actorId, action, target, details), ct);
    }

    private static ProblemHttpResult ChunkNotFound() =>
        new Error("memory.chunk_not_found", "The memory chunk does not exist.", ErrorType.NotFound).ToProblem();

    private static bool TryParse<TEnum>(string? value, out TEnum? parsed)
        where TEnum : struct, Enum
    {
        parsed = null;
        if (value is null)
            return true;
        if (!Enum.TryParse(value, ignoreCase: true, out TEnum result) || int.TryParse(value, out _))
            return false;

        parsed = result;
        return true;
    }
}

/// <summary>A chunk to write (create or supersede replacement).</summary>
/// <param name="Scope">General or project.</param>
/// <param name="ProjectId">Required for project scope.</param>
/// <param name="Type">Kind of knowledge.</param>
/// <param name="Title">Short English title (part of the identity).</param>
/// <param name="Content">English content.</param>
/// <param name="Importance">1–5 (default 3).</param>
/// <param name="SourceRef">Origin reference, if any.</param>
public sealed record ChunkWriteRequest(ChunkScope Scope, ProjectId? ProjectId, ChunkType Type, string Title, string Content, int? Importance, string? SourceRef)
{
    public ChunkDraft ToDraft() => new(Scope, ProjectId, Type, Title ?? "", Content ?? "", Importance ?? 3, SourceRef);
}

/// <summary>New content of a chunk; identity fields cannot change (supersede instead).</summary>
/// <param name="Content">English content.</param>
/// <param name="Importance">1–5.</param>
/// <param name="SourceRef">Origin reference, if any.</param>
public sealed record ChunkEditRequest(string Content, int Importance, string? SourceRef);

/// <summary>A chunk with its history data.</summary>
/// <param name="Chunk">The chunk.</param>
/// <param name="Versions">Earlier versions, oldest first.</param>
/// <param name="Edges">Edges from and to the chunk.</param>
public sealed record MemoryChunkDetail(MemoryChunk Chunk, IReadOnlyList<MemoryChunkVersion> Versions, IReadOnlyList<MemoryEdge> Edges);

/// <summary>Name and description of a project.</summary>
/// <param name="Name">Name (explicit activation by name).</param>
/// <param name="Description">Description (part of the centroid).</param>
public sealed record ProjectRequest(string Name, string? Description);

/// <summary>An alias phrase.</summary>
/// <param name="Phrase">The phrase; normalized when stored.</param>
public sealed record AliasRequest(string Phrase);

/// <summary>A new alias status.</summary>
/// <param name="Status">Active or rejected.</param>
public sealed record AliasStatusRequest(ProjectAliasStatus Status);
