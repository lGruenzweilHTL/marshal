using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Api.Problems;
using Marshal.Core.Identity;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Auth;

/// <summary>
/// The signed-in user's preferences (<c>/me/preferences</c>).
/// </summary>
internal static class PreferencesEndpoints
{
    public static RouteGroupBuilder MapPreferencesEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder preferences = group.MapGroup("/me/preferences").WithTags("Preferences").RequireAuthorization(Policies.Human);
        preferences.MapGet("/", GetAsync).WithName("GetPreferences")
            .WithSummary("Language, time zone, quiet hours, notification targets and do-not-disturb of the user.");
        preferences.MapPut("/", PutAsync).WithName("UpdatePreferences")
            .WithSummary("Replaces the user's preferences.");
        return group;
    }

    private static async Task<Ok<UserPreferencesResponse>> GetAsync(ClaimsPrincipal user, IUserPreferencesStore store, CancellationToken ct) =>
        TypedResults.Ok(UserPreferencesResponse.From(await store.GetAsync(user.Owner(), ct)));

    private static async Task<Results<NoContent, ProblemHttpResult>> PutAsync(UserPreferencesResponse request, ClaimsPrincipal user, IUserPreferencesStore store, CancellationToken ct)
    {
        UserPreferencesRecord preferences = request.ToRecord();
        Result valid = UserPreferencesRules.Validate(preferences);
        if (valid.IsFailure)
            return valid.Error.ToProblem();

        await store.SaveAsync(user.Owner(), preferences, ct);
        return TypedResults.NoContent();
    }
}

/// <summary>A user's preferences. Content preferences (briefing topics, interests) live in memory, not here.</summary>
/// <param name="Language"><c>de</c> or <c>en</c>; used when a request has no language.</param>
/// <param name="TimeZone">IANA time zone id.</param>
/// <param name="QuietHours">Weekly quiet-hour windows; an end before the start ends the next day.</param>
/// <param name="NotificationTargets">HA <c>person.*</c> entity and <c>notify.*</c> push services.</param>
/// <param name="DndUntil">Do-not-disturb end, if active.</param>
/// <param name="HelpMarshalLearn">"Help Marshal learn" (default off): rated runs become eval cases, and later learning features.</param>
public sealed record UserPreferencesResponse(
    string Language,
    string TimeZone,
    IReadOnlyList<QuietHoursWindow> QuietHours,
    NotificationTargets NotificationTargets,
    DateTimeOffset? DndUntil,
    bool HelpMarshalLearn = false)
{
    public static UserPreferencesResponse From(UserPreferencesRecord record) =>
        new(record.Language, record.TimeZone, record.QuietHours, record.NotificationTargets, record.DndUntil, record.HelpMarshalLearn);

    public UserPreferencesRecord ToRecord() =>
        new(Language, TimeZone, QuietHours ?? [], NotificationTargets ?? NotificationTargets.None, DndUntil, HelpMarshalLearn);
}
