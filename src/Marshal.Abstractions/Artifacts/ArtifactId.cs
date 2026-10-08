using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Artifacts;

/// <summary>
/// Id of an artifact.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<ArtifactId>))]
public readonly record struct ArtifactId(Guid Value) : IGuidId<ArtifactId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static ArtifactId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static ArtifactId From(Guid value) => new(value);

    /// <inheritdoc />
    public static ArtifactId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ArtifactId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
