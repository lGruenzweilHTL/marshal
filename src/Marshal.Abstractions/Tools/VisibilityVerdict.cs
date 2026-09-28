namespace Marshal.Abstractions.Tools;

/// <summary>
/// Whether a tool is visible (and callable) in a context, and why not.
/// </summary>
/// <param name="IsVisible">True when the tool may be offered and called.</param>
/// <param name="Reason">Stable reason code when hidden (e.g. <c>forbidden</c>, <c>satellite_denied</c>).</param>
public sealed record VisibilityVerdict(bool IsVisible, string? Reason = null)
{
    public static VisibilityVerdict Visible { get; } = new(true);

    public static VisibilityVerdict Hidden(string reason) => new(false, reason);
}
