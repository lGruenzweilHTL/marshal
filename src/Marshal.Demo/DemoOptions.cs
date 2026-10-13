namespace Marshal.Demo;

/// <summary>
/// The <c>demo:</c> section of <c>marshal.yaml</c>.
/// </summary>
public sealed class DemoOptions
{
    /// <summary>Simulates the unconfigured integrations and seeds demo data for the owner.</summary>
    public bool Enabled { get; set; }

    /// <summary>Shows the "Demo" badge in the navigation; turn off for screenshots.</summary>
    public bool ShowBadge { get; set; } = true;
}
