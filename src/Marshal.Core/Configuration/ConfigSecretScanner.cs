using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Secrets;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Marshal.Core.Configuration;

/// <summary>
/// Scans the raw YAML tree (not bound options), so unknown keys are checked too. The exact rules are
/// documented in the core-agent design (D9).
/// </summary>
public sealed partial class ConfigSecretScanner : IConfigSecretScanner
{
    private const double EncodedEntropyThreshold = 4.4;
    private const double OpaqueEntropyThreshold = 4.5;

    private static readonly HashSet<string> _forbiddenLastWords = new(StringComparer.Ordinal)
    {
        "password", "passphrase", "token", "credential", "credentials",
    };

    private static readonly HashSet<string> _forbiddenLastTwoWords = new(StringComparer.Ordinal)
    {
        "api key", "private key", "access key",
    };

    /// <inheritdoc />
    public Result<IReadOnlyList<ConfigSecretFinding>> Scan(string file, string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException exception)
        {
            return new Error("config.invalid_yaml", $"{file}: invalid YAML at line {exception.Start.Line}.", ErrorType.Validation);
        }

        var findings = new List<ConfigSecretFinding>();
        foreach (YamlDocument document in stream.Documents)
            Visit(file, document.RootNode, key: null, path: "", findings);

        return findings;
    }

    public IReadOnlyList<ConfigSecretFinding> ScanText(string file, string text)
    {
        var findings = new List<ConfigSecretFinding>();
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (LooksLikeCredential(lines[index].Trim()))
                findings.Add(new ConfigSecretFinding(file, $"line {index + 1}", ConfigSecretRule.CredentialValue));
        }

        return findings;
    }

    private static void Visit(string file, YamlNode node, string? key, string path, List<ConfigSecretFinding> findings)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
                {
                    string childKey = (keyNode as YamlScalarNode)?.Value ?? "";
                    string childPath = path.Length == 0 ? childKey : $"{path}.{childKey}";
                    if (IsForbiddenKey(childKey))
                        findings.Add(new ConfigSecretFinding(file, childPath, ConfigSecretRule.ForbiddenKey));
                    else
                        Visit(file, valueNode, childKey, childPath, findings);
                }

                break;

            case YamlSequenceNode sequence:
                int index = 0;
                foreach (YamlNode item in sequence.Children)
                    Visit(file, item, key, $"{path}[{index++}]", findings);

                break;

            case YamlScalarNode scalar:
                CheckScalar(file, key, path, scalar.Value ?? "", findings);
                break;
        }
    }

    private static void CheckScalar(string file, string? key, string path, string value, List<ConfigSecretFinding> findings)
    {
        if (LooksLikeCredential(value))
        {
            findings.Add(new ConfigSecretFinding(file, path, ConfigSecretRule.CredentialValue));
            return;
        }

        if (key is not null && IsReferenceKey(key) && value.Length > 0 && !SecretName.IsValid(value))
            findings.Add(new ConfigSecretFinding(file, path, ConfigSecretRule.InvalidReference));
    }

    private static bool IsReferenceKey(string key) => SplitWords(key) is [.., "secret"];

    private static bool IsForbiddenKey(string key)
    {
        List<string> words = SplitWords(key);
        if (words.Count == 0)
            return false;

        if (_forbiddenLastWords.Contains(words[^1]))
            return true;

        return words.Count >= 2 && _forbiddenLastTwoWords.Contains($"{words[^2]} {words[^1]}");
    }

    private static List<string> SplitWords(string key) =>
        [.. WordPattern().Matches(key).Select(match => match.Value.ToLowerInvariant())];

    private static bool LooksLikeCredential(string value)
    {
        if (value.Length == 0)
            return false;

        if (KnownPrefixPattern().IsMatch(value) || HexPattern().IsMatch(value))
            return true;

        foreach (Match match in Base64Pattern().Matches(value).Concat(Base64UrlPattern().Matches(value)))
        {
            if (value.EndsWith('=') && match.Value.EndsWith('=') && match.Length % 4 == 0)
                return true;
            if (HasMixedClasses(match.Value) && Entropy(match.Value) >= EncodedEntropyThreshold)
                return true;
        }

        return OpaquePattern().Matches(value).Any(match => HasMixedClasses(match.Value) && Entropy(match.Value) >= OpaqueEntropyThreshold);
    }

    private static bool HasMixedClasses(string text) =>
        text.Any(char.IsUpper) && text.Any(char.IsLower) && text.Any(char.IsDigit);

    private static double Entropy(string text) =>
        -text.GroupBy(character => character)
            .Select(group => (double)group.Count() / text.Length)
            .Sum(probability => probability * Math.Log2(probability));

    // camelCase, PascalCase, snake_case and kebab-case words ("apiKeySecret" → api, key, secret; "inputPerMTok" → input, per, m, tok).
    [GeneratedRegex("[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"-----BEGIN [A-Z ]+-----|(^|[^A-Za-z0-9])(eyJ[A-Za-z0-9_-]{8,}|sk-ant-|sk-[A-Za-z0-9_-]|ghp_[A-Za-z0-9]|xox[a-z]-)", RegexOptions.CultureInvariant)]
    private static partial Regex KnownPrefixPattern();

    [GeneratedRegex("(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])", RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();

    [GeneratedRegex("[A-Za-z0-9+/]{32,}={0,2}", RegexOptions.CultureInvariant)]
    private static partial Regex Base64Pattern();

    [GeneratedRegex("[A-Za-z0-9_-]{32,}", RegexOptions.CultureInvariant)]
    private static partial Regex Base64UrlPattern();

    [GeneratedRegex(@"[^\s/.:@?&=,;]{24,}", RegexOptions.CultureInvariant)]
    private static partial Regex OpaquePattern();
}
