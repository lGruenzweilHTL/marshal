using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marshal.Tasks.Store;

/// <summary>
/// JSON shape of the task columns that hold structured values (budget, spend, deliveries).
/// </summary>
internal static class TaskJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"Stored {typeof(T).Name} JSON is null.");
}
