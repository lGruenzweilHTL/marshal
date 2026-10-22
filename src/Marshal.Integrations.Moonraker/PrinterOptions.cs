using Marshal.Abstractions.Secrets;
using Microsoft.Extensions.Options;

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

    /// <summary>The printer's web UI (Mainsail / Fluidd), linked from the printer page for actions Marshal does not offer.</summary>
    public Uri? WebUrl { get; set; }

    /// <summary>
    /// Home Assistant <c>switch.*</c> entity that powers the printer; the print-done-away suggestion offers to switch
    /// it off. Optional.
    /// </summary>
    public string? PowerEntity { get; set; }

    /// <summary>Name of the secret holding the Moonraker API key; none when Moonraker trusts the network.</summary>
    public string? ApiKeySecret { get; set; }

    /// <summary>The API key, resolved from the secret store at startup.</summary>
    [Secret(nameof(ApiKeySecret))]
    public string? ApiKey { get; set; }

    /// <summary><see cref="Url"/> with a trailing slash, so relative paths keep a path prefix.</summary>
    internal Uri BaseUri() =>
        Url!.AbsolutePath.EndsWith('/') ? Url : new UriBuilder(Url) { Path = Url.AbsolutePath + "/" }.Uri;

    /// <summary>Klipper macros <c>printer.run_macro</c> may run (upper-case names, no arguments).</summary>
    public List<string> Macros { get; set; } = [];

    /// <summary>The print babysitter for this printer (needs a webcam and a vision model).</summary>
    public BabysitterOptions Babysitter { get; set; } = new();
}

/// <summary>Print babysitter settings of one printer. Only configuration changes them, never the agent.</summary>
public sealed class BabysitterOptions
{
    public bool Enabled { get; set; }

    /// <summary>Time between snapshot checks while a print runs.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
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

/// <summary>Checks the keys whose error names the printer's index.</summary>
public sealed class PrinterOptionsValidator : IValidateOptions<PrinterOptions>
{
    public ValidateOptionsResult Validate(string? name, PrinterOptions options)
    {
        for (int index = 0; index < options.Printers.Count; index++)
        {
            if (options.Printers[index].PowerEntity is { } entity && !IsSwitch(entity))
                return ValidateOptionsResult.Fail($"printers[{index}].powerEntity must be a switch.* entity, not '{entity}'.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsSwitch(string entity) =>
        entity.StartsWith("switch.", StringComparison.Ordinal) && entity.Length > "switch.".Length && !entity.Contains(' ', StringComparison.Ordinal);
}
