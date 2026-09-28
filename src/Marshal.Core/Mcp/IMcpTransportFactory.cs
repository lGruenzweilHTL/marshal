using Marshal.Abstractions.Mcp;
using ModelContextProtocol.Client;

namespace Marshal.Core.Mcp;

/// <summary>
/// Creates the client transport for a server (stdio child process or streamable HTTP).
/// </summary>
public interface IMcpTransportFactory
{
    /// <summary>A new, unconnected transport for <paramref name="settings"/>.</summary>
    IClientTransport Create(McpConnectionSettings settings);
}
