using System.Collections;
using System.Text.RegularExpressions;
using Marshal.Abstractions.Configuration;

namespace Marshal.Api.Configuration;

/// <summary>
/// Turns a validation message into a <see cref="StartupError"/>. Messages start with the full key path
/// (<c>embedding.model is required.</c>); the source is the <c>MARSHAL__</c> variable that sets the key, else
/// <c>marshal.yaml</c> (also for a missing key, which belongs there).
/// </summary>
internal static partial class ConfigurationErrors
{
    public static StartupError Locate(string message, IDictionary environment)
    {
        int space = message.IndexOf(' ', StringComparison.Ordinal);
        string first = space < 0 ? message : message[..space];
        if (space < 0 || !first.AsSpan().ContainsAny('.', '[') || !KeyPath().IsMatch(first))
            return new StartupError(MarshalConfiguration.MainFile, null, message);

        return new StartupError(SourceOf(first, environment), first, message[(space + 1)..]);
    }

    private static string SourceOf(string keyPath, IDictionary environment)
    {
        string path = keyPath.Replace("[", ".", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal)
            .Replace(".", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
        foreach (DictionaryEntry entry in environment)
        {
            if (entry.Key is string name && string.Equals(MarshalConfiguration.KeyOf(name), path, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return MarshalConfiguration.MainFile;
    }

    // A lowercase dotted or indexed path: embedding.model, printers[0].apiKeySecret, printers[].name.
    [GeneratedRegex(@"^[a-z][A-Za-z0-9]*(\[\d*\])?(\.[A-Za-z][A-Za-z0-9_]*(\[\d*\])?)*$")]
    private static partial Regex KeyPath();
}
