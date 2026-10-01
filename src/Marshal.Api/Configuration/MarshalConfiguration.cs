using System.Collections;
using Marshal.Abstractions.Configuration;
using Marshal.Api.Hosting;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Marshal.Api.Configuration;

/// <summary>
/// Configuration layering, low to high: built-in defaults (options classes, log levels) → <c>/config/marshal.yaml</c> →
/// environment variables with the exact prefix <c>MARSHAL__</c>.
/// </summary>
public static class MarshalConfiguration
{
    /// <summary>Exact prefix of configuration environment variables. <c>MARSHAL_SECRET_*</c> never matches it.</summary>
    public const string EnvironmentPrefix = "MARSHAL__";

    /// <summary>Main configuration file inside the config directory.</summary>
    public const string MainFile = "marshal.yaml";

    // The host logs a failed start with its stack trace; Program reports it instead (see StartupReport). Faulted
    // background services still log at Critical.
    private static readonly Dictionary<string, string?> _defaults = new()
    {
        ["Logging:LogLevel:Microsoft.Extensions.Hosting.Internal.Host"] = "Critical",
    };

    /// <summary>Adds the layers. Throws <see cref="StartupFailedException"/> when <c>marshal.yaml</c> is not valid YAML.</summary>
    public static IConfigurationBuilder AddMarshalConfiguration(this IConfigurationBuilder builder, string configDirectory, IDictionary environment)
    {
        string mainFile = Path.Combine(Path.GetFullPath(configDirectory), MainFile);
        CheckSyntax(mainFile, environment);
        builder.AddInMemoryCollection(_defaults);
        builder.AddYamlFile(mainFile, optional: true, reloadOnChange: false);
        builder.AddInMemoryCollection(FromEnvironment(environment));
        return builder;
    }

    // Parsed here as well because the YAML provider loads lazily and wraps parse errors; the error names only the
    // position, since the parser message can quote the content.
    private static void CheckSyntax(string path, IDictionary environment)
    {
        if (!File.Exists(path))
            return;

        try
        {
            new YamlStream().Load(new StringReader(File.ReadAllText(path)));
        }
        catch (YamlException error)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(FromEnvironment(environment)).Build();
            throw new StartupFailedException(
                [new StartupError(MainFile, null, $"is not valid YAML (line {error.Start.Line}, column {error.Start.Column}).")],
                StartupReport.IsVerbose(configuration),
                error);
        }
    }

    private static Dictionary<string, string?> FromEnvironment(IDictionary environment)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in environment)
        {
            if (entry.Key is string name && KeyOf(name) is { } key)
                values[key] = entry.Value as string;
        }

        return values;
    }

    /// <summary>The configuration key an environment variable sets, or <c>null</c> when it is no configuration variable.</summary>
    public static string? KeyOf(string variable)
    {
        if (!variable.StartsWith(EnvironmentPrefix, StringComparison.Ordinal))
            return null;

        string key = variable[EnvironmentPrefix.Length..].Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
        return key.Length > 0 ? key : null;
    }
}
