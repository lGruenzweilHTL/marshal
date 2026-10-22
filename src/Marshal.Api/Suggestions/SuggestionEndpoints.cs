using System.Security.Claims;
using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Suggestions;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Tasks.SkillProposals;
using Marshal.Tasks.Suggestions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Suggestions;

/// <summary>
/// Suggestions (list, accept, dismiss; detector counts, mutes and resets) and skill proposals (list, detail, download,
/// dismiss). Accepting, dismissing and downloading are audited user actions.
/// </summary>
internal static class SuggestionEndpoints
{
    public static RouteGroupBuilder MapSuggestionEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder suggestions = group.MapGroup("/suggestions").WithTags("Suggestions").RequireAuthorization(Policies.Human);
        suggestions.MapGet("/", ListAsync).WithName("ListSuggestions").WithDescription("The user's open suggestions, newest first.");
        suggestions.MapPost("/{id:guid}/accept", (Guid id, ClaimsPrincipal user, SuggestionResponder responder, IAuditLog audit, CancellationToken ct) =>
                AnswerAsync(id, accept: true, user, responder, audit, ct))
            .WithName("AcceptSuggestion")
            .WithDescription("Accepts a suggestion; its proposed action, if any, runs once through the guard.");
        suggestions.MapPost("/{id:guid}/dismiss", (Guid id, ClaimsPrincipal user, SuggestionResponder responder, IAuditLog audit, CancellationToken ct) =>
                AnswerAsync(id, accept: false, user, responder, audit, ct))
            .WithName("DismissSuggestion")
            .WithDescription("Dismisses a suggestion; nothing runs.");
        suggestions.MapGet("/detectors", ListDetectorsAsync).WithName("ListSuggestionDetectors")
            .WithDescription("Every detector with the user's counts, acceptance estimate and mute.");
        suggestions.MapPut("/detectors/{detector}/mute", MuteAsync).WithName("MuteSuggestionDetector")
            .WithDescription("Mutes a detector until a time, or indefinitely without one.");
        suggestions.MapDelete("/detectors/{detector}/mute", UnmuteAsync).WithName("UnmuteSuggestionDetector").WithDescription("Unmutes a detector.");
        suggestions.MapPost("/detectors/{detector}/reset", ResetAsync).WithName("ResetSuggestionDetector")
            .WithDescription("Sets a detector's counts back to zero; a mute stays.");

        RouteGroupBuilder skills = group.MapGroup("/skill-proposals").WithTags("Suggestions").RequireAuthorization(Policies.Human);
        skills.MapGet("/", ListSkillProposalsAsync).WithName("ListSkillProposals").WithDescription("Open and downloaded skill proposals, newest first.");
        skills.MapGet("/{id:guid}", GetSkillProposalAsync).WithName("GetSkillProposal").WithDescription("One skill proposal with its SKILL.md and diff.");
        skills.MapGet("/{id:guid}/download", DownloadAsync).WithName("DownloadSkillProposal")
            .WithDescription("The drafted SKILL.md as a file; marks the proposal downloaded.");
        skills.MapPost("/{id:guid}/dismiss", DismissSkillProposalAsync).WithName("DismissSkillProposal")
            .WithDescription("Dismisses a skill proposal; the same workflow is not proposed again.");
        return group;
    }

    private static async Task<Ok<IReadOnlyList<SuggestionItem>>> ListAsync(ISuggestionStore store, IPendingActionStore pending, TimeProvider time, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        var items = new List<SuggestionItem>();
        foreach (SuggestionRecord record in await store.ListOpenAsync(ct))
        {
            if (record.ExpiresAt <= now)
                continue;

            IReadOnlyList<string> reasons = record.PendingActionId is { } action && await pending.GetAsync(action, ct) is { } frozen
                ? [.. frozen.Reasons.Select(reason => reason.Sentence)]
                : [];
            items.Add(SuggestionItem.From(record, reasons));
        }

        return TypedResults.Ok<IReadOnlyList<SuggestionItem>>(items);
    }

    private static async Task<Results<Ok<SuggestionAnswerItem>, ProblemHttpResult>> AnswerAsync(
        Guid id, bool accept, ClaimsPrincipal user, SuggestionResponder responder, IAuditLog audit, CancellationToken ct)
    {
        Result<SuggestionAnswer> answered = await responder.AnswerAsync(id, accept, DecidedBy.Ui, ct);
        if (answered.IsFailure && answered.Error.Type == ErrorType.NotFound)
            return answered.Error.ToProblem();

        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, accept ? "suggestion.accept" : "suggestion.dismiss", id.ToString(), new
        {
            detector = answered.IsSuccess ? answered.Value.Suggestion.Detector : null,
            outcome = answered.IsSuccess ? "ok" : answered.Error.Code,
        }), ct);
        return answered.IsSuccess
            ? TypedResults.Ok(new SuggestionAnswerItem(SuggestionItem.From(answered.Value.Suggestion, []), answered.Value.ActionResult))
            : answered.Error.ToProblem();
    }

    private static async Task<Ok<IReadOnlyList<DetectorItem>>> ListDetectorsAsync(IEnumerable<ISuggestionDetector> detectors, ISuggestionStore store, TimeProvider time, CancellationToken ct)
    {
        var stats = (await store.ListStatsAsync(ct)).ToDictionary(stat => stat.Detector, StringComparer.Ordinal);
        DateTimeOffset now = time.GetUtcNow();
        IEnumerable<string> ids = detectors.Select(detector => detector.Id).Concat(stats.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return TypedResults.Ok<IReadOnlyList<DetectorItem>>([.. ids.Select(id => DetectorItem.From(stats.GetValueOrDefault(id) ?? new SuggestionDetectorStats(id, 0, 0, 0), now))]);
    }

    private static async Task<Results<Ok<DetectorItem>, ProblemHttpResult>> MuteAsync(string detector, MuteRequest request, ISuggestionStore store, TimeProvider time, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (request.Until is { } until && until <= now)
            return new Error("suggestions.invalid_mute", "The mute must end in the future.", ErrorType.Validation).ToProblem();

        await store.MuteAsync(detector, request.Until ?? DateTimeOffset.MaxValue, ct);
        return TypedResults.Ok(DetectorItem.From(await store.GetStatsAsync(detector, ct), now));
    }

    private static async Task<Ok<DetectorItem>> UnmuteAsync(string detector, ISuggestionStore store, TimeProvider time, CancellationToken ct)
    {
        await store.MuteAsync(detector, null, ct);
        return TypedResults.Ok(DetectorItem.From(await store.GetStatsAsync(detector, ct), time.GetUtcNow()));
    }

    private static async Task<Ok<DetectorItem>> ResetAsync(string detector, ISuggestionStore store, TimeProvider time, CancellationToken ct)
    {
        await store.ResetAsync(detector, ct);
        return TypedResults.Ok(DetectorItem.From(await store.GetStatsAsync(detector, ct), time.GetUtcNow()));
    }

    private static async Task<Ok<IReadOnlyList<SkillProposalSummary>>> ListSkillProposalsAsync(ISkillProposalStore store, CancellationToken ct) =>
        TypedResults.Ok<IReadOnlyList<SkillProposalSummary>>([.. (await store.ListAsync(ct)).Select(SkillProposalSummary.From)]);

    private static async Task<Results<Ok<SkillProposalRecord>, ProblemHttpResult>> GetSkillProposalAsync(Guid id, ISkillProposalStore store, CancellationToken ct) =>
        await store.GetAsync(id, ct) is { } found ? TypedResults.Ok(found) : SkillProposalErrors.NotFound().ToProblem();

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadAsync(
        Guid id, ClaimsPrincipal user, ISkillProposalStore store, IAuditLog audit, CancellationToken ct)
    {
        Result<SkillProposalRecord> marked = await store.MarkDownloadedAsync(id, ct);
        if (marked.IsFailure)
            return marked.Error.ToProblem();

        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, "skill_proposal.download", id.ToString(), new { name = marked.Value.Name }), ct);
        return TypedResults.File(Encoding.UTF8.GetBytes(marked.Value.Content), "text/markdown; charset=utf-8", "SKILL.md");
    }

    private static async Task<Results<Ok<SkillProposalSummary>, ProblemHttpResult>> DismissSkillProposalAsync(
        Guid id, ClaimsPrincipal user, ISkillProposalStore store, IAuditLog audit, CancellationToken ct)
    {
        Result<SkillProposalRecord> dismissed = await store.DismissAsync(id, ct);
        if (dismissed.IsFailure)
            return dismissed.Error.ToProblem();

        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, "skill_proposal.dismiss", id.ToString(), new { name = dismissed.Value.Name }), ct);
        return TypedResults.Ok(SkillProposalSummary.From(dismissed.Value));
    }
}

/// <summary>An open suggestion in the Inbox.</summary>
/// <param name="Id">Suggestion id.</param>
/// <param name="Detector">Detector id, e.g. <c>window_rain</c>.</param>
/// <param name="Text">The worded suggestion.</param>
/// <param name="Delivery"><c>inbox</c> or <c>push</c>.</param>
/// <param name="Status"><c>open</c>, <c>accepted</c>, <c>dismissed</c> or <c>expired</c>.</param>
/// <param name="Snippet">Text to show verbatim below the suggestion (a YAML snippet); null for none.</param>
/// <param name="ActionSummary">What Accept runs; null without a proposed action.</param>
/// <param name="ActionReasons">Why the proposed action is confirm tier (Accept confirms it); empty for a safe one.</param>
/// <param name="CreatedAt">When it was sent.</param>
/// <param name="ExpiresAt">When it expires unanswered.</param>
internal sealed record SuggestionItem(
    Guid Id, string Detector, string Text, string? Snippet, string Delivery, string Status, string? ActionSummary, IReadOnlyList<string> ActionReasons, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)
{
    public static SuggestionItem From(SuggestionRecord record, IReadOnlyList<string> reasons) => new(
        record.Id, record.Detector, record.Text, record.Snippet, record.Delivery.ToString().ToLowerInvariant(), record.Status.ToString().ToLowerInvariant(),
        record.ActionSummary, reasons, record.CreatedAt, record.ExpiresAt);
}

/// <summary>The answered suggestion and what its proposed action returned.</summary>
internal sealed record SuggestionAnswerItem(SuggestionItem Suggestion, string? ActionResult);

/// <summary>One detector's counts for the user.</summary>
/// <param name="Detector">Detector id.</param>
/// <param name="Sent">Suggestions sent since the last reset.</param>
/// <param name="Accepted">Accepted ones.</param>
/// <param name="Dismissed">Dismissed ones.</param>
/// <param name="Estimate">(accepted + 1) / (accepted + dismissed + 2).</param>
/// <param name="Muted">Whether it is muted now.</param>
/// <param name="MutedUntil">End of the mute; null when indefinite or not muted.</param>
internal sealed record DetectorItem(string Detector, int Sent, int Accepted, int Dismissed, double Estimate, bool Muted, DateTimeOffset? MutedUntil)
{
    public static DetectorItem From(SuggestionDetectorStats stats, DateTimeOffset now) => new(
        stats.Detector, stats.Sent, stats.Accepted, stats.Dismissed, Math.Round(stats.Estimate, 3), stats.IsMuted(now),
        stats.IsMuted(now) && stats.MutedUntil != DateTimeOffset.MaxValue ? stats.MutedUntil : null);
}

/// <summary>Mute a detector until <paramref name="Until"/>, or indefinitely when it is null.</summary>
internal sealed record MuteRequest(DateTimeOffset? Until);

/// <summary>A skill proposal in the Inbox list.</summary>
internal sealed record SkillProposalSummary(Guid Id, string Name, string Description, string Status, bool HasDiff, int SourceRunCount, DateTimeOffset CreatedAt)
{
    public static SkillProposalSummary From(SkillProposalRecord record) => new(
        record.Id, record.Name, record.Description, record.Status.ToString().ToLowerInvariant(), record.Diff is not null, record.SourceRuns.Count, record.CreatedAt);
}
