using Marshal.Abstractions.Runs;

namespace Marshal.Abstractions.Audit;

/// <summary>
/// An action outside a run. The owner comes from the execution context.
/// </summary>
/// <param name="ActorKind">Who acted.</param>
/// <param name="ActorId">User id, token id, or a system component name.</param>
/// <param name="Action">Action name, e.g. <c>token.create</c>.</param>
/// <param name="Target">What was acted on, e.g. a token id.</param>
/// <param name="Details">Extra data (never secrets); serialized as JSON.</param>
/// <param name="RunId">Related run, if any.</param>
public sealed record AuditEntry(AuditActorKind ActorKind, string? ActorId, string Action, string? Target = null, object? Details = null, RunId? RunId = null);
