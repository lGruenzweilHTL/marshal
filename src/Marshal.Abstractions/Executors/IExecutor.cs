using System.Text.Json;

namespace Marshal.Abstractions.Executors;

/// <summary>
/// Long external work (e.g. a coding session on the executor host). Starting returns the executor's handle at once;
/// progress and completion arrive later as callbacks.
/// </summary>
public interface IExecutor
{
    /// <summary>Name the model uses, e.g. <c>claude_code</c>.</summary>
    string Name { get; }

    /// <summary>What the executor does and what its spec looks like (shown to the model).</summary>
    string Description { get; }

    /// <summary>Checks a spec before anything is created; fails with <c>executor.invalid_spec</c>.</summary>
    Result ValidateSpec(JsonElement spec);

    /// <summary>Starts a session.</summary>
    Task<Result<ExecutorStarted>> StartAsync(ExecutorStart start, CancellationToken cancellationToken);

    /// <summary>Stops a running session.</summary>
    Task<Result> CancelAsync(string handle, CancellationToken cancellationToken);
}

/// <summary>A session to start.</summary>
/// <param name="SessionId">Marshal's id of the session (part of the callback URL).</param>
/// <param name="Spec">The validated spec.</param>
/// <param name="CallbackUrl">Where the executor posts progress and completion.</param>
/// <param name="CallbackToken">The session's <c>executor_callback</c> token.</param>
public sealed record ExecutorStart(Guid SessionId, JsonElement Spec, Uri CallbackUrl, string CallbackToken);

/// <summary>A started session.</summary>
/// <param name="Handle">The executor's own id for the session.</param>
/// <param name="Target">What the session works on, e.g. the repository (the target of its proposal).</param>
public sealed record ExecutorStarted(string Handle, string Target);
