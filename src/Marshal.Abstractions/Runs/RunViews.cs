using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Runs;

/// <summary>
/// Filter for the runs list.
/// </summary>
/// <param name="Source">Only runs from this source.</param>
/// <param name="Outcome">Only runs with this outcome.</param>
/// <param name="MinCost">Only runs costing at least this much.</param>
/// <param name="Before">Only runs started before this time (paging).</param>
/// <param name="Limit">Maximum number of runs (1-200).</param>
/// <param name="TaskId">Only runs of this task.</param>
public sealed record RunFilter(SessionSource? Source = null, RunOutcome? Outcome = null, decimal? MinCost = null, DateTimeOffset? Before = null, int Limit = 50, TaskId? TaskId = null)
{
    /// <summary>Order of the list; newest first by default.</summary>
    public RunSort Sort { get; init; } = RunSort.Newest;
}

/// <summary>Order of the runs list.</summary>
public enum RunSort
{
    /// <summary>Newest start first.</summary>
    Newest,

    /// <summary>Longest time to first delta first; runs without a delta are left out.</summary>
    SlowestFirstDelta,
}

/// <summary>
/// A run in the runs list.
/// </summary>
/// <param name="Id">Run id.</param>
/// <param name="SessionId">Conversation the run belongs to, if any.</param>
/// <param name="Source">voice, web, task or api.</param>
/// <param name="InputMode">voice or text.</param>
/// <param name="Trigger">What started the run.</param>
/// <param name="SatelliteId">Satellite of a voice run.</param>
/// <param name="StartedAt">Start.</param>
/// <param name="EndedAt">End, when finished.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="OutcomeReason">Why, when not a normal end.</param>
/// <param name="Tainted">True when the run read untrusted content.</param>
/// <param name="Cost">Cost of the run's model calls.</param>
public sealed record RunSummary(
    RunId Id,
    SessionId? SessionId,
    SessionSource Source,
    InputMode? InputMode,
    string Trigger,
    string? SatelliteId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    RunOutcome Outcome,
    string? OutcomeReason,
    bool Tainted,
    decimal Cost)
{
    /// <summary>Milliseconds from the request to the first streamed delta; null when the run sent none.</summary>
    public int? TimeToFirstDeltaMs { get; init; }
}

/// <summary>
/// A run with its steps.
/// </summary>
/// <param name="Run">The run.</param>
/// <param name="Language">Reply language.</param>
/// <param name="TaintSources">Why the run is tainted.</param>
/// <param name="Steps">Context build, model calls, tool calls and reply in order.</param>
public sealed record RunDetail(RunSummary Run, string? Language, IReadOnlyList<TaintSource> TaintSources, IReadOnlyList<RunStepDetail> Steps)
{
    /// <summary>Tool namespaces the run loaded, in load order, with why (<c>core</c>, <c>retrieval</c> with its score, ...).</summary>
    public IReadOnlyList<Tools.LoadedToolNamespace> LoadedNamespaces { get; init; } = [];
}

/// <summary>
/// One step of a run as recorded (sensitive tool results only as <paramref name="Reference"/>).
/// </summary>
/// <param name="Sequence">Position in the run.</param>
/// <param name="Kind">Step kind.</param>
/// <param name="Model">Model of a model call.</param>
/// <param name="Tier">Model tier of a model call.</param>
/// <param name="TokensIn">Input tokens.</param>
/// <param name="TokensOut">Output tokens.</param>
/// <param name="TokensCached">Cached input tokens.</param>
/// <param name="FirstTokenMs">Time to first token.</param>
/// <param name="DurationMs">Duration.</param>
/// <param name="Cost">Cost of a model call.</param>
/// <param name="Tool">Tool of a tool call.</param>
/// <param name="RiskTier">Resolved risk tier.</param>
/// <param name="GuardDecision">Guard decision.</param>
/// <param name="Reasons">Confirmation reasons.</param>
/// <param name="Input">User input (context step).</param>
/// <param name="Content">Prompt, tool result or reply (truncated).</param>
/// <param name="Arguments">Tool arguments.</param>
/// <param name="Reference">Reference stored instead of a redacted result.</param>
/// <param name="ContinueConversation">Whether the reply asked something.</param>
/// <param name="Error">Error, if the step failed.</param>
/// <param name="Depth">Delegation depth: 0 for the run's own loop, 1 and more for subagent steps.</param>
/// <param name="Notes">Context steps: active projects, retrieved memory and similar notes of the context build.</param>
/// <param name="Reasoning">Model calls: the reasoning the model returned.</param>
/// <param name="ReasoningWithheld">Model calls: reasoning followed a redacted tool result and was not kept.</param>
public sealed record RunStepDetail(
    int Sequence,
    RunStepKind Kind,
    string? Model,
    string? Tier,
    int? TokensIn,
    int? TokensOut,
    int? TokensCached,
    int? FirstTokenMs,
    int DurationMs,
    decimal? Cost,
    string? Tool,
    RiskTier? RiskTier,
    GuardDecision? GuardDecision,
    IReadOnlyList<ConfirmationReason> Reasons,
    string? Input,
    string? Content,
    string? Arguments,
    string? Reference,
    bool? ContinueConversation,
    string? Error,
    int Depth,
    IReadOnlyList<Context.ContextNote> Notes,
    string? Reasoning,
    bool ReasoningWithheld);

/// <summary>
/// Cost over a date range.
/// </summary>
/// <param name="TimeZone">Time zone the days are counted in.</param>
/// <param name="From">First day.</param>
/// <param name="To">Last day.</param>
/// <param name="Days">One entry per day with cost.</param>
/// <param name="Total">Sum including the advisor.</param>
/// <param name="ByTier">Sum per model tier, without the advisor.</param>
/// <param name="Advisor">Advisor sum.</param>
public sealed record CostReport(string TimeZone, DateOnly From, DateOnly To, IReadOnlyList<DailyCost> Days, decimal Total, IReadOnlyDictionary<string, decimal> ByTier, decimal Advisor);

/// <summary>
/// Cost of one local day.
/// </summary>
/// <param name="Date">The day.</param>
/// <param name="Total">Sum including the advisor.</param>
/// <param name="ByTier">Sum per model tier, without the advisor.</param>
/// <param name="Advisor">Advisor sum.</param>
public sealed record DailyCost(DateOnly Date, decimal Total, IReadOnlyDictionary<string, decimal> ByTier, decimal Advisor);
