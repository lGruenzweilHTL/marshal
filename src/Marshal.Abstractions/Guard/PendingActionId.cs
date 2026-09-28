using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// Id of a frozen pending action.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<PendingActionId>))]
public readonly record struct PendingActionId(Guid Value) : IGuidId<PendingActionId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static PendingActionId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static PendingActionId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PendingActionId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PendingActionId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
