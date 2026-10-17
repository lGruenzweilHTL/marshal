namespace Marshal.Abstractions.Context;

/// <summary>
/// The system prompt in two parts: everything before the first turn section, and the turn sections. A provider can
/// cache the stable part across turns.
/// </summary>
/// <param name="Stable">Static and session sections; ends with a blank line when <paramref name="Turn"/> follows.</param>
/// <param name="Turn">Turn sections; empty when there are none.</param>
public sealed record SystemPrompt(string Stable, string Turn)
{
    /// <summary>The whole prompt.</summary>
    public string Text => Stable + Turn;
}
