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

    /// <summary>
    /// Loads <paramref name="relativePath"/> under the directory of <paramref name="watcher"/>, watched through it.
    /// A failing first load returns its error (prefixed with the file name), so startup can report it with the others.
    /// </summary>
    public static Result<YamlConfigFile<T>> TryLoad<T>(string directory, string relativePath, IConfigWatcher watcher, IConfigSecretScanner scanner, Func<T, Result> validate, ILogger logger)
        where T : class, new() =>
        TryInitialize(new YamlConfigFile<T>(Path.Combine(directory, relativePath), scanner, validate, logger, watcher, relativePath, ownedWatcher: null));

    private static YamlConfigFile<T> Initialize<T>(YamlConfigFile<T> file)
        where T : class, new()
    {
        Result<YamlConfigFile<T>> loaded = TryInitialize(file);
        return loaded.IsSuccess ? loaded.Value : throw new InvalidOperationException(loaded.Error.Message);
    }

    private static Result<YamlConfigFile<T>> TryInitialize<T>(YamlConfigFile<T> file)
        where T : class, new()
    {
        Result first = file.Initialize();
        if (first.IsSuccess)
            return file;

        file.Dispose();
        return first.Error;
    }
}
