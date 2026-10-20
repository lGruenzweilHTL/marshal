using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Marshal.Core.Mcp;

/// <summary>
/// A tool definition as pinned: canonical JSON of everything that reaches the prompt or the guard (name, title,
/// description, input and output schema, annotations, MCP Apps <c>_meta.ui</c>) and its SHA-256.
/// </summary>
/// <param name="Json">Canonical JSON: keys sorted, no insignificant whitespace, absent fields left out.</param>
/// <param name="Fingerprint">Lowercase hex SHA-256 of <paramref name="Json"/> (UTF-8).</param>
public sealed record McpToolDefinition(string Json, string Fingerprint);

/// <summary>
/// Fingerprints MCP tools for pinning. Key order and whitespace never change the hash; any change to a field the
/// model sees does.
/// </summary>
public static class McpToolFingerprint
{
    public static McpToolDefinition Of(Tool tool)
    {
        var definition = new JsonObject { ["name"] = tool.Name };
        if (tool.Title is not null)
            definition["title"] = tool.Title;

        if (tool.Description is not null)
            definition["description"] = tool.Description;

        definition["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText());
        if (tool.OutputSchema is { } output)
            definition["outputSchema"] = JsonNode.Parse(output.GetRawText());

        if (tool.Annotations is not null)
            definition["annotations"] = JsonSerializer.SerializeToNode(tool.Annotations, McpJsonUtilities.DefaultOptions);

        // Which UI a tool shows (MCP Apps) is part of its definition; the UI's HTML is not.
        if (tool.Meta?["ui"] is { } ui)
            definition["_meta"] = new JsonObject { ["ui"] = ui.DeepClone() };

        string json = Canonical(definition);
        return new McpToolDefinition(json, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
    }

    /// <summary>Canonical form of any JSON text (keys sorted ordinally at every level, compact).</summary>
    public static string Canonical(JsonNode? node)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
            Write(writer, node);

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach ((string key, JsonNode? value) in obj.OrderBy(property => property.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    Write(writer, value);
                }

                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (JsonNode? item in array)
                    Write(writer, item);

                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}
