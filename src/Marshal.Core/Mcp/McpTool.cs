using Marshal.Abstractions;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Mcp;

/// <summary>
/// One tool of an MCP server; calls go through the source's current connection.
/// </summary>
internal sealed class McpTool(McpToolSource source, ToolDescriptor descriptor, string name) : ITool
{
    public ToolDescriptor Descriptor => descriptor;

    public ToolPinState PinState => source.PinStateOf(name);

    /// <summary>The tool name as the server reports it.</summary>
    public string Name => name;

    public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken) =>
        source.CallAsync(name, invocation.Arguments, invocation.Context, cancellationToken);
}
