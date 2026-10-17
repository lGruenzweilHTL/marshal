using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.InputRequests;

/// <summary>
/// Id of a pending input request.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<InputRequestId>))]
public readonly record struct InputRequestId(Guid Value) : IGuidId<InputRequestId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static InputRequestId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static InputRequestId From(Guid value) => new(value);

    /// <inheritdoc />
    public static InputRequestId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out InputRequestId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
