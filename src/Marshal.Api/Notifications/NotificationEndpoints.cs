using Marshal.Abstractions;
using Marshal.Abstractions.Notifications;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Notifications;

/// <summary>
/// The notification history: every notification with the router's decision and its reason.
/// </summary>
internal static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/notifications", ListAsync).RequireAuthorization(Policies.Human).WithTags("Notifications").WithName("ListNotifications")
            .WithDescription("The user's notifications, newest first, with channel, outcome and reason; filter by outcome, page with before.");
        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<NotificationEntry>>, ProblemHttpResult>> ListAsync(
        string? outcome, DateTimeOffset? before, int? limit, INotificationHistory history, CancellationToken ct)
    {
        NotificationOutcome? filter = null;
        if (outcome is not null)
        {
            if (!Enum.TryParse(outcome, ignoreCase: true, out NotificationOutcome parsed) || int.TryParse(outcome, out _))
                return new Error("notifications.invalid_filter", "Unknown outcome.", ErrorType.Validation).ToProblem();
            filter = parsed;
        }

        return TypedResults.Ok(await history.ListAsync(new NotificationQuery(filter, Before: before, Limit: limit ?? 50), ct));
    }
}
