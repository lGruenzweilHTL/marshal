using System.Globalization;
using System.Text.RegularExpressions;

namespace Marshal.Abstractions.Notifications;

/// <summary>
/// A delivery condition <c>state(&lt;entity_id&gt; &lt;op&gt; &lt;value&gt;)</c> on a Home Assistant entity state, with
/// <c>=</c>, <c>!=</c> (text, case-insensitive) or <c>&gt;</c>, <c>&lt;</c> (numeric). <c>$person</c> stands for the
/// owner's person entity. Areas are not entities: <c>area.*</c> is rejected (use an occupancy sensor).
/// </summary>
/// <param name="Entity">Entity id, or <see cref="Person"/>.</param>
/// <param name="Operator">Comparison.</param>
/// <param name="Value">Value to compare the state with.</param>
public sealed partial record StateCondition(string Entity, ConditionOperator Operator, string Value)
{
    /// <summary>Placeholder for the owner's <c>person.*</c> entity.</summary>
    public const string Person = "$person";

    public static Result<StateCondition> Parse(string text)
    {
        Match match = Syntax().Match(text);
        if (!match.Success)
            return Invalid($"'{text}' is not a condition like state(binary_sensor.kitchen_occupancy = on).");

        string entity = match.Groups["entity"].Value;
        if (entity.StartsWith("area.", StringComparison.Ordinal))
            return Invalid($"'{entity}' is an area, not an entity; use an occupancy sensor such as binary_sensor.kitchen_occupancy.");

        ConditionOperator op = match.Groups["op"].Value switch
        {
            "=" => ConditionOperator.Equal,
            "!=" => ConditionOperator.NotEqual,
            ">" => ConditionOperator.Greater,
            _ => ConditionOperator.Less,
        };
        string value = match.Groups["value"].Value.Trim();
        if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
            value = value[1..^1];
        if (op is ConditionOperator.Greater or ConditionOperator.Less && Number(value) is null)
            return Invalid($"'{text}' compares with > or < but '{value}' is not a number.");

        return new StateCondition(entity, op, value);
    }

    /// <summary>Whether <paramref name="state"/> satisfies the condition; an unknown state never does.</summary>
    public bool HoldsFor(string? state)
    {
        if (state is null || state is "unknown" or "unavailable")
            return false;

        return Operator switch
        {
            ConditionOperator.Equal => string.Equals(state, Value, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotEqual => !string.Equals(state, Value, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Greater => Number(state) is { } number && number > Number(Value),
            _ => Number(state) is { } number && number < Number(Value),
        };
    }

    public override string ToString() => $"state({Entity} {Symbol(Operator)} {Value})";

    private static string Symbol(ConditionOperator op) => op switch
    {
        ConditionOperator.Equal => "=",
        ConditionOperator.NotEqual => "!=",
        ConditionOperator.Greater => ">",
        _ => "<",
    };

    private static decimal? Number(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ? number : null;

    private static Error Invalid(string message) => new("notifications.invalid_condition", message, ErrorType.Validation);

    [GeneratedRegex(@"^\s*state\(\s*(?<entity>\$person|[a-z_][a-z0-9_]*\.[a-z0-9_]+)\s*(?<op>!=|=|>|<)\s*(?<value>[^)]+?)\s*\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Syntax();
}

/// <summary>Comparison of a <see cref="StateCondition"/>.</summary>
public enum ConditionOperator
{
    Equal,
    NotEqual,
    Greater,
    Less,
}
