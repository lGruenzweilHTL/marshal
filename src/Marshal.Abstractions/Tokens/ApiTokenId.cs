using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Tokens;

/// <summary>
/// Id of a machine API token.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<ApiTokenId>))]
public readonly record struct ApiTokenId(Guid Value) : IGuidId<ApiTokenId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static ApiTokenId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static ApiTokenId From(Guid value) => new(value);

    /// <inheritdoc />
    public static ApiTokenId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ApiTokenId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
