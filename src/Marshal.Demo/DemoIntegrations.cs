namespace Marshal.Demo;

/// <summary>An integration demo mode can simulate.</summary>
public enum DemoIntegration
{
    HomeAssistant,
    Printers,
    Mail,
    Rss,
    Notes,
    Web,
    Caldav,
}

/// <summary>The integrations simulated in this process because they were not configured.</summary>
public sealed class DemoIntegrations(IReadOnlyList<DemoIntegration> simulatedAtStartup)
{
    public IReadOnlyList<DemoIntegration> SimulatedAtStartup { get; } = simulatedAtStartup;
}
