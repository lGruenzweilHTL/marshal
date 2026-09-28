namespace Marshal.Abstractions.Tools;

/// <summary>
/// Tool sources that come and go at runtime (one per MCP server). The registry reads them on every refresh, so a
/// source registers and unregisters as a unit.
/// </summary>
public interface IToolSourceProvider
{
    /// <summary>The sources that currently exist.</summary>
    IReadOnlyList<IToolSource> Sources { get; }
}
