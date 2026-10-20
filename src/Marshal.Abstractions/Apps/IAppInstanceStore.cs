using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;

namespace Marshal.Abstractions.Apps;

/// <summary>
/// MCP app instances: the UI one tool call showed in a web turn, kept so the app renders again after a reload.
/// Reads are scoped to the current owner.
/// </summary>
public interface IAppInstanceStore
{
    Task<AppInstanceId> CreateAsync(NewAppInstance instance, CancellationToken cancellationToken);

    /// <summary>The current owner's instance, or null (unknown or another owner's).</summary>
    Task<AppInstanceRecord?> GetAsync(AppInstanceId id, CancellationToken cancellationToken);
}

/// <param name="OwnerId">The owner of the turn.</param>
/// <param name="SessionId">The turn's session, when it has one.</param>
/// <param name="RunId">The run of the call.</param>
/// <param name="ToolCallId">The model's tool call id.</param>
/// <param name="Arguments">The call's arguments (JSON object).</param>
/// <param name="App">Server, tool, resource and HTML.</param>
/// <param name="Result">The call's <c>CallToolResult</c> JSON; null when it was too large to keep.</param>
public sealed record NewAppInstance(OwnerId OwnerId, SessionId? SessionId, RunId RunId, string ToolCallId, string Arguments, McpAppOutput App, string? Result);

/// <param name="Id">The instance id (the app id the chat uses).</param>
/// <param name="Server">The MCP server id.</param>
/// <param name="Tool">The tool name as the server reports it.</param>
/// <param name="ToolCallId">The model's tool call id.</param>
/// <param name="ResourceUri">The <c>ui://</c> resource.</param>
/// <param name="Html">The stored resource HTML.</param>
/// <param name="Arguments">The call's arguments (JSON object).</param>
/// <param name="Result">The call's <c>CallToolResult</c> JSON; null when it was too large to keep.</param>
/// <param name="CreatedAt">When the call ran.</param>
public sealed record AppInstanceRecord(
    AppInstanceId Id, string Server, string Tool, string ToolCallId, string ResourceUri, string Html, string Arguments, string? Result, DateTimeOffset CreatedAt);

/// <summary>An MCP app a turn showed: kept with the reply so a reopened conversation renders it again.</summary>
/// <param name="AppId">The stored app instance.</param>
/// <param name="Server">The MCP server id.</param>
/// <param name="Tool">The tool name as the server reports it.</param>
/// <param name="CallId">The tool call that produced it.</param>
public sealed record AppRef(AppInstanceId AppId, string Server, string Tool, string CallId);
