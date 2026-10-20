using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Evals;

/// <summary>
/// Id of an eval case.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<EvalCaseId>))]
public readonly record struct EvalCaseId(Guid Value) : IGuidId<EvalCaseId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static EvalCaseId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static EvalCaseId From(Guid value) => new(value);

    /// <inheritdoc />
    public static EvalCaseId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out EvalCaseId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
