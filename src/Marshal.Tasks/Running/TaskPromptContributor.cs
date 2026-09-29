using Marshal.Abstractions.Context;

namespace Marshal.Tasks.Running;

/// <summary>
/// Rules for task runs: nobody waits for the reply, and the final reply is the task's result (a short speakable
/// first paragraph, then details).
/// </summary>
public sealed class TaskPromptContributor : IContextContributor
{
    public const string Rules = """
        You are running a background task. Nobody is waiting for your reply right now; work through the goal with your tools.
        You do not see earlier runs of this task. Everything you need to know from them is in the task notes.
        When the goal is reached, reply with the result: first one or two short sentences that can be spoken aloud (no markdown), then a blank line, then the details in markdown if there are any.
        """;

    public int Order => 450;

    public Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Context.TaskId is null ? null : Rules);
}
