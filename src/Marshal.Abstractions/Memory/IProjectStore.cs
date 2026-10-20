using Marshal.Abstractions.Runs;

namespace Marshal.Abstractions.Memory;

/// <summary>
/// The current owner's projects as managed in the memory view: name and description, aliases and alias candidates,
/// and the correction log.
/// </summary>
public interface IProjectStore
{
    /// <summary>Live projects, ordered by name.</summary>
    Task<IReadOnlyList<ProjectView>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Creates a project; the name must not match another live project's name.</summary>
    Task<Result<ProjectView>> CreateAsync(string name, string description, CancellationToken cancellationToken);

    /// <summary>Renames or re-describes a project; its centroid is recomputed on the next activation.</summary>
    Task<Result<ProjectView>> UpdateAsync(ProjectId id, string name, string description, CancellationToken cancellationToken);

    /// <summary>Soft-deletes a project; its chunks stay and show up as orphans in the audit.</summary>
    Task<Result> DeleteAsync(ProjectId id, CancellationToken cancellationToken);

    /// <summary>Adds an active alias, or activates the existing candidate or rejected alias for the phrase.</summary>
    Task<Result<ProjectAliasView>> AddAliasAsync(ProjectId id, string phrase, CancellationToken cancellationToken);

    /// <summary>Promotes (active) or rejects an alias or alias candidate.</summary>
    Task<Result<ProjectAliasView>> SetAliasStatusAsync(ProjectId id, Guid aliasId, ProjectAliasStatus status, CancellationToken cancellationToken);

    Task<Result> RemoveAliasAsync(ProjectId id, Guid aliasId, CancellationToken cancellationToken);

    /// <summary>The project's corrections and confirmations, newest first.</summary>
    Task<Result<IReadOnlyList<ProjectFeedbackView>>> GetFeedbackAsync(ProjectId id, CancellationToken cancellationToken);
}

/// <summary>A project in the memory view.</summary>
/// <param name="Id">Project id.</param>
/// <param name="Name">Name (explicit activation by name).</param>
/// <param name="Description">Description (part of the centroid).</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="LastActiveAt">Last activation, if any.</param>
/// <param name="ChunkCount">Live chunks of the project.</param>
/// <param name="Aliases">Active, candidate and rejected aliases.</param>
/// <param name="Summary">Summary written by the idle-time jobs (read-only).</param>
/// <param name="SummaryUpdatedAt">When the summary was written.</param>
public sealed record ProjectView(
    ProjectId Id, string Name, string Description, DateTimeOffset CreatedAt, DateTimeOffset? LastActiveAt, int ChunkCount, IReadOnlyList<ProjectAliasView> Aliases,
    string? Summary = null, DateTimeOffset? SummaryUpdatedAt = null);

/// <summary>An alias or alias candidate.</summary>
/// <param name="Id">Alias id.</param>
/// <param name="Phrase">The phrase.</param>
/// <param name="Status">Candidate, active or rejected.</param>
/// <param name="Confirmations">Confirmations counted towards promotion.</param>
/// <param name="CreatedAt">First seen.</param>
public sealed record ProjectAliasView(Guid Id, string Phrase, ProjectAliasStatus Status, int Confirmations, DateTimeOffset CreatedAt);

/// <summary>An entry of a project's correction log.</summary>
/// <param name="Id">Entry id.</param>
/// <param name="Kind">Confirmed, rejected or switched.</param>
/// <param name="Phrase">The phrase the activation was about.</param>
/// <param name="RunId">The run of the correction, if any.</param>
/// <param name="CreatedAt">When it happened.</param>
public sealed record ProjectFeedbackView(Guid Id, ProjectFeedbackKind Kind, string Phrase, RunId? RunId, DateTimeOffset CreatedAt);
