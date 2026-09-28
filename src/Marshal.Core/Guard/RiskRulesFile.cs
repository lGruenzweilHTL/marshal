using System.Collections;
using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Guard;

/// <summary>
/// Contents of <c>/config/risk.yaml</c>: an ordered rule list, first match wins.
/// </summary>
public sealed class RiskRulesFile
{
    private const string ToolKey = "tool";
    private const string ArgumentsKey = "args";

    public List<RiskRuleEntry> Risk { get; set; } = [];

    /// <summary>Validates every rule; used on load and on every reload.</summary>
    public static Result Validate(RiskRulesFile file)
    {
        Result<IReadOnlyList<RiskRule>> compiled = Compile(file);
        return compiled.IsSuccess ? Result.Success() : compiled.Error;
    }

    /// <summary>Turns the file into compiled rules.</summary>
    public static Result<IReadOnlyList<RiskRule>> Compile(RiskRulesFile file)
    {
        var rules = new List<RiskRule>();
        for (int index = 0; index < file.Risk.Count; index++)
        {
            RiskRuleEntry entry = file.Risk[index];
            string at = $"risk[{index}]";
            if (!entry.Match.TryGetValue(ToolKey, out object? toolValue) || toolValue is not string tool || tool.Length == 0)
                return Invalid($"{at}.match.tool is required and must be a glob string.");

            if (!Enum.TryParse(entry.Tier, ignoreCase: true, out RiskTier tier) || !Enum.IsDefined(tier) || int.TryParse(entry.Tier, CultureInfo.InvariantCulture, out _))
                return Invalid($"{at}.tier must be safe, confirm or forbidden.");

            var arguments = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var targets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach ((string key, object? value) in entry.Match)
            {
                if (key == ToolKey)
                    continue;

                if (key == ArgumentsKey)
                {
                    if (value is not IDictionary argumentMap)
                        return Invalid($"{at}.match.args must map argument names to values.");

                    foreach (DictionaryEntry argument in argumentMap)
                    {
                        string name = argument.Key.ToString() ?? "";
                        if (!TryValues(argument.Value, out IReadOnlyList<string> allowed))
                            return Invalid($"{at}.match.args.{name} must be a value or a list of values.");

                        arguments[name] = allowed;
                    }

                    continue;
                }

                if (!TryValues(value, out IReadOnlyList<string> values))
                    return Invalid($"{at}.match.{key} must be a value or a list of values.");

                targets[key] = values;
            }

            rules.Add(new RiskRule(tool, arguments, targets, tier));
        }

        return rules;
    }

    private static bool TryValues(object? value, out IReadOnlyList<string> values)
    {
        switch (value)
        {
            case string text:
                values = [text];
                return true;
            case IList list when list.Cast<object?>().All(item => item is string):
                values = [.. list.Cast<string>()];
                return values.Count > 0;
            default:
                values = [];
                return false;
        }
    }

    private static Error Invalid(string message) => new("config.invalid", message, ErrorType.Validation);
}
