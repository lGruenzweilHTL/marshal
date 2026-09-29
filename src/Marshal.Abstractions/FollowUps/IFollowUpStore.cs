using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.FollowUps;

/// <summary>
/// Voice questions and deliveries that expect a reply, for the current owner.
/// </summary>
public interface IFollowUpStore
{
    /// <summary>Stores a follow-up with a fresh nonce; the nonce goes out as <c>marshal-followup:&lt;nonce&gt;</c>.</summary>
    Task<FollowUpRecord> CreateAsync(NewFollowUp followUp, CancellationToken cancellationToken);
}

/// <summary>A follow-up to create.</summary>
/// <param name="SatelliteId">Satellite the question was spoken on (replies from others are ignored).</param>
/// <param name="TaskId">Task whose result was delivered or that waits for the reply.</param>
/// <param name="PendingActionId">Pending action the question asks to confirm.</param>
public sealed record NewFollowUp(string SatelliteId, TaskId? TaskId = null, PendingActionId? PendingActionId = null);

/// <summary>A stored follow-up.</summary>
public sealed record FollowUpRecord(Guid Id, string Nonce, string SatelliteId, TaskId? TaskId, PendingActionId? PendingActionId, DateTimeOffset ExpiresAt, DateTimeOffset? ConsumedAt)
{
    /// <summary>Prefix of the <c>extra_system_prompt</c> that carries the nonce.</summary>
    public const string PromptPrefix = "marshal-followup:";

    public string Prompt => PromptPrefix + Nonce;
}
