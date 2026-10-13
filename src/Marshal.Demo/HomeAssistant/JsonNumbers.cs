using System.Globalization;
using System.Text.Json.Nodes;

namespace Marshal.Demo.HomeAssistant;

/// <summary>Reads a number from a JSON node whether it was parsed from text or built in code (int, double, ...).</summary>
internal static class JsonNumbers
{
    public static double? Number(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture)
            : null;
}
