namespace Marshal.Core.Mcp;

/// <summary>
/// One <c>/config/mcp/*.yaml</c> file as written (camelCase keys, unknown keys are errors).
/// </summary>
public sealed class McpServerFile
{
    public string? Id { get; set; }

    /// <summary><c>stdio</c> or <c>http</c> (streamable HTTP).</summary>
    public string? Transport { get; set; }

    public string? Url { get; set; }

    public string? Command { get; set; }

    public List<string> Args { get; set; } = [];

    public string? ToolPrefix { get; set; }

    /// <summary>One-line English description of the server's tools, used for tool retrieval.</summary>
    public string? Description { get; set; }

    public string? AuthTokenSecret { get; set; }

    /// <summary>Environment variables of a stdio server taken from secrets.</summary>
    public List<EnvironmentSecret> EnvSecrets { get; set; } = [];

    public bool Untrusted { get; set; }

    /// <summary>Globs on reported tool names whose output is third-party content.</summary>
    public List<string> UntrustedTools { get; set; } = [];

    /// <summary>New or changed tools wait for approval after the first discovery (default true).</summary>
    public bool PinTools { get; set; } = true;

    /// <summary>Render the server's MCP Apps UIs in web chat (default false).</summary>
    public bool Apps { get; set; }

    /// <summary>A <c>variable</c> filled from a <c>secret</c> name.</summary>
    public sealed class EnvironmentSecret
    {
        public string? Variable { get; set; }

        public string? Secret { get; set; }
    }
}
