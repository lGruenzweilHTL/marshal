using Marshal.Abstractions.Secrets;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Printers (<c>printers</c> in <c>marshal.yaml</c>) and snapshot settings (<c>printerSnapshots</c>). Changes need a
/// restart.
/// </summary>
public sealed class PrinterOptions
{
    public List<PrinterConfig> Printers { get; set; } = [];

    public SnapshotOptions PrinterSnapshots { get; set; } = new();
}

/// <summary>One Moonraker instance.</summary>
public sealed class PrinterConfig
{
    /// <summary>Name used in tools and the UI (e.g. <c>voron</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Moonraker base URL, e.g. <c>http://voron.lan:7125</c>.</summary>
    public Uri? Url { get; set; }

    /// <summary>Name of the secret holding the Moonraker API key; none when Moonraker trusts the network.</summary>
    public string? ApiKeySecret { get; set; }

    /// <summary>The API key, resolved from the secret store at startup.</summary>
    [Secret(nameof(ApiKeySecret))]
    public string? ApiKey { get; set; }

    /// <summary>Klipper macros <c>printer.run_macro</c> may run (upper-case names, no arguments).</summary>
    public List<string> Macros { get; set; } = [];
}

/// <summary>Webcam snapshots.</summary>
public sealed class SnapshotOptions
{
    /// <summary>Longest side in pixels after resizing.</summary>
    public int MaxSize { get; set; } = 1024;

    /// <summary>JPEG quality 1–100.</summary>
    public int Quality { get; set; } = 85;

    /// <summary>Snapshots kept per printer; older ones are deleted.</summary>
    public int Retention { get; set; } = 20;
}
