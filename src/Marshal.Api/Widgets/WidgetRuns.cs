using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Api.Auth;

namespace Marshal.Api.Widgets;

/// <summary>
/// Records a widget action (or an MCP app's request) as a run of its own: source <c>web</c>, trigger
/// <c>widget:&lt;kind&gt;</c>, the signed-in user as owner. The action itself runs inside <see cref="RunAsync"/>.
/// </summary>
internal sealed class WidgetRuns(IRunRecorder recorder, IExecutionContextAccessor executionContext, IUserProfileProvider profiles, IToolRegistry registry)
{
    /// <summary>Starts the run, runs <paramref name="action"/> in its context and completes the run with the action's error, if any.</summary>
    public Task<Result<T>> RunAsync<T>(ClaimsPrincipal user, string kind, Func<MarshalExecutionContext, Task<Result<T>>> action, CancellationToken cancellationToken) =>
        RunTriggeredAsync(user, $"widget:{kind}", action, cancellationToken);

    /// <summary>Like <see cref="RunAsync{T}"/> with another trigger (MCP app requests: <c>app:&lt;server&gt;/&lt;tool&gt;</c>).</summary>
    public async Task<Result<T>> RunTriggeredAsync<T>(ClaimsPrincipal user, string trigger, Func<MarshalExecutionContext, Task<Result<T>>> action, CancellationToken cancellationToken)
    {
        OwnerId owner = user.Owner();
        Result<UserProfile> profile = await profiles.GetAsync(owner, cancellationToken);
        var context = new MarshalExecutionContext
        {
            OwnerId = owner,
            Source = SessionSource.Web,
            InputMode = InputMode.Text,
            Language = profile.IsSuccess ? profile.Value.Language : "en",
            Tools = registry.Current,
            // Nobody confirms inside a widget run: a call the guard raises to confirm tier goes to the approval queue.
            IsInteractive = false,
        };

        RunId runId;
        using (executionContext.Use(context))
            runId = await recorder.StartRunAsync(new RunStart(context, trigger), cancellationToken);

        context = context with { RunId = runId };
        using IDisposable scope = executionContext.Use(context);
        Result<T> result = await action(context);
        await recorder.CompleteRunAsync(runId, new RunCompletion(result.IsSuccess ? RunOutcome.Ok : RunOutcome.Error, result.IsFailure ? result.Error.Code : null, context.Taint.Sources), cancellationToken);
        return result;
    }

    /// <summary>Records a step of the current widget run.</summary>
    public Task<int> RecordAsync(MarshalExecutionContext context, RunStepRecord step, CancellationToken cancellationToken) =>
        recorder.RecordStepAsync(context.RunId!.Value, step, cancellationToken);
}
