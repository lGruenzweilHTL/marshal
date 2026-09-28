using Marshal.Abstractions;
using Microsoft.Extensions.Logging;

namespace Marshal.Core.Configuration;

/// <summary>
/// Factory for <see cref="YamlConfigFile{T}"/>.
/// </summary>
public static class YamlConfigFile
{
    /// <summary>Loads the file (missing = defaults) and starts watching it. Throws when the first load fails.</summary>
    public static YamlConfigFile<T> Load<T>(string path, IConfigSecretScanner scanner, Func<T, Result> validate, ILogger logger, bool watch = true)
        where T : class, new()
    {
        ConfigDirectoryWatcher? watcher = watch ? new ConfigDirectoryWatcher(Path.GetDirectoryName(Path.GetFullPath(path))!) : null;
        return Initialize(new YamlConfigFile<T>(path, scanner, validate, logger, watcher, Path.GetFileName(path), watcher));
    }

    /// <summary>Loads <paramref name="relativePath"/> under the directory of <paramref name="watcher"/>, watched through it.</summary>
    public static YamlConfigFile<T> Load<T>(string directory, string relativePath, IConfigWatcher watcher, IConfigSecretScanner scanner, Func<T, Result> validate, ILogger logger)
        where T : class, new() =>
        Initialize(new YamlConfigFile<T>(Path.Combine(directory, relativePath), scanner, validate, logger, watcher, relativePath, ownedWatcher: null));

    private static YamlConfigFile<T> Initialize<T>(YamlConfigFile<T> file)
        where T : class, new()
    {
        Result first = file.Initialize();
        if (first.IsSuccess)
            return file;

        file.Dispose();
        throw new InvalidOperationException(first.Error.Message);
    }
}
