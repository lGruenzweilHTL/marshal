using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Identity;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Tasks.Maintenance;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Maintenance;

/// <summary>
/// Usage-based maintenance items for the Home widget: their state (due first) and marking one done. The items are
/// instance configuration tracked for the instance owner; other users see none. Marking done is an audited user action.
/// </summary>
internal static class MaintenanceEndpoints
{
    public static RouteGroupBuilder MapMaintenanceEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder maintenance = group.MapGroup("/maintenance").WithTags("Maintenance").RequireAuthorization(Policies.Human);
        maintenance.MapGet("/", ListAsync).WithName("ListMaintenanceItems")
            .WithSummary("Maintenance items read now: due first, then soon, then by name; empty without configured items.");
        maintenance.MapPost("/{id}/done", DoneAsync).WithName("MarkMaintenanceDone")
            .WithSummary("Marks a maintenance item done: its usage starts again from the counter's current value.");
        return group;
    }

    private static async Task<Ok<IReadOnlyList<MaintenanceItemStatus>>> ListAsync(
        ClaimsPrincipal user, MaintenanceTracker tracker, IInstanceOwner instanceOwner, CancellationToken ct) =>
        TypedResults.Ok(user.Owner() == await instanceOwner.GetAsync(ct) ? await tracker.StatusAsync(ct) : []);

    private static async Task<Results<Ok<MaintenanceItemStatus>, ProblemHttpResult>> DoneAsync(
        string id, ClaimsPrincipal user, MaintenanceTracker tracker, IInstanceOwner instanceOwner, IAuditLog audit, CancellationToken ct)
    {
        if (user.Owner() != await instanceOwner.GetAsync(ct))
            return MaintenanceErrors.UnknownItem(id, []).ToProblem();

        Result<MaintenanceItemStatus> done = await tracker.DoneAsync(id, ct);
        if (done.IsFailure && done.Error.Type == ErrorType.NotFound)
            return done.Error.ToProblem();

        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, "maintenance.done", id, new { outcome = done.IsSuccess ? "ok" : done.Error.Code }), ct);
        return done.IsSuccess ? TypedResults.Ok(done.Value) : done.Error.ToProblem();
    }
}
