using Marshal.Abstractions;

namespace Marshal.Core.Configuration;

/// <summary>
/// Refuses literal secrets in <c>/config</c> files.
/// </summary>
public interface IConfigSecretScanner
{
    /// <summary>Scans the raw YAML of one file. Fails with <c>config.invalid_yaml</c> when it does not parse.</summary>
    Result<IReadOnlyList<ConfigSecretFinding>> Scan(string file, string yaml);

    /// <summary>Checks free text (e.g. Markdown) line by line for values that look like credentials.</summary>
    IReadOnlyList<ConfigSecretFinding> ScanText(string file, string text);
}
