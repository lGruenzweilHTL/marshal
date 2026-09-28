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
        var file = new YamlConfigFile<T>(path, scanner, validate, logger, watch);
        Result first = file.Initialize();
        if (first.IsSuccess)
            return file;

        file.Dispose();
        throw new InvalidOperationException(first.Error.Message);
    }
}
