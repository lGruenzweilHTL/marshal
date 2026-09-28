namespace Marshal.Abstractions.Mcp;

/// <summary>A server definition with its secrets resolved, used to open one connection.</summary>
/// <param name="Server">The definition.</param>
/// <param name="Headers">HTTP headers (e.g. <c>Authorization</c>).</param>
/// <param name="Environment">Environment variables for a stdio child process.</param>
public sealed record McpConnectionSettings(
    McpServerDefinition Server,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Environment);
