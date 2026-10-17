using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Health;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Health;

/// <summary>
/// <c>system.health()</c> (safe): Marshal's current health warnings with messages in the run's language, e.g. for a
/// briefing. Hidden while there are no warnings. The output is trusted: warnings carry only Marshal-authored text.
/// </summary>
public sealed class SystemHealthTool(ISystemHealth? health = null) : ITool
{
    public const string Id = "system.health";

    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = new ToolId("system", "health"),
        Description = "List Marshal's current health warnings (unreachable servers, tools waiting for approval, configuration problems). "
            + "Mention them briefly to the user when they matter.",
        ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
    };

    public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(health is not null && health.Last.Count > 0);

    public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        IReadOnlyList<HealthWarning> warnings = health is null ? [] : await health.GetWarningsAsync(cancellationToken);
        string language = invocation.Context.Language;
        var body = new
        {
            warnings = warnings.Select(warning => new
            {
                component = warning.Component,
                code = warning.Code,
                severity = warning.Severity == HealthSeverity.Error ? "error" : "warning",
                message = warning.Message.For(language),
                since = warning.Since,
            }),
        };
        return new ToolOutput(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
    }
}
