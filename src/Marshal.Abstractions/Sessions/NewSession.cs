using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;

namespace Marshal.Abstractions.Sessions;

/// <summary>
/// A session to create for the current owner.
/// </summary>
public sealed record NewSession(SessionSource Source, ApiTokenId? ApiTokenId, string? ExternalId, IdentityEvidence Evidence, string ResolvedBy);
