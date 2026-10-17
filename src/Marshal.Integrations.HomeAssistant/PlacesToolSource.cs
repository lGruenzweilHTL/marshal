using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Marshal's own <c>places.*</c> tools over Home Assistant zones (a built-in source, so they may use the reserved
/// <c>places</c> namespace); none without Home Assistant.
/// </summary>
public sealed class PlacesToolSource(
    IHomeAssistantCache cache,
    IHomeAssistantCommands commands,
    IUserPreferencesStore preferences,
    IOptions<HomeAssistantOptions> options) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = options.Value.Url is null ? [] : PlacesTools.Create(cache, commands, preferences);

    public string Name => "places";

    public bool IsBuiltIn => true;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("places", "Where people are: home, work, zones, distances and travel times")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
