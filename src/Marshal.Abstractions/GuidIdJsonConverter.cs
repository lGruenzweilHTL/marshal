using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions;

/// <summary>
/// Serializes a strongly typed Guid id as its plain Guid string.
/// </summary>
/// <typeparam name="TId">The id type.</typeparam>
public sealed class GuidIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct, IGuidId<TId>
{
    /// <inheritdoc />
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out Guid value))
            return TId.From(value);

        throw new JsonException($"Expected a GUID string for {typeof(TId).Name}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);

    /// <inheritdoc />
    public override TId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Guid.TryParse(reader.GetString(), out Guid value)
            ? TId.From(value)
            : throw new JsonException($"Expected a GUID property name for {typeof(TId).Name}.");

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value.ToString());
}
