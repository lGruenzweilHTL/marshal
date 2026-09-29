using System.Text.Json;
using Marshal.Abstractions;

namespace Marshal.Memory.Tools;

/// <summary>Reads memory tool arguments.</summary>
internal static class ToolArguments
{
    public static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? Integer(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number)
            ? number
            : null;

    /// <summary>Parses a snake_case enum value.</summary>
    public static bool TryEnum<T>(string value, out T result)
        where T : struct, Enum
    {
        foreach (T candidate in Enum.GetValues<T>())
        {
            if (JsonNamingPolicy.SnakeCaseLower.ConvertName(candidate.ToString()) == value)
            {
                result = candidate;
                return true;
            }
        }

        result = default;
        return false;
    }

    public static Error Invalid(string message) => new("memory.invalid_arguments", message, ErrorType.Validation);
}
