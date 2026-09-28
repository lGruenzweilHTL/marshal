using Marshal.Abstractions.Guard;

namespace Marshal.Core.Guard;

/// <summary>
/// Classifies a user reply to a confirmation question. Sees only the action summary and the reply.
/// </summary>
public interface IConfirmationJudge
{
    Task<JudgeVerdict> JudgeAsync(string actionSummary, string userReply, string language, CancellationToken cancellationToken);
}
