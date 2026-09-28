using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Memory;

/// <summary>
/// Stable id of a memory project. Chunk ids use it, never the project name, so renaming keeps them.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<ProjectId>))]
public readonly record struct ProjectId(Guid Value) : IGuidId<ProjectId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static ProjectId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static ProjectId From(Guid value) => new(value);

    /// <inheritdoc />
    public static ProjectId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ProjectId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
