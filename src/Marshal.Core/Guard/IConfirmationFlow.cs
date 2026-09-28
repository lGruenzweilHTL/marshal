using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Sessions;

namespace Marshal.Core.Guard;

/// <summary>
/// Decides a session's pending action from the next user turn. Only user turns reach this; tool and model output
/// never do.
/// </summary>
public interface IConfirmationFlow
{
    Task<ConfirmationOutcome> HandleUserTurnAsync(SessionId sessionId, string userText, MarshalExecutionContext context, CancellationToken cancellationToken);
}
