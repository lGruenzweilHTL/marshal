using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Evals;

/// <summary>
/// Answers tool calls of a replay from the eval case's recording instead of executing them. A call matches a
/// recorded call with the same tool id and canonical arguments: the next unused one in recorded order, or an already
/// used one when the candidate repeats a call. Anything else gets "no recording" and marks the replay diverged.
/// </summary>
public sealed class RecordedToolExecutor(IReadOnlyList<EvalToolCall> recorded)
{
    /// <summary>Error code of a call that has no recording.</summary>
    public const string NoRecording = "no_recording";

    private readonly bool[] _used = new bool[recorded.Count];
    private readonly List<ReplayedCall> _calls = [];
    private readonly Lock _lock = new();

    /// <summary>The candidate's calls in order.</summary>
    public IReadOnlyList<ReplayedCall> Calls
    {
        get
        {
            lock (_lock)
                return [.. _calls];
        }
    }

    public Result<ToolOutput> Execute(ToolId tool, JsonElement arguments)
    {
        string id = tool.ToString();
        string canonical = CanonicalJson.Of(arguments);
        lock (_lock)
        {
            int match = Find(id, canonical, unusedOnly: true);
            if (match < 0)
                match = Find(id, canonical, unusedOnly: false);

            _calls.Add(new ReplayedCall(id, canonical, match >= 0));
            if (match < 0)
                return new Error(NoRecording, "No recording: this call was not made in the recorded run, so the replay cannot answer it.", ErrorType.NotFound);

            _used[match] = true;
            EvalToolCall call = recorded[match];
            return call.Error is { } error
                ? new Error("replay.recorded_error", error)
                : new ToolOutput(call.Result ?? "");
        }
    }

    private int Find(string tool, string arguments, bool unusedOnly)
    {
        for (int index = 0; index < recorded.Count; index++)
        {
            if ((!unusedOnly || !_used[index]) && recorded[index].Tool == tool && recorded[index].Arguments == arguments)
                return index;
        }

        return -1;
    }
}

/// <summary>A tool of the replay snapshot: same descriptor, answers from the recording.</summary>
internal sealed class RecordedTool(ITool inner, RecordedToolExecutor executor) : ITool
{
    public ToolDescriptor Descriptor => inner.Descriptor;

    public ToolPinState PinState => inner.PinState;

    public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken) =>
        Task.FromResult(executor.Execute(inner.Descriptor.Id, invocation.Arguments));

    public string Summarize(JsonElement arguments, string language) => inner.Summarize(arguments, language);

    public Result Validate(JsonElement arguments) => inner.Validate(arguments);

    public ValueTask<bool> IsAvailableAsync(Abstractions.Execution.MarshalExecutionContext context, CancellationToken cancellationToken) =>
        inner.IsAvailableAsync(context, cancellationToken);
}
