using Marshal.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Marshal.Core.Configuration;

/// <summary>
/// A hot-reloaded YAML file under <c>/config</c>. Every load is secret-scanned and validated; a reload that fails
/// keeps the previous value and records the error. A failing first load fails startup.
/// </summary>
/// <typeparam name="T">Bound type; keys are camelCase property names, unknown keys are errors.</typeparam>
public sealed partial class YamlConfigFile<T> : IOptionsMonitor<T>, IDisposable
    where T : class, new()
{
    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly string _path;
    private readonly IConfigSecretScanner _scanner;
    private readonly Func<T, Result> _validate;
    private readonly ILogger _logger;
    private readonly IConfigWatcher? _watcher;
    private readonly string _filter;
    private readonly IDisposable? _ownedWatcher;
    private readonly List<Action<T, string?>> _listeners = [];
    private readonly Lock _lock = new();
    private IDisposable? _watch;
    private T _current = new();

    internal YamlConfigFile(string path, IConfigSecretScanner scanner, Func<T, Result> validate, ILogger logger, IConfigWatcher? watcher, string filter, IDisposable? ownedWatcher)
    {
        _path = Path.GetFullPath(path);
        _scanner = scanner;
        _validate = validate;
        _logger = logger;
        _watcher = watcher;
        _filter = filter;
        _ownedWatcher = ownedWatcher;
    }

    /// <summary>File name, for error messages.</summary>
    public string FileName => Path.GetFileName(_path);

    /// <inheritdoc />
    public T CurrentValue
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    /// <summary>Error of the last failed reload, or null when the last load succeeded.</summary>
    public string? LastError { get; private set; }

    internal Result Initialize()
    {
        Result<T> first = Read();
        if (first.IsFailure)
            return first;

        _current = first.Value;
        Watch();
        return Result.Success();
    }

    /// <inheritdoc />
    public T Get(string? name) => CurrentValue;

    /// <inheritdoc />
    public IDisposable? OnChange(Action<T, string?> listener)
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

    /// <summary>Reads the file again. On failure the previous value stays active.</summary>
    public Result Reload()
    {
        Result<T> result = Read();
        if (result.IsFailure)
        {
            LastError = result.Error.Message;
            LogReloadFailed(_logger, FileName, result.Error.Message);
            return result;
        }

        List<Action<T, string?>> listeners;
        lock (_lock)
        {
            _current = result.Value;
            listeners = [.. _listeners];
        }

        LastError = null;
        LogReloaded(_logger, FileName);
        foreach (Action<T, string?> listener in listeners)
            listener(result.Value, Options.DefaultName);

        return Result.Success();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watch?.Dispose();
        _ownedWatcher?.Dispose();
    }

    private Result<T> Read()
    {
        if (!File.Exists(_path))
            return new T();

        string yaml;
        try
        {
            yaml = File.ReadAllText(_path);
        }
        catch (IOException exception)
        {
            return new Error("config.unreadable", $"{FileName}: {exception.Message}", ErrorType.Failure);
        }

        Result<IReadOnlyList<ConfigSecretFinding>> scan = _scanner.Scan(FileName, yaml);
        if (scan.IsFailure)
            return scan.Error;
        if (scan.Value.Count > 0)
            return new Error("config.literal_secret", string.Join(" ", scan.Value), ErrorType.Validation);

        T? value;
        try
        {
            value = _deserializer.Deserialize<T?>(yaml);
        }
        catch (YamlException exception)
        {
            return new Error("config.invalid_yaml", $"{FileName}: invalid value at line {exception.Start.Line}: {exception.Message}", ErrorType.Validation);
        }

        value ??= new T();
        Result valid = _validate(value);
        return valid.IsSuccess ? value : valid.Error with { Message = $"{FileName}: {valid.Error.Message}" };
    }

    private void Watch()
    {
        if (_watcher is null)
            return;

        IChangeToken token = _watcher.Watch(_filter);
        _watch = token.RegisterChangeCallback(_ => OnFileChanged(), null);
    }

    private void OnFileChanged()
    {
        _watch?.Dispose();
        Reload();
        Watch();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reload of {File} failed, keeping the previous configuration: {Error}")]
    private static partial void LogReloadFailed(ILogger logger, string file, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reloaded {File}")]
    private static partial void LogReloaded(ILogger logger, string file);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
