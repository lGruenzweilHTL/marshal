#pragma warning disable CA1711 // "Queue" is the domain term (the review queue), not a collection type.

using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.InputRequests;

/// <summary>
/// Input requests of MCP servers, next to the approvals in the review queue. A request freezes the interrupted call;
/// only a user action in the web UI answers it, and the answer repeats the call once.
/// </summary>
public interface IInputRequestQueue
{
    /// <summary>
    /// Freezes a request for the context's owner with the approval TTL, makes sure a task waits for the outcome (the
    /// asking background task, or a new one for the turn's conversation) and sends a push with a link and no actions.
    /// </summary>
    Task<InputRequestRecord> CreateAsync(NewInputRequest request, MarshalExecutionContext context, CancellationToken cancellationToken);

    /// <summary>A request of the current owner, in any status, or null.</summary>
    Task<InputRequestRecord?> GetAsync(InputRequestId id, CancellationToken cancellationToken);

    /// <summary>The current owner's pending requests, oldest first.</summary>
    Task<IReadOnlyList<InputRequestRecord>> ListPendingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Answers a pending request (form: <paramref name="content"/> validated against the requested schema; URL: null
    /// for "done"), repeats the frozen call and resumes the waiting task with the outcome. Fails when the request is
    /// unknown, already decided, expired or the answer does not fit the schema.
    /// </summary>
    Task<Result<InputRequestRecord>> AnswerAsync(InputRequestId id, System.Text.Json.JsonElement? content, CancellationToken cancellationToken);

    /// <summary>Declines a pending request: nothing is sent to the server; the task resumes with the refusal.</summary>
    Task<Result<InputRequestRecord>> DeclineAsync(InputRequestId id, CancellationToken cancellationToken);
}
