namespace Marshal.Integrations.Moonraker;

/// <summary>Directory holding stored snapshots (<c>/data/snapshots</c>), one folder per printer.</summary>
/// <param name="Directory">Absolute path.</param>
public sealed record SnapshotStorage(string Directory);
