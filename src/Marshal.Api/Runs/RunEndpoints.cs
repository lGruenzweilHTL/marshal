using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Runs;

/// <summary>
/// The runs view (<c>/runs</c>) and the cost view (<c>/cost</c>) for the signed-in user.
/// </summary>
internal static class RunEndpoints
{
    private const int MaxCostDays = 366;

    public static RouteGroupBuilder MapRunEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/runs", ListAsync).RequireAuthorization(Policies.Human).WithTags("Runs").WithName("ListRuns")
            .WithSummary("The user's runs (voice, web, task, api), newest first; filter by source, outcome and minimum cost, page with before.");
        group.MapGet("/runs/{id}", GetAsync).RequireAuthorization(Policies.Human).WithTags("Runs").WithName("GetRun")
            .WithSummary("One run: context build, model calls, tool calls with guard decisions, taint sources and the reply.");
        group.MapGet("/cost", CostAsync).RequireAuthorization(Policies.Human).WithTags("Runs").WithName("GetCost")
            .WithSummary("Cost per day and tier in the user's time zone, advisor separately (default: the last 30 days).");
        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<RunSummary>>, ProblemHttpResult>> ListAsync(
        string? source, string? outcome, decimal? minCost, DateTimeOffset? before, int? limit, IRunQueries runs, CancellationToken ct)
    {
        // Query values use the same camelCase names as the JSON (voice, web, ok, error, ...).
        if (!TryParse(source, out SessionSource? sourceFilter) || !TryParse(outcome, out RunOutcome? outcomeFilter))
            return new Error("runs.invalid_filter", "Unknown source or outcome.", ErrorType.Validation).ToProblem();

        return TypedResults.Ok(await runs.ListAsync(new RunFilter(sourceFilter, outcomeFilter, minCost, before, limit ?? 50), ct));
    }

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

    private static async Task<Results<Ok<RunDetail>, ProblemHttpResult>> GetAsync(RunId id, IRunQueries runs, CancellationToken ct) =>
        await runs.GetAsync(id, ct) is { } detail
            ? TypedResults.Ok(detail)
            : new Error("run.not_found", "The run does not exist.", ErrorType.NotFound).ToProblem();

    private static async Task<Results<Ok<CostReport>, ProblemHttpResult>> CostAsync(
        DateOnly? from, DateOnly? to, ClaimsPrincipal user, IRunQueries runs, IUserPreferencesStore preferences, TimeProvider timeProvider, CancellationToken ct)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById((await preferences.GetAsync(user.Owner(), ct)).TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
        DateOnly last = to ?? today;
        DateOnly first = from ?? last.AddDays(-29);
        if (first > last || last.DayNumber - first.DayNumber >= MaxCostDays)
            return new Error("cost.invalid_range", $"from must not be after to, and the range is at most {MaxCostDays} days.", ErrorType.Validation).ToProblem();

        return TypedResults.Ok(await runs.CostAsync(first, last, zone, ct));
    }
}
