using System.Text.Json;

namespace Marshal.Core.AiTasks;

/// <summary>
/// Validates JSON against the JSON Schema subset Home Assistant's AI task structures produce: <c>type</c> (object,
/// string, number, integer, boolean, array, null, or a list of these), <c>properties</c>, <c>required</c>,
/// <c>items</c> and <c>enum</c>. Other keywords are ignored.
/// </summary>
public static class JsonSchemaSubsetValidator
{
    /// <summary>Validation errors as <c>path: message</c>; empty when <paramref name="instance"/> matches.</summary>
    public static IReadOnlyList<string> Validate(JsonElement schema, JsonElement instance)
    {
        var errors = new List<string>();
        Check(schema, instance, "$", errors);
        return errors;
    }

    private static void Check(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return;

        if (schema.TryGetProperty("type", out JsonElement type))
        {
            string[] types = type.ValueKind switch
            {
                JsonValueKind.String => [type.GetString()!],
                JsonValueKind.Array => [.. type.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)],
                _ => [],
            };
            if (types.Length > 0 && !types.Any(name => Matches(name, instance)))
            {
                errors.Add($"{path}: expected {string.Join(" or ", types)}");
                return;
            }
        }

        if (schema.TryGetProperty("enum", out JsonElement allowed) && allowed.ValueKind == JsonValueKind.Array
            && !allowed.EnumerateArray().Any(value => JsonElement.DeepEquals(value, instance)))
        {
            errors.Add($"{path}: must be one of {string.Join(", ", allowed.EnumerateArray().Select(Display))}");
            return;
        }

        if (instance.ValueKind == JsonValueKind.Object)
        {
            CheckObject(schema, instance, path, errors);
        }
        else if (instance.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out JsonElement items))
        {
            int index = 0;
            foreach (JsonElement item in instance.EnumerateArray())
                Check(items, item, $"{path}[{index++}]", errors);
        }
    }

    private static void CheckObject(JsonElement schema, JsonElement instance, string path, List<string> errors)
    {
        if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement name in required.EnumerateArray())
            {
                if (name.ValueKind == JsonValueKind.String && !instance.TryGetProperty(name.GetString()!, out _))
                    errors.Add($"{path}.{name.GetString()}: required property is missing");
            }
        }

        if (!schema.TryGetProperty("properties", out JsonElement properties) || properties.ValueKind != JsonValueKind.Object)
            return;

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            if (instance.TryGetProperty(property.Name, out JsonElement value))
                Check(property.Value, value, $"{path}.{property.Name}", errors);
        }
    }

    private static bool Matches(string type, JsonElement instance) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => instance.ValueKind == JsonValueKind.Null,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetDecimal(out decimal value) && value == decimal.Truncate(value),
        _ => true,
    };

    private static string Display(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
}
