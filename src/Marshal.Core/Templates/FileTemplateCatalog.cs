using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Templates;
using Marshal.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Marshal.Core.Templates;

/// <summary>
/// Agent templates from <c>/config/templates/*.md</c> (one per file, named like the file) plus the built-in
/// <c>default</c>, which a <c>default.md</c> replaces. Each file is validated and secret-scanned on its own.
/// </summary>
public sealed partial class FileTemplateCatalog : ITemplateCatalog, IDisposable
{
    private const int MaxSteps = 50;
    private const int MaxAdvisorCalls = 10;

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly string _directory;
    private readonly IConfigWatcher? _watcher;
    private readonly string _filter;
    private readonly IConfigSecretScanner _scanner;
    private readonly ILogger<FileTemplateCatalog> _logger;
    private IDisposable? _watch;
    private volatile State _state;

    /// <summary>Reads <paramref name="directory"/>, watched through <paramref name="watcher"/> with <paramref name="filter"/> (relative to the watcher's root).</summary>
    public FileTemplateCatalog(string directory, IConfigWatcher? watcher, string filter, IConfigSecretScanner scanner, ILogger<FileTemplateCatalog> logger)
    {
        _directory = Path.GetFullPath(directory);
        _watcher = watcher;
        _filter = filter;
        _scanner = scanner;
        _logger = logger;
        _state = Read();
        Watch();
    }

    public IReadOnlyList<AgentTemplate> Templates => _state.Templates;

    public IReadOnlyList<ConfigFileError> Errors => _state.Errors;

    public AgentTemplate? Find(string name) => _state.Templates.FirstOrDefault(template => template.Name == name);

    /// <summary>Reads the directory again.</summary>
    public void Reload()
    {
        _state = Read();
        foreach (ConfigFileError error in _state.Errors)
            LogRejected(error.File, error.Message);
    }

    public void Dispose() => _watch?.Dispose();

    private State Read()
    {
        var templates = new Dictionary<string, AgentTemplate>(StringComparer.Ordinal) { [AgentTemplate.DefaultName] = AgentTemplate.BuiltInDefault };
        var errors = new List<ConfigFileError>();
        if (Directory.Exists(_directory))
        {
            foreach (string path in Directory.EnumerateFiles(_directory, "*.md").Order(StringComparer.Ordinal))
            {
                string file = Path.GetFileName(path);
                Result<AgentTemplate> template = Parse(file, path);
                if (template.IsSuccess)
                    templates[template.Value.Name] = template.Value;
                else
                    errors.Add(new ConfigFileError(file, template.Error.Message, template.Error.Code == LiteralSecretCode));
            }
        }

        return new State([.. templates.Values.OrderBy(template => template.Name, StringComparer.Ordinal)], errors);
    }

    private Result<AgentTemplate> Parse(string file, string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            return Invalid(file, $"cannot be read: {exception.Message}");
        }

        if (MarkdownFrontmatter.Split(text) is not { } split)
            return Invalid(file, "has no YAML frontmatter (--- name / description / tools ---).");

        (string yaml, string body) = split;
        Result<IReadOnlyList<ConfigSecretFinding>> scan = _scanner.Scan(file, yaml);
        if (scan.IsFailure)
            return scan.Error;

        List<ConfigSecretFinding> findings = [.. scan.Value, .. _scanner.ScanText(file, body)];
        if (findings.Count > 0)
            return new Error(LiteralSecretCode, string.Join(" ", findings), ErrorType.Validation);

        TemplateFrontmatter? frontmatter;
        try
        {
            frontmatter = _deserializer.Deserialize<TemplateFrontmatter?>(yaml);
        }
        catch (YamlException exception)
        {
            return Invalid(file, $"invalid frontmatter at line {exception.Start.Line}: {exception.Message}");
        }

        return Build(file, frontmatter ?? new TemplateFrontmatter(), body);
    }

    private static Result<AgentTemplate> Build(string file, TemplateFrontmatter frontmatter, string body)
    {
        string expected = Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrWhiteSpace(frontmatter.Name) || !NamePattern().IsMatch(frontmatter.Name))
            return Invalid(file, $"name '{frontmatter.Name}' must be lowercase letters, digits and hyphens.");
        if (frontmatter.Name != expected)
            return Invalid(file, $"name '{frontmatter.Name}' must match the file name '{expected}'.");
        if (string.IsNullOrWhiteSpace(frontmatter.Description))
            return Invalid(file, "description is required.");

        if (frontmatter.ModelTier is not (null or "fast" or "standard"))
            return Invalid(file, $"modelTier '{frontmatter.ModelTier}' must be 'fast' or 'standard'.");
        if (frontmatter.MaxSteps is < 1 or > MaxSteps)
            return Invalid(file, $"maxSteps must be between 1 and {MaxSteps}.");
        if (frontmatter.MaxAdvisorCalls is < 0 or > MaxAdvisorCalls)
            return Invalid(file, $"maxAdvisorCalls must be between 0 and {MaxAdvisorCalls}.");

        return new AgentTemplate(
            frontmatter.Name,
            frontmatter.Description.Trim(),
            frontmatter.ModelTier switch { "fast" => ModelTier.Fast, "standard" => ModelTier.Standard, _ => null },
            frontmatter.Tools ?? [],
            frontmatter.Skills,
            frontmatter.MaxSteps,
            frontmatter.MaxAdvisorCalls,
            body.Trim());
    }

    private const string LiteralSecretCode = "template.literal_secret";

    private static Error Invalid(string file, string message) => new("template.invalid", $"{file}: {message}", ErrorType.Validation);

    private void Watch()
    {
        if (_watcher is null)
            return;

        IChangeToken token = _watcher.Watch(_filter);
        _watch = token.RegisterChangeCallback(_ => OnDirectoryChanged(), null);
    }

    // Watch again before reading, so a change made while reloading triggers another reload instead of being lost.
    private void OnDirectoryChanged()
    {
        _watch?.Dispose();
        Watch();
        Reload();
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [LoggerMessage(Level = LogLevel.Error, Message = "Template {File} rejected: {Error}")]
    private partial void LogRejected(string file, string error);

    private sealed record State(IReadOnlyList<AgentTemplate> Templates, IReadOnlyList<ConfigFileError> Errors);
}
