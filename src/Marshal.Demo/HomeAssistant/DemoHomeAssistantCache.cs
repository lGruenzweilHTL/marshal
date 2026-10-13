using Marshal.Integrations.HomeAssistant;

namespace Marshal.Demo.HomeAssistant;

/// <summary>The registry and state cache, read straight from the simulated home; always loaded.</summary>
public sealed class DemoHomeAssistantCache(DemoWorld world) : IHomeAssistantCache
{
    public HaSnapshot? Current => world.Home.Snapshot;

    public string? LastError => null;
}
