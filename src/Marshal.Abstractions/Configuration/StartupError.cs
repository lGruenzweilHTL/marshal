namespace Marshal.Abstractions.Configuration;

/// <summary>A configuration or secret problem found by a startup check; startup reports every one before failing.</summary>
/// <param name="Source">The file (<c>marshal.yaml</c>, <c>risk.yaml</c>, <c>mcp/search.yaml</c>) or environment variable that holds the problem.</param>
/// <param name="Key">The full key path (<c>embedding.model</c>, <c>printers[0].apiKeySecret</c>), or <c>null</c> for a file-level problem.</param>
/// <param name="Message">What is wrong; never contains the configured value.</param>
public sealed record StartupError(string Source, string? Key, string Message)
{
    public override string ToString() => Key is null ? $"{Source}: {Message}" : $"{Source}: {Key} {Message}";
}
