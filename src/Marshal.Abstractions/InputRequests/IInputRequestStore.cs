namespace Marshal.Abstractions.InputRequests;

/// <summary>
/// Persists input requests for the current owner.
/// </summary>
public interface IInputRequestStore
{
    /// <summary>Stores a new request.</summary>
    Task SaveAsync(InputRequestRecord request, CancellationToken cancellationToken);

    /// <summary>A request of the current owner by id, or null.</summary>
    Task<InputRequestRecord?> GetAsync(InputRequestId id, CancellationToken cancellationToken);

    /// <summary>The current owner's pending requests, oldest first.</summary>
    Task<IReadOnlyList<InputRequestRecord>> ListPendingAsync(CancellationToken cancellationToken);

    /// <summary>Moves a pending request to a final status. Fails with <c>input_request.not_pending</c> when already decided.</summary>
    Task<Result> DecideAsync(InputRequestId id, InputRequestStatus status, CancellationToken cancellationToken);
}
