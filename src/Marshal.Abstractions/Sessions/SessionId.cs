using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Sessions;

/// <summary>
/// Id of a conversation session.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<SessionId>))]
public readonly record struct SessionId(Guid Value) : IGuidId<SessionId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static SessionId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static SessionId From(Guid value) => new(value);

    /// <inheritdoc />
    public static SessionId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SessionId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
