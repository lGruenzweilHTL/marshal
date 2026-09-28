using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Memory;

/// <summary>
/// Deterministic id of a memory chunk: 26 lowercase base32 characters of a hash over owner, scope, project, type
/// and normalized title. Chunks are never merged by similarity; the same identity always addresses the same id.
/// </summary>
[JsonConverter(typeof(ChunkIdJsonConverter))]
public readonly record struct ChunkId : IParsable<ChunkId>
{
    /// <summary>Length of an id.</summary>
    public const int Length = 26;

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    private ChunkId(string value) => Value = value;

    /// <summary>The id.</summary>
    public string Value { get; }

    /// <summary>Wraps a computed id; the value must be valid.</summary>
    public static ChunkId From(string value) =>
        IsValid(value) ? new ChunkId(value) : throw new ArgumentException($"'{value}' is not a chunk id.", nameof(value));

    /// <inheritdoc />
    public static ChunkId Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out ChunkId id) ? id : throw new FormatException($"'{s}' is not a chunk id.");

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ChunkId result)
    {
        bool ok = IsValid(s);
        result = ok ? new ChunkId(s!) : default;
        return ok;
    }

    /// <summary>Lowercase base32 (RFC 4648 alphabet) of the first 130 bits of <paramref name="hash"/>.</summary>
    public static ChunkId FromHash(ReadOnlySpan<byte> hash)
    {
        if (hash.Length * 8 < Length * 5)
            throw new ArgumentException("The hash is too short for a chunk id.", nameof(hash));

        Span<char> chars = stackalloc char[Length];
        for (int index = 0; index < Length; index++)
        {
            int bit = index * 5;
            int value = (hash[bit / 8] << 8) | (bit / 8 + 1 < hash.Length ? hash[bit / 8 + 1] : 0);
            chars[index] = Alphabet[(value >> (11 - bit % 8)) & 31];
        }

        return new ChunkId(new string(chars));
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsValid([NotNullWhen(true)] string? value) =>
        value is { Length: Length } && value.All(Alphabet.Contains);

    private sealed class ChunkIdJsonConverter : JsonConverter<ChunkId>
    {
        public override ChunkId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TryParse(reader.GetString(), null, out ChunkId id) ? id : throw new JsonException("Invalid chunk id.");

        public override void Write(Utf8JsonWriter writer, ChunkId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }
}
