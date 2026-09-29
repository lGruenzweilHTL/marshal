using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Tools;
using Marshal.Memory.Projects;

namespace Marshal.Memory.Tools;

/// <summary>
/// <c>memory.correct_project(kind, project?)</c>: the user corrects or confirms an inferred project ("not that
/// project", "that's about X", "yes"). It refers to the previous turn, whose inference the user reacted to (the
/// current turn when there is none). Every correction and confirmation is stored as feedback; rejected phrases become
/// negative examples, switched and confirmed phrases count towards alias promotion. Hidden when no projects exist.
/// </summary>
public sealed class MemoryCorrectProjectTool(ProjectSessions sessions, ProjectLearning learning, IMemoryProjects projects) : ITool
{
    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = new ToolId("memory", "correct_project"),
        Description = "Corrects the project Marshal inferred for the conversation: 'rejected' when the user says it is not about that project, 'switched' with the right project, 'confirmed' when the user confirms it.",
        ParametersSchema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "kind":{"type":"string","enum":["rejected","switched","confirmed"]},
              "project":{"type":"string","description":"Project name: required for switched; for rejected or confirmed only when several projects were active."}},
             "required":["kind"]}
            """).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
    };

    public async ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
        await projects.AnyAsync(cancellationToken);

    public string Summarize(JsonElement arguments, string language) =>
        $"project {ToolArguments.Text(arguments, "kind")} {ToolArguments.Text(arguments, "project")}".TrimEnd();

    public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        string? kindText = ToolArguments.Text(invocation.Arguments, "kind");
        if (kindText is null || !ToolArguments.TryEnum(kindText, out ProjectFeedbackKind kind))
            return ToolArguments.Invalid("kind must be rejected, switched or confirmed.");

        string? projectName = ToolArguments.Text(invocation.Arguments, "project");
        if (kind == ProjectFeedbackKind.Switched && projectName is null)
            return ToolArguments.Invalid("switched needs the right project.");
        if (invocation.Context.SessionId is not { } session)
            return new Error("memory.no_session", "Project corrections need a conversation session.", ErrorType.Validation);

        ProjectId? named = null;
        if (projectName is not null)
        {
            named = await projects.FindAsync(projectName, cancellationToken);
            if (named is null)
                return new Error("memory.project_not_found", $"There is no project '{projectName}'.", ErrorType.NotFound);
        }

        ProjectSessionState state = sessions.Get(session);
        ProjectTurn? target;
        List<ProjectActivation> inferred;
        lock (state)
        {
            target = state.PreviousTurn ?? state.LastTurn;
            inferred = [.. new[] { state.PreviousTurn, state.LastTurn }
                .Where(turn => turn is not null)
                .SelectMany(turn => turn!.Activations)
                .Where(activation => activation.IsInferred || activation.Reason == ActivationReason.Sticky)
                .DistinctBy(activation => activation.ProjectId)];
        }

        if (target is null)
            return NothingToCorrect();

        return kind switch
        {
            ProjectFeedbackKind.Rejected => await RejectAsync(state, target, named is { } id ? [id] : [.. inferred.Select(activation => activation.ProjectId)], invocation, cancellationToken),
            ProjectFeedbackKind.Switched => await SwitchAsync(state, target, named!.Value, [.. inferred.Select(activation => activation.ProjectId).Where(id => id != named)], invocation, cancellationToken),
            _ => await ConfirmAsync(state, target, named ?? inferred.FirstOrDefault()?.ProjectId, invocation, cancellationToken),
        };
    }

    private async Task<Result<ToolOutput>> RejectAsync(ProjectSessionState state, ProjectTurn target, List<ProjectId> rejected, ToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (rejected.Count == 0)
            return NothingToCorrect();

        Unload(state, rejected);
        foreach (ProjectId project in rejected)
            await learning.RecordAsync(project, ProjectFeedbackKind.Rejected, target.Phrase, target.Embedding, invocation.Context.RunId, cancellationToken);

        return new ToolOutput("The project is unloaded for this conversation. Answer without it.");
    }

    private async Task<Result<ToolOutput>> SwitchAsync(ProjectSessionState state, ProjectTurn target, ProjectId project, List<ProjectId> previous, ToolInvocation invocation, CancellationToken cancellationToken)
    {
        Unload(state, previous);
        lock (state)
        {
            state.Rejected.Remove(project);
            state.Sticky[project] = 1;
        }

        foreach (ProjectId old in previous)
            await learning.RecordAsync(old, ProjectFeedbackKind.Rejected, target.Phrase, target.Embedding, invocation.Context.RunId, cancellationToken);
        await learning.RecordAsync(project, ProjectFeedbackKind.Switched, target.Phrase, target.Embedding, invocation.Context.RunId, cancellationToken);
        await learning.ConfirmAliasAsync(project, target.Phrase, cancellationToken);
        return new ToolOutput("The project is switched; its memory is loaded from the next turn on.");
    }

    private async Task<Result<ToolOutput>> ConfirmAsync(ProjectSessionState state, ProjectTurn target, ProjectId? project, ToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (project is not { } confirmed)
            return NothingToCorrect();

        lock (state)
        {
            target.Handled = true;
        }

        await learning.RecordAsync(confirmed, ProjectFeedbackKind.Confirmed, target.Phrase, target.Embedding, invocation.Context.RunId, cancellationToken);
        await learning.ConfirmAliasAsync(confirmed, target.Phrase, cancellationToken);
        return new ToolOutput("Noted.");
    }

    // The corrected turns' inferences must not count as confirmations later.
    private static void Unload(ProjectSessionState state, List<ProjectId> projects)
    {
        lock (state)
        {
            foreach (ProjectId project in projects)
            {
                state.Sticky.Remove(project);
                state.Rejected.Add(project);
            }

            foreach (ProjectTurn turn in state.Turns)
                turn.Handled = true;
        }
    }

    private static Error NothingToCorrect() =>
        new("memory.nothing_to_correct", "No project was inferred in this conversation.", ErrorType.Validation);
}
