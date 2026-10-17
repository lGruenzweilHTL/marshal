namespace Marshal.Core.Mcp;

/// <summary>Ends an MCP call whose server asked for user input; the call is frozen as a pending input request.</summary>
internal sealed class McpInputRequestedException() : Exception("The server needs input from the user.");

/// <summary>The server asked for input that cannot be attributed to a call or asked at all.</summary>
internal sealed class McpInputUnavailableException(string message) : Exception(message);
