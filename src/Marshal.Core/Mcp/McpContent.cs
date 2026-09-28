using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Marshal.Core.Mcp;

/// <summary>
/// Turns MCP tool results into the text given to the model: text content verbatim, other content summarized.
/// </summary>
internal static class McpContent
{
    public static string ToText(CallToolResult result)
    {
        if (result.Content.Count == 0)
            return result.StructuredContent is { } structured ? JsonSerializer.Serialize(structured) : string.Empty;

        return string.Join('\n', result.Content.Select(Describe));
    }

    private static string Describe(ContentBlock block) => block switch
    {
        TextContentBlock text => text.Text,
        ImageContentBlock image => $"[image: {image.MimeType}]",
        AudioContentBlock audio => $"[audio: {audio.MimeType}]",
        EmbeddedResourceBlock { Resource: TextResourceContents resource } => resource.Text,
        EmbeddedResourceBlock embedded => $"[resource: {embedded.Resource.Uri} ({embedded.Resource.MimeType})]",
        ResourceLinkBlock link => $"[resource: {link.Name} {link.Uri}]",
        _ => $"[{block.Type} content]",
    };
}
