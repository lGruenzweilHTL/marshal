using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Apps;

/// <summary>
/// Id of a MCP app instance.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<AppInstanceId>))]
public readonly record struct AppInstanceId(Guid Value) : IGuidId<AppInstanceId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static AppInstanceId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static AppInstanceId From(Guid value) => new(value);

    /// <inheritdoc />
    public static AppInstanceId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AppInstanceId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
