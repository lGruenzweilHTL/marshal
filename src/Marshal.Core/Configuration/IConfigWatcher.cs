using Microsoft.Extensions.Primitives;

namespace Marshal.Core.Configuration;

/// <summary>
/// Change notifications for files under one config directory.
/// </summary>
public interface IConfigWatcher
{
    /// <summary>A token that fires once when a file matching <paramref name="filter"/> (glob, relative to the directory) changes.</summary>
    IChangeToken Watch(string filter);
}
