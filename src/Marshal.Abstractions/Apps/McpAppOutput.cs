namespace Marshal.Abstractions.Apps;

/// <summary>
/// The MCP Apps UI of a tool call (<c>ui://</c> resource of a server with <c>apps: true</c>), read for a web turn.
/// The HTML is server-authored and only ever rendered in a sandboxed iframe.
/// </summary>
/// <param name="Server">The MCP server id.</param>
/// <param name="Tool">The tool name as the server reports it.</param>
/// <param name="ResourceUri">The <c>ui://</c> resource.</param>
/// <param name="Html">The resource's HTML (at most <see cref="MaxHtmlBytes"/>).</param>
public sealed record McpAppOutput(string Server, string Tool, string ResourceUri, string Html)
{
    /// <summary>Largest app resource Marshal renders (UTF-8 bytes).</summary>
    public const int MaxHtmlBytes = 1024 * 1024;

    /// <summary>Largest tool result kept for an app (UTF-8 bytes of the JSON); a bigger one is not kept.</summary>
    public const int MaxResultBytes = 256 * 1024;
}
