using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Guard;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Approvals;

/// <summary>
/// The review queue's approvals: confirm-tier actions of background tasks waiting for the user. Approving executes
/// exactly the frozen action and resumes the task; rejecting resumes it with the rejection. Both are audited.
/// </summary>
internal static class ApprovalEndpoints
{
    public static RouteGroupBuilder MapApprovalEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder approvals = group.MapGroup("/approvals").WithTags("Approvals").RequireAuthorization(Policies.Human);
        approvals.MapGet("/", ListAsync).WithName("ListApprovals")
            .WithSummary("Undecided approvals of the user's background tasks, oldest first, with reasons and taint sources.");
        approvals.MapPost("/{id}/approve", (PendingActionId id, ClaimsPrincipal user, IApprovalQueue queue, IAuditLog audit, CancellationToken ct) =>
                DecideAsync(id, approve: true, user, queue, audit, ct))
            .WithName("ApproveApproval")
            .WithSummary("Executes the frozen action and resumes the task with its result.");
        approvals.MapPost("/{id}/reject", (PendingActionId id, ClaimsPrincipal user, IApprovalQueue queue, IAuditLog audit, CancellationToken ct) =>
                DecideAsync(id, approve: false, user, queue, audit, ct))
            .WithName("RejectApproval")
            .WithSummary("Resumes the task with the rejection; nothing is executed.");
        return group;
    }

    private static async Task<Ok<List<ApprovalItem>>> ListAsync(IApprovalQueue queue, CancellationToken ct) =>
        TypedResults.Ok((await queue.ListPendingAsync(ct)).Select(ApprovalItem.From).ToList());

    private static async Task<Results<Ok<ApprovalDecision>, ProblemHttpResult>> DecideAsync(
        PendingActionId id, bool approve, ClaimsPrincipal user, IApprovalQueue queue, IAuditLog audit, CancellationToken ct)
    {
        Result<PendingActionRecord> decided = await queue.DecideAsync(id, approve, DecidedBy.Ui, ct);
        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, approve ? "approval.approve" : "approval.reject", id.ToString(), new
        {
            tool = decided.IsSuccess ? decided.Value.Tool.ToString() : null,
            outcome = decided.IsSuccess ? "ok" : decided.Error.Code,
        }), ct);
        return decided.IsSuccess
            ? TypedResults.Ok(new ApprovalDecision(id.ToString(), decided.Value.Status))
            : decided.Error.ToProblem();
    }
}
