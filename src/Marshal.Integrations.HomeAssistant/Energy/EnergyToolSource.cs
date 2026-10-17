using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Energy;

/// <summary>
/// Marshal's own <c>energy.*</c> tools over HA's energy dashboard (a built-in source, so they may use the reserved
/// <c>energy</c> namespace); none without Home Assistant.
/// </summary>
public sealed class EnergyToolSource(
    IHomeAssistantCache cache,
    IHomeAssistantCommands commands,
    IUserProfileProvider profiles,
    IOptions<HomeAssistantOptions> options,
    TimeProvider time) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = options.Value.Url is null ? [] : EnergyTools.Create(cache, commands, profiles, time);

    public string Name => "energy";

    public bool IsBuiltIn => true;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("energy", "Energy: electricity, gas and water usage, solar production, consumption per device")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
