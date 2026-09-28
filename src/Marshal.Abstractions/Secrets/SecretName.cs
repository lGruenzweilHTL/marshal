using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Marshal.Abstractions.Secrets;

/// <summary>
/// Name of a secret (<c>^[a-z][a-z0-9_]{0,63}$</c>). Configuration and data rows hold only names;
/// values come from <see cref="ISecretStore"/>.
/// </summary>
[JsonConverter(typeof(SecretNameJsonConverter))]
public readonly partial record struct SecretName
{
    private SecretName(string value) => Value = value;

    /// <summary>The name.</summary>
    public string Value { get; }

    /// <summary>Validates and creates a secret name.</summary>
    public static Result<SecretName> Create(string? value) =>
        value is not null && Pattern().IsMatch(value)
            ? new SecretName(value)
            : new Error("secret.invalid_name", "A secret name must match ^[a-z][a-z0-9_]{0,63}$.", ErrorType.Validation);

    /// <summary>True when <paramref name="value"/> is a valid secret name.</summary>
    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    private sealed class SecretNameJsonConverter : JsonConverter<SecretName>
    {
        public override SecretName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Result<SecretName> result = Create(reader.GetString());
            return result.IsSuccess ? result.Value : throw new JsonException(result.Error.Message);
        }

        public override void Write(Utf8JsonWriter writer, SecretName value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }
}
