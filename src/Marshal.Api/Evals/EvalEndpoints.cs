using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Templates;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Core.Evals;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Evals;

/// <summary>
/// Run ratings (any signed-in human, own runs only) and the evals admin API: eval cases, cost estimates and eval runs
/// that replay cases against a candidate configuration.
/// </summary>
internal static class EvalEndpoints
{
    public static RouteGroupBuilder MapEvalEndpoints(this RouteGroupBuilder group)
    {
        group.MapPut("/runs/{id}/rating", RateAsync).RequireAuthorization(Policies.Human).WithTags("Runs").WithName("RateRun")
            .WithSummary("Rates one of the user's runs up or down with an optional note; with \"help Marshal learn\" on, the first rating also creates an eval case.");
        group.MapDelete("/runs/{id}/rating", ClearAsync).RequireAuthorization(Policies.Human).WithTags("Runs").WithName("ClearRunRating")
            .WithSummary("Removes the rating of a run; an eval case made from it stays.");
        group.MapPost("/runs/{id}/eval-case", SaveCaseAsync).RequireAuthorization(Policies.Human, Policies.Admin).WithTags("Evals").WithName("SaveRunAsEvalCase")
            .WithSummary("Snapshots a run as an eval case, whatever the learning preference says; returns the existing case when there is one.");

        RouteGroupBuilder evals = group.MapGroup("/evals").WithTags("Evals").RequireAuthorization(Policies.Human, Policies.Admin);
        evals.MapGet("/cases", ListCasesAsync).WithName("ListEvalCases").WithSummary("Eval cases, newest first.");
        evals.MapGet("/cases/{id}", GetCaseAsync).WithName("GetEvalCase").WithSummary("One eval case with its history, turn context, recorded tool calls and reply.");
        evals.MapPut("/cases/{id}/note", NoteAsync).WithName("SetEvalCaseNote").WithSummary("Sets the expected behavior note of a case (at most 1,000 characters).");
        evals.MapDelete("/cases/{id}", DeleteCaseAsync).WithName("DeleteEvalCase").WithSummary("Deletes an eval case.");
        evals.MapPost("/estimate", EstimateAsync).WithName("EstimateEvalRun").WithSummary("Estimated model cost of replaying the cases with the candidate configuration.");
        evals.MapGet("/runs", ListRunsAsync).WithName("ListEvalRuns").WithSummary("Eval runs with their totals, newest first.");
        evals.MapPost("/runs", StartAsync).WithName("StartEvalRun").WithSummary("Starts an eval run: replays the cases one at a time in the background without executing any tool.");
        evals.MapGet("/runs/{id}", GetRunAsync).WithName("GetEvalRun").WithSummary("An eval run with its totals and per case the replayed calls, reply, outcome and judge score.");
        evals.MapPost("/runs/{id}/cancel", CancelAsync).WithName("CancelEvalRun").WithSummary("Cancels a queued or running eval run.");
        evals.MapDelete("/runs/{id}", DeleteRunAsync).WithName("DeleteEvalRun").WithSummary("Deletes an eval run and its results.");
        return group;
    }

    private static async Task<Results<Ok<RatedRun>, ProblemHttpResult>> RateAsync(RunId id, RatingRequest request, IRunRatings ratings, CancellationToken ct)
    {
        // Parsed by hand: an unknown value is a validation problem, not a body that fails to bind.
        if (!Enum.TryParse(request.Rating, ignoreCase: true, out RunRating rating) || int.TryParse(request.Rating, out _))
            return new Error("rating.invalid", "rating must be up or down.", ErrorType.Validation).ToProblem();

        Result<RatedRun> rated = await ratings.RateAsync(id, rating, request.Note, ct);
        return rated.IsSuccess ? TypedResults.Ok(rated.Value) : rated.Error.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ClearAsync(RunId id, IRunRatings ratings, CancellationToken ct) =>
        await ratings.ClearAsync(id, ct) is { IsSuccess: false } failed ? failed.Error.ToProblem() : TypedResults.NoContent();

    private static async Task<Results<Ok<EvalCase>, ProblemHttpResult>> SaveCaseAsync(RunId id, IEvalCaseStore cases, CancellationToken ct)
    {
        Result<EvalCase> saved = await cases.SaveRunAsync(id, ct);
        return saved.IsSuccess ? TypedResults.Ok(saved.Value) : saved.Error.ToProblem();
    }

    private static async Task<Ok<IReadOnlyList<EvalCase>>> ListCasesAsync(IEvalCaseStore cases, CancellationToken ct) => TypedResults.Ok(await cases.ListAsync(ct));

    private static async Task<Results<Ok<EvalCase>, ProblemHttpResult>> GetCaseAsync(EvalCaseId id, IEvalCaseStore cases, CancellationToken ct) =>
        await cases.GetAsync(id, ct) is { } evalCase ? TypedResults.Ok(evalCase) : CaseNotFound().ToProblem();

    private static async Task<Results<NoContent, ProblemHttpResult>> NoteAsync(EvalCaseId id, EvalCaseNoteRequest request, IEvalCaseStore cases, CancellationToken ct) =>
        await cases.UpdateNoteAsync(id, request.Note, ct) is { IsSuccess: false } failed ? failed.Error.ToProblem() : TypedResults.NoContent();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteCaseAsync(EvalCaseId id, IEvalCaseStore cases, CancellationToken ct) =>
        await cases.DeleteAsync(id, ct) is { IsSuccess: false } failed ? failed.Error.ToProblem() : TypedResults.NoContent();

    private static async Task<Results<Ok<EvalEstimate>, ProblemHttpResult>> EstimateAsync(
        EvalRunRequest request, IEvalCaseStore cases, EvalModels models, ITemplateCatalog templates, EvalCostEstimator estimator, CancellationToken ct)
    {
        Result<(CandidateConfig Candidate, IReadOnlyList<EvalCase> Cases)> validated = await ValidateAsync(request, cases, models, templates, ct);
        return validated.IsSuccess
            ? TypedResults.Ok(new EvalEstimate(estimator.Estimate(validated.Value.Cases, validated.Value.Candidate)))
            : validated.Error.ToProblem();
    }

    private static async Task<Ok<IReadOnlyList<EvalRunView>>> ListRunsAsync(IEvalRunStore runs, CancellationToken ct) =>
        TypedResults.Ok<IReadOnlyList<EvalRunView>>([.. (await runs.ListAsync(ct)).Select(EvalRunView.From)]);

    private static async Task<Results<Created<EvalRunView>, ProblemHttpResult>> StartAsync(
        EvalRunRequest request, ClaimsPrincipal user, IEvalCaseStore cases, IEvalRunStore runs, EvalModels models, ITemplateCatalog templates,
        EvalCostEstimator estimator, IEvalRunControl control, CancellationToken ct)
    {
        Result<(CandidateConfig Candidate, IReadOnlyList<EvalCase> Cases)> validated = await ValidateAsync(request, cases, models, templates, ct);
        if (validated.IsFailure)
            return validated.Error.ToProblem();

        (CandidateConfig candidate, IReadOnlyList<EvalCase> selected) = validated.Value;
        EvalRunRecord run = await runs.CreateAsync(candidate, [.. selected.Select(evalCase => evalCase.Id)], user.Actor().Id, estimator.Estimate(selected, candidate), ct);
        control.Enqueue(run.Id, user.Owner());
        return TypedResults.Created($"/api/v1/evals/runs/{run.Id}", EvalRunView.From(run));
    }

    private static async Task<Results<Ok<EvalRunReport>, ProblemHttpResult>> GetRunAsync(EvalRunId id, IEvalRunStore runs, CancellationToken ct) =>
        await runs.GetAsync(id, ct) is { } run
            ? TypedResults.Ok(new EvalRunReport(EvalRunView.From(run), await runs.ResultsAsync(id, ct)))
            : RunNotFound().ToProblem();

    private static async Task<Results<NoContent, ProblemHttpResult>> CancelAsync(EvalRunId id, IEvalRunStore runs, IEvalRunControl control, CancellationToken ct)
    {
        if (await runs.GetAsync(id, ct) is null)
            return RunNotFound().ToProblem();

        await control.CancelAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteRunAsync(EvalRunId id, IEvalRunStore runs, IEvalRunControl control, CancellationToken ct)
    {
        if (await runs.GetAsync(id, ct) is { Status: EvalRunStatus.Queued or EvalRunStatus.Running })
            await control.CancelAsync(id, ct);

        return await runs.DeleteAsync(id, ct) is { IsSuccess: false } failed ? failed.Error.ToProblem() : TypedResults.NoContent();
    }

    private static async Task<Result<(CandidateConfig, IReadOnlyList<EvalCase>)>> ValidateAsync(
        EvalRunRequest request, IEvalCaseStore cases, EvalModels models, ITemplateCatalog templates, CancellationToken ct)
    {
        if (request.CaseIds is not { Count: > 0 })
            return Invalid("Select at least one case.");

        var tiers = new Dictionary<ModelTier, string>();
        foreach ((string tier, string key) in request.Models ?? new Dictionary<string, string>())
        {
            if (!Enum.TryParse(tier, ignoreCase: true, out ModelTier parsed) || int.TryParse(tier, out _))
                return Invalid($"Unknown model tier '{tier}'.");
            if (!models.Exists(key))
                return Invalid($"There is no configured model '{key}'.");
            tiers[parsed] = key;
        }

        if (request.JudgeModel is { } judge && !models.Exists(judge))
            return Invalid($"There is no configured model '{judge}'.");
        if (request.Template is { } template && templates.Find(template) is null)
            return Invalid($"There is no template '{template}'.");

        List<EvalCase> selected = [];
        foreach (EvalCaseId id in request.CaseIds.Distinct())
        {
            if (await cases.GetAsync(id, ct) is not { } evalCase)
                return Invalid($"There is no eval case '{id}'.");
            selected.Add(evalCase);
        }

        string? basePrompt = string.IsNullOrWhiteSpace(request.BasePrompt) ? null : request.BasePrompt;
        return (new CandidateConfig(tiers, request.Template, basePrompt, request.JudgeModel), selected);
    }

    private static Error Invalid(string message) => new("evals.invalid_request", message, ErrorType.Validation);

    private static Error CaseNotFound() => new("eval_case.not_found", "The eval case does not exist.", ErrorType.NotFound);

    private static Error RunNotFound() => new("eval_run.not_found", "The eval run does not exist.", ErrorType.NotFound);
}

/// <summary>A rating of a run.</summary>
/// <param name="Rating"><c>up</c> or <c>down</c>.</param>
/// <param name="Note">What should have happened (at most 1,000 characters).</param>
public sealed record RatingRequest(string Rating, string? Note = null);

/// <summary>The expected behavior note of an eval case.</summary>
/// <param name="Note">The note; empty or null removes it.</param>
public sealed record EvalCaseNoteRequest(string? Note);

/// <summary>Cases and the candidate configuration of an eval run.</summary>
/// <param name="CaseIds">Cases to replay, in order.</param>
/// <param name="Models">Configured model key per tier (<c>fast</c>, <c>standard</c>, <c>advisor</c>); tiers left out use the current routing.</param>
/// <param name="Template">Agent template; null keeps <c>default</c>.</param>
/// <param name="BasePrompt">Replaces Marshal's base instructions; null keeps them.</param>
/// <param name="JudgeModel">Configured model key that scores replies; null for no judge.</param>
public sealed record EvalRunRequest(
    IReadOnlyList<EvalCaseId>? CaseIds, IReadOnlyDictionary<string, string>? Models, string? Template = null, string? BasePrompt = null, string? JudgeModel = null);

/// <param name="EstimatedCost">Estimated model cost of the replays and judge calls.</param>
public sealed record EvalEstimate(decimal EstimatedCost);

/// <summary>The candidate configuration of an eval run.</summary>
/// <param name="Models">Model key per tier (<c>fast</c>, <c>standard</c>, <c>advisor</c>).</param>
/// <param name="Template">Agent template override.</param>
/// <param name="BasePrompt">Base prompt override.</param>
/// <param name="JudgeModel">Judge model, if any.</param>
public sealed record EvalCandidateView(IReadOnlyDictionary<string, string> Models, string? Template, string? BasePrompt, string? JudgeModel);

/// <summary>An eval run with its totals.</summary>
public sealed record EvalRunView(
    EvalRunId Id,
    EvalCandidateView Candidate,
    EvalRunStatus Status,
    IReadOnlyList<EvalCaseId> CaseIds,
    string StartedBy,
    decimal EstimatedCost,
    decimal Cost,
    int Pass,
    int Changed,
    int Diverged,
    int Failed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndedAt,
    string? Error)
{
    public static EvalRunView From(EvalRunRecord run) => new(
        run.Id,
        new EvalCandidateView(
            run.Candidate.Models.ToDictionary(model => model.Key.ToString().ToLowerInvariant(), model => model.Value, StringComparer.Ordinal),
            run.Candidate.Template,
            run.Candidate.BasePrompt,
            run.Candidate.JudgeModel),
        run.Status,
        run.CaseIds,
        run.StartedBy,
        run.EstimatedCost,
        run.Cost,
        run.Pass,
        run.Changed,
        run.Diverged,
        run.Failed,
        run.CreatedAt,
        run.EndedAt,
        run.Error);
}

/// <summary>An eval run and its case results.</summary>
/// <param name="Run">The run with its totals.</param>
/// <param name="Results">Per case result in replay order.</param>
public sealed record EvalRunReport(EvalRunView Run, IReadOnlyList<EvalCaseResult> Results);
