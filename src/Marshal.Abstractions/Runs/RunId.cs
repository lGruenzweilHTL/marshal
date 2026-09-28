using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Runs;

/// <summary>
/// Id of a run (one agent loop execution).
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<RunId>))]
public readonly record struct RunId(Guid Value) : IGuidId<RunId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static RunId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static RunId From(Guid value) => new(value);

    /// <inheritdoc />
    public static RunId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out RunId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
