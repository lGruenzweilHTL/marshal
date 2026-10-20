using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marshal.Abstractions.Evals;

/// <summary>
/// Canonical JSON for comparing tool arguments: keys sorted ordinally at every level, numbers normalized
/// (<c>1.50</c> and <c>1.5</c> are equal), no insignificant whitespace.
/// </summary>
public static class CanonicalJson
{
    /// <summary>The canonical form of <paramref name="json"/>; text that is not JSON is returned as it is.</summary>
    public static string Of(string json)
    {
        try
        {
            return Of(JsonNode.Parse(json));
        }
        catch (JsonException)
        {
            return json;
        }
    }

    public static string Of(JsonElement element) => Of(JsonNode.Parse(element.GetRawText()));

    public static string Of(JsonNode? node)
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
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number && decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number):
                writer.WriteRawValue((number / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture));
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}
