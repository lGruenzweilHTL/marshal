using System.Collections;

namespace Marshal.Api.Configuration;

/// <summary>
/// Configuration layering, low to high: built-in defaults (options classes) → <c>/config/marshal.yaml</c> →
/// environment variables with the exact prefix <c>MARSHAL__</c>.
/// </summary>
public static class MarshalConfiguration
{
    /// <summary>Exact prefix of configuration environment variables. <c>MARSHAL_SECRET_*</c> never matches it.</summary>
    public const string EnvironmentPrefix = "MARSHAL__";

    /// <summary>Main configuration file inside the config directory.</summary>
    public const string MainFile = "marshal.yaml";

    public static IConfigurationBuilder AddMarshalConfiguration(this IConfigurationBuilder builder, string configDirectory, IDictionary environment)
    {
        builder.AddYamlFile(Path.Combine(Path.GetFullPath(configDirectory), MainFile), optional: true, reloadOnChange: false);
        builder.AddInMemoryCollection(FromEnvironment(environment));
        return builder;
    }

    private static Dictionary<string, string?> FromEnvironment(IDictionary environment)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in environment)
        {
            if (entry.Key is not string name || !name.StartsWith(EnvironmentPrefix, StringComparison.Ordinal))
                continue;

            string key = name[EnvironmentPrefix.Length..].Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
            if (key.Length > 0)
                values[key] = entry.Value as string;
        }

        return values;
    }
}
