using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.InputRequests;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Approvals;

/// <summary>
/// The review queue's approvals: confirm-tier actions of background tasks waiting for the user. Approving executes
/// exactly the frozen action and resumes the task; rejecting resumes it with the rejection. Both are audited. Next to
/// them, the input requests of MCP servers: answering repeats the frozen call once with the answer.
/// </summary>
internal static class ApprovalEndpoints
{
    public static RouteGroupBuilder MapApprovalEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder approvals = group.MapGroup("/approvals").WithTags("Approvals").RequireAuthorization(Policies.Human);
        approvals.MapGet("/", ListAsync).WithName("ListApprovals")
            .WithDescription("Undecided approvals of the user's background tasks, oldest first, with reasons and taint sources.");
        approvals.MapPost("/{id}/approve", (PendingActionId id, ClaimsPrincipal user, IApprovalQueue queue, IAuditLog audit, CancellationToken ct) =>
                DecideAsync(id, approve: true, user, queue, audit, ct))
            .WithName("ApproveApproval")
            .WithDescription("Executes the frozen action and resumes the task with its result.");
        approvals.MapPost("/{id}/reject", (PendingActionId id, ClaimsPrincipal user, IApprovalQueue queue, IAuditLog audit, CancellationToken ct) =>
                DecideAsync(id, approve: false, user, queue, audit, ct))
            .WithName("RejectApproval")
            .WithDescription("Resumes the task with the rejection; nothing is executed.");

        // Answered only by a signed-in user in the web UI: never by a token, a push action or the model.
        approvals.MapGet("/inputs", ListInputsAsync).WithName("ListInputRequests")
            .WithDescription("Pending input requests of MCP servers, oldest first.");
        approvals.MapGet("/inputs/{id}", GetInputAsync).WithName("GetInputRequest")
            .WithDescription("One input request in any status.");
        approvals.MapPost("/inputs/{id}", AnswerInputAsync).WithName("AnswerInputRequest")
            .WithDescription("Answers an input request (form fields, or null when a URL request is done), repeats the frozen call once and resumes the waiting task with its outcome.");
        approvals.MapPost("/inputs/{id}/decline", DeclineInputAsync).WithName("DeclineInputRequest")
            .WithDescription("Declines an input request; nothing is sent to the server and the waiting task resumes with the refusal.");
        return group;
    }

    private static async Task<Ok<List<ApprovalItem>>> ListAsync(IApprovalQueue queue, CancellationToken ct) =>
        TypedResults.Ok((await queue.ListPendingAsync(ct)).Select(ApprovalItem.From).ToList());

    private static async Task<Ok<List<InputRequestItem>>> ListInputsAsync(IInputRequestQueue inputs, CancellationToken ct) =>
        TypedResults.Ok((await inputs.ListPendingAsync(ct)).Select(InputRequestItem.From).ToList());

    private static async Task<Results<Ok<InputRequestItem>, ProblemHttpResult>> GetInputAsync(InputRequestId id, IInputRequestQueue inputs, CancellationToken ct) =>
        await inputs.GetAsync(id, ct) is { } request
            ? TypedResults.Ok(InputRequestItem.From(request))
            : new Error("input_request.not_found", "There is no such input request.", ErrorType.NotFound).ToProblem();

    private static async Task<Results<Ok<InputRequestItem>, ProblemHttpResult>> AnswerInputAsync(
        InputRequestId id, InputAnswerRequest answer, ClaimsPrincipal user, IInputRequestQueue inputs, IAuditLog audit, CancellationToken ct)
    {
        Result<InputRequestRecord> answered = await inputs.AnswerAsync(id, answer.Content, ct);
        return await AuditedAsync(id, "input_request.answer", answered, user, audit, ct);
    }

    private static async Task<Results<Ok<InputRequestItem>, ProblemHttpResult>> DeclineInputAsync(
        InputRequestId id, ClaimsPrincipal user, IInputRequestQueue inputs, IAuditLog audit, CancellationToken ct)
    {
        Result<InputRequestRecord> declined = await inputs.DeclineAsync(id, ct);
        return await AuditedAsync(id, "input_request.decline", declined, user, audit, ct);
    }

    // The answer itself is not audited: it can be personal data the server asked for.
    private static async Task<Results<Ok<InputRequestItem>, ProblemHttpResult>> AuditedAsync(
        InputRequestId id, string action, Result<InputRequestRecord> outcome, ClaimsPrincipal user, IAuditLog audit, CancellationToken ct)
    {
        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, action, id.ToString(), new
        {
            tool = outcome.IsSuccess ? outcome.Value.Tool.ToString() : null,
            outcome = outcome.IsSuccess ? "ok" : outcome.Error.Code,
        }), ct);
        return outcome.IsSuccess ? TypedResults.Ok(InputRequestItem.From(outcome.Value)) : outcome.Error.ToProblem();
    }

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
