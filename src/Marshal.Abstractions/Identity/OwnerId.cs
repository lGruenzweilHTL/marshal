using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Identity;

/// <summary>
/// Id of a user. Every user-owned row carries one; a user's id is always an <see cref="OwnerId"/>.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<OwnerId>))]
[TypeConverter(typeof(GuidIdTypeConverter<OwnerId>))]
public readonly record struct OwnerId(Guid Value) : IGuidId<OwnerId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static OwnerId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static OwnerId From(Guid value) => new(value);

    /// <inheritdoc />
    public static OwnerId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out OwnerId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
