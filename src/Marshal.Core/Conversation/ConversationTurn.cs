using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;

namespace Marshal.Core.Conversation;

/// <summary>
/// One interactive turn as received by the conversation endpoint.
/// </summary>
public sealed record ConversationTurn
{
    /// <summary>Owner of the calling principal (token owner or logged-in user).</summary>
    public required OwnerId PrincipalOwner { get; init; }

    /// <summary>The token of a token client; null for cookie clients.</summary>
    public ApiTokenId? ApiTokenId { get; init; }

    public required SessionSource Source { get; init; }

    /// <summary>Client conversation id (token clients) or Marshal's session id (cookie clients).</summary>
    public string? ConversationId { get; init; }

    public required string Text { get; init; }

    public string? Language { get; init; }

    public required IdentityEvidence Evidence { get; init; }

    public InputMode InputMode { get; init; } = InputMode.Text;

    /// <summary>Opaque follow-up reference; unknown references are ignored.</summary>
    public string? FollowupRef { get; init; }
}
