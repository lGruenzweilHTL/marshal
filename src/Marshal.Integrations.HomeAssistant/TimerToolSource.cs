using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Marshal's own <c>timers.*</c> tools (a built-in source, so they may use the reserved <c>timers</c> namespace);
/// none without Home Assistant.
/// </summary>
public sealed class TimerToolSource(IHomeAssistantClient client, IOptions<HomeAssistantOptions> options) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = options.Value.Url is null ? [] : TimerTools.Create(client);

    public string Name => "timers";

    public bool IsBuiltIn => true;

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
