using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Tools;
using Marshal.Core.Configuration;
using Marshal.Core.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Marshal.Core.Mcp;

/// <summary>
/// MCP servers from <c>/config/mcp/*.yaml</c> (one server per file). Every file is secret-scanned and validated on
/// its own; a rejected file never affects the others. The directory is watched and reloaded as a whole.
/// </summary>
public sealed partial class YamlMcpServerCatalog : IMcpServerCatalog, IDisposable
{
    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly string _directory;
    private readonly IConfigSecretScanner _scanner;
    private readonly ISecretStore _secrets;
    private readonly ILogger<YamlMcpServerCatalog> _logger;
    private readonly IConfigWatcher? _watcher;
    private readonly string _filter;
    private readonly IDisposable? _ownedWatcher;
    private readonly List<Action> _listeners = [];
    private readonly Lock _lock = new();
    private IDisposable? _watch;
    private volatile State _state = new([], []);

    /// <summary>Reads <paramref name="directory"/>, watched through <paramref name="watcher"/> with <paramref name="filter"/> (relative to the watcher's root).</summary>
    public YamlMcpServerCatalog(string directory, IConfigWatcher? watcher, string filter, IConfigSecretScanner scanner, ISecretStore secrets, ILogger<YamlMcpServerCatalog> logger)
        : this(directory, watcher, filter, ownedWatcher: null, scanner, secrets, logger)
    {
    }

    /// <summary>Reads and (optionally) watches <paramref name="directory"/> on its own.</summary>
    public YamlMcpServerCatalog(string directory, IConfigSecretScanner scanner, ISecretStore secrets, ILogger<YamlMcpServerCatalog> logger, bool watch = true)
        : this(directory, OwnWatcher(directory, watch), "*.yaml", scanner, secrets, logger, watch)
    {
    }

    private YamlMcpServerCatalog(string directory, ConfigDirectoryWatcher? watcher, string filter, IConfigSecretScanner scanner, ISecretStore secrets, ILogger<YamlMcpServerCatalog> logger, bool owned)
        : this(directory, watcher, filter, owned ? watcher : null, scanner, secrets, logger)
    {
    }

    private YamlMcpServerCatalog(string directory, IConfigWatcher? watcher, string filter, IDisposable? ownedWatcher, IConfigSecretScanner scanner, ISecretStore secrets, ILogger<YamlMcpServerCatalog> logger)
    {
        _directory = Path.GetFullPath(directory);
        _scanner = scanner;
        _secrets = secrets;
        _logger = logger;
        _watcher = watcher;
        _filter = filter;
        _ownedWatcher = ownedWatcher;
        _state = Read();
        Watch();
    }

    /// <inheritdoc />
    public IReadOnlyList<McpServerDefinition> Servers => _state.Servers;

    /// <inheritdoc />
    public IReadOnlyList<ConfigFileError> Errors => _state.Errors;

    /// <inheritdoc />
    public IDisposable OnChange(Action listener)
    {
        lock (_lock)
        {
            _listeners.Add(listener);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                _listeners.Remove(listener);
            }
        });
    }

    /// <summary>Reads the directory again and notifies listeners.</summary>
    public void Reload()
    {
        _state = Read();
        foreach (ConfigFileError error in _state.Errors)
            LogRejected(_logger, error.File, error.Message);

        Action[] listeners;
        lock (_lock)
        {
            listeners = [.. _listeners];
        }

        foreach (Action listener in listeners)
            listener();
    }

    /// <inheritdoc />
    public async Task<Result<McpConnectionSettings>> ResolveAsync(McpServerDefinition server, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (server.AuthTokenSecret is { } tokenSecret)
        {
            Result<string> token = await _secrets.GetAsync(tokenSecret, cancellationToken);
            if (token.IsFailure)
                return Unresolved(server, "authTokenSecret", tokenSecret);

            headers["Authorization"] = $"Bearer {token.Value}";
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string variable, SecretName secret) in server.EnvironmentSecrets)
        {
            Result<string> value = await _secrets.GetAsync(secret, cancellationToken);
            if (value.IsFailure)
                return Unresolved(server, $"envSecrets.{variable}", secret);

            environment[variable] = value.Value;
        }

        return new McpConnectionSettings(server, headers, environment);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watch?.Dispose();
        _ownedWatcher?.Dispose();
    }

    private static Error Unresolved(McpServerDefinition server, string key, SecretName secret) =>
        new("mcp.secret_unresolved", $"{server.File}: {key} ({secret}) does not resolve.", ErrorType.Failure);

    private State Read()
    {
        if (!Directory.Exists(_directory))
            return new State([], []);

        var servers = new List<McpServerDefinition>();
        var errors = new List<ConfigFileError>();
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(_directory, "*.yaml").Order(StringComparer.Ordinal))
        {
            string file = Path.GetFileName(path);
            Result<McpServerDefinition> parsed = Parse(file, path);
            if (parsed.IsFailure)
            {
                errors.Add(new ConfigFileError(file, parsed.Error.Message, parsed.Error.Code == LiteralSecretCode));
                continue;
            }

            McpServerDefinition server = parsed.Value;
            if (!ids.Add(server.Id))
            {
                errors.Add(new ConfigFileError(file, $"Server id '{server.Id}' is already configured in another file."));
                continue;
            }

            if (!namespaces.Add(server.Namespace))
            {
                errors.Add(new ConfigFileError(file, $"Namespace '{server.Namespace}' is already used by another server."));
                continue;
            }

            servers.Add(server);
        }

        return new State([.. servers.OrderBy(server => server.Id, StringComparer.Ordinal)], errors);
    }

    private Result<McpServerDefinition> Parse(string file, string path)
    {
        string yaml;
        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            return Invalid($"cannot be read: {exception.Message}");
        }

        Result<IReadOnlyList<ConfigSecretFinding>> scan = _scanner.Scan(file, yaml);
        if (scan.IsFailure)
            return scan.Error;
        if (scan.Value.Count > 0)
            return new Error(LiteralSecretCode, string.Join(" ", scan.Value), ErrorType.Validation);

        McpServerFile? raw;
        try
        {
            raw = _deserializer.Deserialize<McpServerFile?>(yaml);
        }
        catch (YamlException exception)
        {
            return Invalid($"invalid value at line {exception.Start.Line}: {exception.Message}");
        }

        return raw is null ? Invalid("is empty.") : Validate(file, raw);
    }

    private static Result<McpServerDefinition> Validate(string file, McpServerFile raw)
    {
        if (string.IsNullOrEmpty(raw.Id) || !IdPattern().IsMatch(raw.Id))
            return Invalid($"id '{raw.Id}' must match [a-z][a-z0-9]*.");
        if (raw.ToolPrefix is not null && !IdPattern().IsMatch(raw.ToolPrefix))
            return Invalid($"toolPrefix '{raw.ToolPrefix}' must match [a-z][a-z0-9]*.");

        string ns = raw.ToolPrefix ?? raw.Id;
        if (ReservedNamespaces.All.Contains(ns) || !ToolId.IsValidNamespace(ns))
            return Invalid($"namespace '{ns}' is reserved for built-in tools; set another id or toolPrefix.");

        SecretName? authToken = null;
        if (raw.AuthTokenSecret is not null)
        {
            if (!SecretName.IsValid(raw.AuthTokenSecret))
                return Invalid("authTokenSecret must be a secret name (^[a-z][a-z0-9_]{0,63}$).");
            authToken = SecretName.Create(raw.AuthTokenSecret).Value;
        }

        var environment = new Dictionary<string, SecretName>(StringComparer.Ordinal);
        foreach (McpServerFile.EnvironmentSecret entry in raw.EnvSecrets)
        {
            if (string.IsNullOrEmpty(entry.Variable) || !SecretName.IsValid(entry.Secret))
                return Invalid("every envSecrets entry needs a variable and a secret name.");
            environment[entry.Variable] = SecretName.Create(entry.Secret).Value;
        }

        var server = new McpServerDefinition
        {
            Id = raw.Id,
            Namespace = ns,
            Transport = McpTransportKind.Http,
            AuthTokenSecret = authToken,
            EnvironmentSecrets = environment,
            Untrusted = raw.Untrusted,
            File = file,
        };

        switch (raw.Transport)
        {
            case "http":
                return Uri.TryCreate(raw.Url, UriKind.Absolute, out Uri? url) && url.Scheme is "http" or "https"
                    ? server with { Url = url }
                    : Invalid("url must be an absolute http(s) URL for transport http.");

            case "stdio":
                return string.IsNullOrWhiteSpace(raw.Command)
                    ? Invalid("command is required for transport stdio.")
                    : server with { Transport = McpTransportKind.Stdio, Command = raw.Command, Arguments = [.. raw.Args] };

            default:
                return Invalid($"transport '{raw.Transport}' must be 'stdio' or 'http'.");
        }

        Error Invalid(string message) => YamlMcpServerCatalog.Invalid($"{file}: {message}");
    }

    private const string LiteralSecretCode = "mcp.literal_secret";

    private static ConfigDirectoryWatcher? OwnWatcher(string directory, bool watch)
    {
        if (!watch)
            return null;

        Directory.CreateDirectory(directory);
        return new ConfigDirectoryWatcher(directory);
    }

    private static Error Invalid(string message) => new("mcp.invalid_server", message, ErrorType.Validation);

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

    [GeneratedRegex("^[a-z][a-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [LoggerMessage(Level = LogLevel.Error, Message = "MCP server file {File} rejected: {Error}")]
    private static partial void LogRejected(ILogger logger, string file, string error);

    private sealed record State(IReadOnlyList<McpServerDefinition> Servers, IReadOnlyList<ConfigFileError> Errors);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
