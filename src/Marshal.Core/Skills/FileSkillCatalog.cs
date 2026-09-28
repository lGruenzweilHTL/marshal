using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Skills;
using Marshal.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Marshal.Core.Skills;

/// <summary>
/// Skills from <c>/config/skills/&lt;name&gt;/SKILL.md</c> plus their other text files. Each skill is validated and
/// secret-scanned on its own (frontmatter keys and values, the body and every file); a rejected skill never
/// affects the others. The directory is watched and reloaded as a whole.
/// </summary>
public sealed partial class FileSkillCatalog : ISkillCatalog, IDisposable
{
    public const string SkillFile = "SKILL.md";

    private const int MaxNameLength = 64;
    private const int MaxDescriptionLength = 1024;
    private const int MaxCompatibilityLength = 500;
    private const int MaxFileBytes = 256 * 1024;

    private static readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    private readonly string _directory;
    private readonly IConfigWatcher? _watcher;
    private readonly string _filter;
    private readonly IConfigSecretScanner _scanner;
    private readonly ILogger<FileSkillCatalog> _logger;
    private readonly List<Action> _listeners = [];
    private readonly Lock _lock = new();
    private IDisposable? _watch;
    private volatile State _state;

    /// <summary>Reads <paramref name="directory"/>, watched through <paramref name="watcher"/> with <paramref name="filter"/> (relative to the watcher's root).</summary>
    public FileSkillCatalog(string directory, IConfigWatcher? watcher, string filter, IConfigSecretScanner scanner, ILogger<FileSkillCatalog> logger)
    {
        _directory = Path.GetFullPath(directory);
        _watcher = watcher;
        _filter = filter;
        _scanner = scanner;
        _logger = logger;
        _state = Read();
        Watch();
    }

    public IReadOnlyList<Skill> Skills => _state.Skills;

    public IReadOnlyList<ConfigFileError> Errors => _state.Errors;

    public Skill? Find(string name) => _state.Skills.FirstOrDefault(skill => skill.Name == name);

    /// <summary>Called after every reload.</summary>
    public IDisposable OnChange(Action listener)
    {
        lock (_lock)
            _listeners.Add(listener);

        return new Subscription(() =>
        {
            lock (_lock)
                _listeners.Remove(listener);
        });
    }

    /// <summary>Reads the directory again and notifies listeners.</summary>
    public void Reload()
    {
        _state = Read();
        foreach (ConfigFileError error in _state.Errors)
            LogRejected(error.File, error.Message);

        Action[] listeners;
        lock (_lock)
            listeners = [.. _listeners];

        foreach (Action listener in listeners)
            listener();
    }

    public void Dispose() => _watch?.Dispose();

    private State Read()
    {
        if (!Directory.Exists(_directory))
            return new State([], []);

        var skills = new List<Skill>();
        var errors = new List<ConfigFileError>();
        foreach (string directory in Directory.EnumerateDirectories(_directory).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(directory);
            string file = $"{name}/{SkillFile}";
            if (!File.Exists(Path.Combine(directory, SkillFile)))
                continue;

            Result<Skill> skill = Parse(name, directory, file);
            if (skill.IsSuccess)
                skills.Add(skill.Value);
            else
                errors.Add(new ConfigFileError(file, skill.Error.Message, skill.Error.Code == LiteralSecretCode));
        }

        return new State(skills, errors);
    }

    private Result<Skill> Parse(string directoryName, string directory, string file)
    {
        string text;
        try
        {
            text = File.ReadAllText(Path.Combine(directory, SkillFile)).Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        catch (IOException exception)
        {
            return Invalid(file, $"cannot be read: {exception.Message}");
        }

        Match split = FrontmatterPattern().Match(text);
        if (!split.Success)
            return Invalid(file, "has no YAML frontmatter (--- name / description ---).");

        string yaml = split.Groups[1].Value;
        string body = text[split.Length..].TrimStart('\n');
        Result<IReadOnlyList<ConfigSecretFinding>> scan = _scanner.Scan(file, yaml);
        if (scan.IsFailure)
            return scan.Error;

        List<ConfigSecretFinding> findings = [.. scan.Value, .. _scanner.ScanText(file, body)];
        Result<Dictionary<string, string>> files = ReadFiles(directoryName, directory, findings);
        if (files.IsFailure)
            return files.Error;
        if (findings.Count > 0)
            return new Error(LiteralSecretCode, string.Join(" ", findings), ErrorType.Validation);

        SkillFrontmatter? frontmatter;
        try
        {
            frontmatter = _deserializer.Deserialize<SkillFrontmatter?>(yaml);
        }
        catch (YamlException exception)
        {
            return Invalid(file, $"invalid frontmatter at line {exception.Start.Line}: {exception.Message}");
        }

        return Validate(directoryName, frontmatter ?? new SkillFrontmatter()) is { } problem
            ? Invalid(file, problem)
            : new Skill(frontmatter!.Name!, frontmatter.Description!.Trim(), body, files.Value);
    }

    private Result<Dictionary<string, string>> ReadFiles(string directoryName, string directory, List<ConfigSecretFinding> findings)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (relative == SkillFile || new FileInfo(path).Length > MaxFileBytes)
                continue;

            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (IOException exception)
            {
                return Invalid($"{directoryName}/{relative}", $"cannot be read: {exception.Message}");
            }

            if (content.Contains('\0', StringComparison.Ordinal))
                continue;

            string file = $"{directoryName}/{relative}";
            if (relative.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                Result<IReadOnlyList<ConfigSecretFinding>> scan = _scanner.Scan(file, content);
                if (scan.IsSuccess)
                    findings.AddRange(scan.Value);
            }

            findings.AddRange(_scanner.ScanText(file, content));
            files[relative] = content;
        }

        return files;
    }

    private static string? Validate(string directoryName, SkillFrontmatter frontmatter)
    {
        if (string.IsNullOrWhiteSpace(frontmatter.Name))
            return "name is required.";
        if (frontmatter.Name.Length > MaxNameLength || !NamePattern().IsMatch(frontmatter.Name))
            return $"name '{frontmatter.Name}' must be 1-{MaxNameLength} lowercase letters, digits and single hyphens, not starting or ending with a hyphen.";
        if (frontmatter.Name != directoryName)
            return $"name '{frontmatter.Name}' must match its directory '{directoryName}'.";
        if (string.IsNullOrWhiteSpace(frontmatter.Description))
            return "description is required.";
        if (frontmatter.Description.Length > MaxDescriptionLength)
            return $"description must be at most {MaxDescriptionLength} characters.";
        if (frontmatter.Compatibility is { Length: > MaxCompatibilityLength })
            return $"compatibility must be at most {MaxCompatibilityLength} characters.";

        return null;
    }

    private const string LiteralSecretCode = "skill.literal_secret";

    private static Error Invalid(string file, string message) => new("skill.invalid", $"{file}: {message}", ErrorType.Validation);

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

    [GeneratedRegex(@"\A---\n(.*?)\n---[ \t]*(?:\n|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FrontmatterPattern();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [LoggerMessage(Level = LogLevel.Error, Message = "Skill {File} rejected: {Error}")]
    private partial void LogRejected(string file, string error);

    private sealed record State(IReadOnlyList<Skill> Skills, IReadOnlyList<ConfigFileError> Errors);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
