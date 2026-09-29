using System.Collections.Concurrent;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tasks;
using Microsoft.Extensions.AI;

namespace Marshal.Tasks.Running;

/// <summary>
/// Transcripts of interactive runs moved to the background, kept in memory until the continuation task's first run
/// takes them. After a restart the task runs from its goal instead.
/// </summary>
public sealed class TaskContinuations
{
    private readonly ConcurrentDictionary<TaskId, Continuation> _continuations = new();

    public void Add(TaskId task, Continuation continuation) => _continuations[task] = continuation;

    /// <summary>Takes the continuation of <paramref name="task"/>; a transcript continues only once.</summary>
    public bool TryTake(TaskId task, out Continuation continuation) => _continuations.TryRemove(task, out continuation!);
}

/// <summary>An interrupted run's transcript and the untrusted sources it had read.</summary>
/// <param name="Transcript">Messages after the system prompt, up to the last completed tool round.</param>
/// <param name="Taint">Taint sources of the interrupted run; the continuation stays tainted.</param>
public sealed record Continuation(IReadOnlyList<ChatMessage> Transcript, IReadOnlyList<TaintSource> Taint);
