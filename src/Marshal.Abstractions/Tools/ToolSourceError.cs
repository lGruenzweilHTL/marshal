namespace Marshal.Abstractions.Tools;

/// <summary>
/// Why a tool source was rejected. All other sources keep working.
/// </summary>
/// <param name="Source">The rejected source.</param>
/// <param name="Message">Human-readable reason.</param>
public sealed record ToolSourceError(string Source, string Message);
