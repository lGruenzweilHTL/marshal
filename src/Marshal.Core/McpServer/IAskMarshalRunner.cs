using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tokens;

namespace Marshal.Core.McpServer;

/// <summary>
/// Runs one Marshal turn for an external agent (<c>ask_marshal</c> on Marshal's MCP server).
/// </summary>
public interface IAskMarshalRunner
{
    Task<Result<AskResult>> AskAsync(AskRequest request, CancellationToken cancellationToken);
}

/// <param name="Owner">The token owner.</param>
/// <param name="Token">The calling token; sessions are looked up by token and conversation id.</param>
/// <param name="Text">What the external agent asks.</param>
/// <param name="ConversationId">Continues the session of an earlier call; null starts a new one.</param>
/// <param name="Language">Reply language; null = the owner's preference.</param>
public sealed record AskRequest(OwnerId Owner, ApiTokenId Token, string Text, string? ConversationId, string? Language);

/// <param name="RunId">The turn's run.</param>
/// <param name="Reply">Marshal's reply.</param>
/// <param name="ConversationId">Pass it again to continue the conversation.</param>
public sealed record AskResult(RunId RunId, string Reply, string ConversationId);

/// <summary>Error codes of <see cref="IAskMarshalRunner"/>.</summary>
public static class AskErrors
{
    public const string DailyCapCode = "mcp.daily_cap";

    public const string FailedCode = "mcp.ask_failed";

    public static Error DailyCap() => new(DailyCapCode, "The daily cost cap is reached. Marshal does not run more turns today.", ErrorType.Conflict);
}
