using System.Collections;
using Marshal.Abstractions.Mcp;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Marshal.Core.Mcp;

/// <summary>
/// Streamable HTTP transports (resolved headers on every request) and stdio child processes. A child process
/// does not inherit Marshal's environment, which may hold secrets: it gets only the basics needed to run
/// (<c>PATH</c>, <c>HOME</c>, locale, time zone) and the secrets configured for that server.
/// </summary>
public sealed class McpTransportFactory(IHttpClientFactory httpClients, ILoggerFactory loggers) : IMcpTransportFactory
{
    /// <summary>Name of the <see cref="HttpClient"/> used for HTTP servers.</summary>
    public const string HttpClientName = "mcp";

    private static readonly string[] _inheritedVariables = ["PATH", "HOME", "LANG", "LC_ALL", "TZ", "TMPDIR"];

    public IClientTransport Create(McpConnectionSettings settings)
    {
        McpServerDefinition server = settings.Server;
        return server.Transport switch
        {
            McpTransportKind.Http => new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Name = server.Id,
                    Endpoint = server.Url ?? throw new InvalidOperationException($"MCP server '{server.Id}' has no URL."),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = settings.Headers.ToDictionary(StringComparer.OrdinalIgnoreCase),
                },
                httpClients.CreateClient(HttpClientName),
                loggers,
                ownsHttpClient: true),
            McpTransportKind.Stdio => new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Name = server.Id,
                    Command = server.Command ?? throw new InvalidOperationException($"MCP server '{server.Id}' has no command."),
                    Arguments = [.. server.Arguments],
                    InheritEnvironmentVariables = false,
                    EnvironmentVariables = new Dictionary<string, string?>(ChildEnvironment(settings, CurrentEnvironment())),
                },
                loggers),
            _ => throw new InvalidOperationException($"Unknown MCP transport '{server.Transport}'."),
        };
    }

    /// <summary>Environment of a stdio child: inherited basics plus the server's resolved secrets.</summary>
    public static IReadOnlyDictionary<string, string?> ChildEnvironment(McpConnectionSettings settings, IReadOnlyDictionary<string, string?> parent)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in _inheritedVariables)
        {
            if (parent.TryGetValue(name, out string? value) && value is not null)
                environment[name] = value;
        }

        foreach ((string name, string value) in settings.Environment)
            environment[name] = value;

        return environment;
    }

    private static Dictionary<string, string?> CurrentEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value, StringComparer.Ordinal);
}
