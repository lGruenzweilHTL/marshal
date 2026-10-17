using Marshal.Abstractions.Models;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Templates;

/// <summary>
/// An agent template (<c>/config/templates/&lt;name&gt;.md</c>): what a run with it may see and do.
/// </summary>
/// <param name="Name">Template name (equals the file name without <c>.md</c>).</param>
/// <param name="Description">When to use it; shown to callers of <c>agent.delegate</c>.</param>
/// <param name="ModelTier">Model tier; null = by input mode (voice fast, otherwise standard).</param>
/// <param name="Tools">Allowlist of tool id globs.</param>
/// <param name="Skills">Skills available to runs with this template; null = all.</param>
/// <param name="MaxSteps">Step limit; null = <c>agent.maxSteps</c>.</param>
/// <param name="MaxAdvisorCalls">Advisor calls per run; null = <c>agent.advisor.maxCallsPerRun</c>.</param>
/// <param name="Prompt">Prompt body added to the system prompt.</param>
public sealed record AgentTemplate(
    string Name,
    string Description,
    ModelTier? ModelTier,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string>? Skills,
    int? MaxSteps,
    int? MaxAdvisorCalls,
    string Prompt)
{
    /// <summary>Shortest time between a task run and its wakeup; null = <c>tasks.minWakeupInterval</c>.</summary>
    public TimeSpan? MinWakeupInterval { get; init; }

    /// <summary>Name of the template used for conversation turns.</summary>
    public const string DefaultName = "default";

    /// <summary>The built-in default: every tool, every skill, no extra prompt.</summary>
    public static AgentTemplate BuiltInDefault { get; } = new(DefaultName, "General assistant for conversation turns.", null, ["*"], null, null, null, "");

    /// <summary>Name of the template used for AI task runs from Home Assistant automations.</summary>
    public const string AiTaskName = "ai_task";

    /// <summary>The built-in AI task template: fast tier, no tools, a prompt that rules out questions.</summary>
    public static AgentTemplate BuiltInAiTask { get; } = new(
        AiTaskName,
        "Answers a Home Assistant automation's instructions.",
        Models.ModelTier.Fast,
        [],
        null,
        null,
        null,
        "You run inside a home automation. Answer the instructions; do not ask questions; nobody can answer them.");

    /// <summary>True for the templates Marshal itself uses for a kind of run (<c>default</c>, <c>ai_task</c>); never a delegation target.</summary>
    public bool IsBuiltInRole => Name is DefaultName or AiTaskName;

    /// <summary>True when the allowlist admits <paramref name="tool"/>.</summary>
    public bool Allows(ToolId tool) => Tools.Any(pattern => ToolGlob.Matches(pattern, tool));

    /// <summary>True when <paramref name="skill"/> is available to runs with this template.</summary>
    public bool AllowsSkill(string skill) => Skills is null || Skills.Contains(skill, StringComparer.Ordinal);
}
