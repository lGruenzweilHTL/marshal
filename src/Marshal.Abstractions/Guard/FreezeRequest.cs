using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// What to freeze. The owner comes from the execution context.
/// </summary>
public sealed record FreezeRequest(
    SessionId? SessionId,
    string? SatelliteId,
    ToolId Tool,
    string ArgumentsJson,
    string Summary,
    IReadOnlyList<ConfirmationReason> Reasons,
    string Language,
    DateTimeOffset ExpiresAt);
