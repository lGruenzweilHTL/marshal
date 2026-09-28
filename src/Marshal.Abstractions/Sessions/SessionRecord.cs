using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;

namespace Marshal.Abstractions.Sessions;

/// <summary>
/// A conversation session. Per-turn data (input mode, satellite, area, language) lives on runs, not here.
/// </summary>
public sealed record SessionRecord(
    SessionId Id,
    OwnerId OwnerId,
    SessionSource Source,
    ApiTokenId? ApiTokenId,
    string? ExternalId,
    string ResolvedBy,
    DateTimeOffset LastTurnAt);
