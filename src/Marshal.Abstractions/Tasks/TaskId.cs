using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Tasks;

/// <summary>
/// Id of a background task.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<TaskId>))]
public readonly record struct TaskId(Guid Value) : IGuidId<TaskId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static TaskId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static TaskId From(Guid value) => new(value);

    /// <inheritdoc />
    public static TaskId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TaskId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
