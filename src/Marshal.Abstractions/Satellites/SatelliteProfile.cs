namespace Marshal.Abstractions.Satellites;

/// <summary>
/// Optional per-satellite settings from <c>/config/satellites.yaml</c>. A convenience limit for a fixed device,
/// not an authorization boundary.
/// </summary>
/// <param name="Verbosity">Reply length hint (<c>short</c>, <c>normal</c>, <c>detailed</c>).</param>
/// <param name="Notes">Free text added to the satellite context lines.</param>
/// <param name="DenyTools">Tool id globs hidden and denied on this satellite.</param>
/// <param name="DenyConfirm">Tool id globs that may not ask for confirmation on this satellite.</param>
/// <param name="Display">Name of the display in the satellite's room, if any.</param>
public sealed record SatelliteProfile(string Verbosity, string? Notes, IReadOnlyList<string> DenyTools, IReadOnlyList<string> DenyConfirm, string? Display = null)
{
    /// <summary>Profile of unconfigured satellites and turns without a satellite.</summary>
    public static SatelliteProfile Default { get; } = new("normal", null, [], []);
}
