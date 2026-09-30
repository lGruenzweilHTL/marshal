using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Executors;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Proposals;

/// <summary>
/// The review queue's coding proposals: list, diff, merge (through the executor host) and reject. Merge and reject
/// are user actions only and audited with their outcome.
/// </summary>
internal static class ProposalEndpoints
{
    public static RouteGroupBuilder MapProposalEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder proposals = group.MapGroup("/proposals").WithTags("Proposals").RequireAuthorization(Policies.Human);
        proposals.MapGet("/", ListAsync).WithName("ListProposals")
            .WithSummary("The user's proposals, newest first, without diffs (default: open ones; status=all for every one).");
        proposals.MapGet("/{id:guid}", GetAsync).WithName("GetProposal").WithSummary("One proposal with its diff.");
        proposals.MapPost("/{id:guid}/merge", (Guid id, ClaimsPrincipal user, IProposalReview review, IAuditLog audit, CancellationToken ct) =>
                DecideAsync(id, merge: true, user, review, audit, ct))
            .WithName("MergeProposal")
            .WithSummary("Merges the change into the branch it started from on the executor host; a conflict leaves it open.");
        proposals.MapPost("/{id:guid}/reject", (Guid id, ClaimsPrincipal user, IProposalReview review, IAuditLog audit, CancellationToken ct) =>
                DecideAsync(id, merge: false, user, review, audit, ct))
            .WithName("RejectProposal")
            .WithSummary("Rejects the change and drops its branch on the executor host.");
        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<ProposalRecord>>, ProblemHttpResult>> ListAsync(string? status, IProposalReview review, CancellationToken ct)
    {
        ProposalStatus? filter = ProposalStatus.Open;
        if (status == "all")
        {
            filter = null;
        }
        else if (status is not null)
        {
            if (!Enum.TryParse(status, ignoreCase: true, out ProposalStatus parsed) || int.TryParse(status, out _))
                return new Error("proposals.invalid_filter", "Unknown status.", ErrorType.Validation).ToProblem();
            filter = parsed;
        }

        return TypedResults.Ok(await review.ListAsync(filter, ct));
    }

    private static async Task<Results<Ok<ProposalRecord>, ProblemHttpResult>> GetAsync(Guid id, IProposalReview review, CancellationToken ct)
    {
        Result<ProposalRecord> proposal = await review.GetAsync(id, ct);
        return proposal.IsSuccess ? TypedResults.Ok(proposal.Value) : proposal.Error.ToProblem();
    }

    private static async Task<Results<Ok<ProposalRecord>, ProblemHttpResult>> DecideAsync(
        Guid id, bool merge, ClaimsPrincipal user, IProposalReview review, IAuditLog audit, CancellationToken ct)
    {
        Result<ProposalRecord> decided = merge ? await review.MergeAsync(id, ct) : await review.RejectAsync(id, ct);
        if (decided.IsFailure && decided.Error.Type == ErrorType.NotFound && decided.Error.Code == "proposal.not_found")
            return decided.Error.ToProblem();

        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, merge ? "proposal.merge" : "proposal.reject", id.ToString(), new
        {
            target = decided.IsSuccess ? decided.Value.Target : null,
            outcome = decided.IsSuccess ? "ok" : decided.Error.Code,
        }), ct);
        return decided.IsSuccess ? TypedResults.Ok(decided.Value) : decided.Error.ToProblem();
    }
}
