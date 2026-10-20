using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Evals;

/// <summary>
/// Id of an eval run.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<EvalRunId>))]
public readonly record struct EvalRunId(Guid Value) : IGuidId<EvalRunId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static EvalRunId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static EvalRunId From(Guid value) => new(value);

    /// <inheritdoc />
    public static EvalRunId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out EvalRunId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
