using System.Runtime.CompilerServices;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Guard;

/// <summary>
/// Applies <c>risk.yaml</c> rules in order (first match wins), identically to built-in and external tools.
/// Target rules are evaluated per resolved target and the highest tier wins; unresolvable targets fail closed
/// to confirm.
/// </summary>
public sealed class RuleBasedRiskEngine(IOptionsMonitor<RiskRulesFile> rules, IEnumerable<IRiskTargetResolver> resolvers) : IRiskEngine
{
    private static readonly ConditionalWeakTable<RiskRulesFile, IReadOnlyList<RiskRule>> _compiled = [];

    private readonly Dictionary<string, IRiskTargetResolver> _resolvers = resolvers.ToDictionary(resolver => resolver.Namespace, StringComparer.Ordinal);

    public async Task<RiskAssessment> AssessAsync(RegisteredTool tool, JsonElement arguments, CancellationToken cancellationToken) =>
        WithConfirmingArguments(await AssessRulesAsync(tool, arguments, cancellationToken), tool, arguments);

    private async Task<RiskAssessment> AssessRulesAsync(RegisteredTool tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        List<RiskRule> matching = MatchingRules(tool.Id);
        if (!matching.Exists(rule => rule.TargetConditions.Count > 0))
            return Assessment(FirstArgumentMatch(matching, tool, arguments));

        IReadOnlyList<RiskTarget>? targets = await ResolveTargetsAsync(tool.Id, arguments, cancellationToken);
        if (targets is null or { Count: 0 })
            return new RiskAssessment(RiskTier.Confirm, [ConfirmationReasonCode.UnresolvedTarget]);

        RiskTier tier = targets.Max(target => FirstMatch(matching, tool, arguments, target));
        return Assessment(tier);
    }

    public RiskProfile GetProfile(RegisteredTool tool)
    {
        List<RiskRule> matching = MatchingRules(tool.Id);
        var tiers = new HashSet<RiskTier>();
        bool defaultReachable = true;
        foreach (RiskRule rule in matching)
        {
            tiers.Add(rule.Tier);
            if (rule.IsUnconditional)
            {
                defaultReachable = false;
                break;
            }
        }

        if (defaultReachable)
            tiers.Add(DefaultTier(tool));
        if (matching.Exists(rule => rule.TargetConditions.Count > 0))
            tiers.Add(RiskTier.Confirm);
        // A safe call becomes confirm when a confirming argument is set.
        if (tool.Descriptor.ConfirmingArguments.Count > 0 && tiers.Contains(RiskTier.Safe))
            tiers.Add(RiskTier.Confirm);

        return new RiskProfile(tiers);
    }

    // Fail closed: external tools (MCP, skills) that no rule matches need confirmation.
    private static RiskTier DefaultTier(RegisteredTool tool) =>
        tool.IsBuiltIn || tool.Descriptor.DefaultTier > RiskTier.Confirm ? tool.Descriptor.DefaultTier : RiskTier.Confirm;

    private static RiskAssessment WithConfirmingArguments(RiskAssessment assessment, RegisteredTool tool, JsonElement arguments)
    {
        if (assessment.Tier == RiskTier.Forbidden || arguments.ValueKind != JsonValueKind.Object)
            return assessment;

        List<ConfirmationReasonCode> added = [.. tool.Descriptor.ConfirmingArguments
            .Where(pair => arguments.TryGetProperty(pair.Key, out JsonElement value) && value.ValueKind != JsonValueKind.Null)
            .Select(pair => pair.Value)
            .Distinct()];
        if (added.Count == 0)
            return assessment;

        // A rule's own confirmation stays; the argument adds its reason.
        List<ConfirmationReasonCode> reasons = [.. assessment.Reasons, .. added.Where(code => !assessment.Reasons.Contains(code))];
        return new RiskAssessment(RiskTier.Confirm, reasons);
    }

    private static RiskAssessment Assessment(RiskTier tier) =>
        new(tier, tier == RiskTier.Confirm ? [ConfirmationReasonCode.Rule] : []);

    private List<RiskRule> MatchingRules(ToolId id)
    {
        RiskRulesFile file = rules.CurrentValue;
        IReadOnlyList<RiskRule> compiled = _compiled.GetValue(file, static f =>
        {
            Result<IReadOnlyList<RiskRule>> result = RiskRulesFile.Compile(f);
            // Invalid files never become current (validated on load); fail closed if one does.
            return result.IsSuccess ? result.Value : [new RiskRule("*", new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, IReadOnlyList<string>>(), RiskTier.Forbidden)];
        });
        return [.. compiled.Where(rule => ToolGlob.Matches(rule.ToolPattern, id))];
    }

    // Used when no matching rule has target conditions.
    private static RiskTier FirstArgumentMatch(List<RiskRule> matching, RegisteredTool tool, JsonElement arguments)
    {
        foreach (RiskRule rule in matching)
        {
            if (ArgumentsMatch(rule, arguments))
                return rule.Tier;
        }

        return DefaultTier(tool);
    }

    private static RiskTier FirstMatch(List<RiskRule> matching, RegisteredTool tool, JsonElement arguments, RiskTarget target)
    {
        foreach (RiskRule rule in matching)
        {
            if (ArgumentsMatch(rule, arguments) && TargetMatches(rule, target))
                return rule.Tier;
        }

        return DefaultTier(tool);
    }

    private static bool ArgumentsMatch(RiskRule rule, JsonElement arguments)
    {
        foreach ((string name, IReadOnlyList<string> allowed) in rule.ArgumentConditions)
        {
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out JsonElement value))
                return false;

            string? text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                _ => null,
            };
            if (text is null || !allowed.Contains(text, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool TargetMatches(RiskRule rule, RiskTarget target)
    {
        foreach ((string attribute, IReadOnlyList<string> allowed) in rule.TargetConditions)
        {
            if (!target.Attributes.TryGetValue(attribute, out string? value) || !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private async Task<IReadOnlyList<RiskTarget>?> ResolveTargetsAsync(ToolId id, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!_resolvers.TryGetValue(id.Namespace, out IRiskTargetResolver? resolver))
            return null;

        try
        {
            Result<IReadOnlyList<RiskTarget>> result = await resolver.ResolveAsync(id, arguments, cancellationToken);
            return result.IsSuccess ? result.Value : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A broken resolver must not make a call safe.
            return null;
        }
    }
}
