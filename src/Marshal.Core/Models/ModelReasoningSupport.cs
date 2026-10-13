using System.Globalization;
using System.Text.RegularExpressions;
using Marshal.Abstractions.Models;

namespace Marshal.Core.Models;

/// <summary>
/// The reasoning setting of a configured model as it is used: the configured value, or <c>Off</c> when the provider
/// cannot return reasoning for that model. Claude models before 4.6 only take a token budget, not the adaptive
/// thinking the Anthropic SDK requests, so they run without reasoning.
/// </summary>
public static partial class ModelReasoningSupport
{
    public static bool TryParse(string? value, out ModelReasoning reasoning)
    {
        reasoning = ModelReasoning.Off;
        return value is null
            || (value.Length > 0 && !char.IsDigit(value[0]) && Enum.TryParse(value, ignoreCase: true, out reasoning) && Enum.IsDefined(reasoning));
    }

    public static ModelReasoning Effective(ModelOptions model) =>
        TryParse(model.Reasoning, out ModelReasoning reasoning) && Supported(model) ? reasoning : ModelReasoning.Off;

    /// <summary>Whether the model can return reasoning; false only for models known to lack it.</summary>
    public static bool Supported(ModelOptions model)
    {
        if (model.Provider != ModelProviders.Anthropic)
            return true;

        Match match = ClaudeVersion().Match(model.Model);
        if (!match.Success)
            return true;

        int major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        // A dated id (claude-sonnet-4-20250514) has no minor version.
        int minor = match.Groups["minor"].Success && match.Groups["minor"].Value.Length <= 2 ? int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture) : 0;
        return major > 4 || (major == 4 && minor >= 6);
    }

    [GeneratedRegex(@"^claude-(?:[a-z]+-)?(?<major>\d+)(?:-(?<minor>\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeVersion();
}
