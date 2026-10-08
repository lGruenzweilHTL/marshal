using System.Collections.Immutable;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Logging;

namespace Marshal.Core.Mcp;

/// <summary>
/// One <see cref="McpToolSource"/> per configured server, kept in step with the catalog: a server file that is
/// added starts a source, a removed one stops it, a changed one replaces it. Sources of unchanged servers keep
/// their connection. The registry reads <see cref="IToolSourceProvider.Sources"/> on every refresh.
/// </summary>
public sealed partial class McpToolSources : IToolSourceProvider, IAsyncDisposable
{
    private static readonly TimeSpan _reloadWait = TimeSpan.FromSeconds(10);

    private readonly IMcpServerCatalog _catalog;
    private readonly IMcpTransportFactory _transports;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _logger;
    private readonly McpReconnectPolicy _reconnect;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _lock = new();
    private readonly IDisposable _subscription;
    private volatile ImmutableSortedDictionary<string, McpToolSource> _sources = ImmutableSortedDictionary.Create<string, McpToolSource>(StringComparer.Ordinal);
    private Task _pending = Task.CompletedTask;
    private volatile bool _started;
    private int _disposed;

    public McpToolSources(IMcpServerCatalog catalog, IMcpTransportFactory transports, ILoggerFactory loggers, McpReconnectPolicy reconnect, TimeProvider time)
    {
        _catalog = catalog;
        _transports = transports;
        _loggers = loggers;
        _logger = loggers.CreateLogger<McpToolSources>();
        _reconnect = reconnect;
        _time = time;
        _subscription = catalog.OnChange(OnCatalogChanged);
    }

    IReadOnlyList<IToolSource> IToolSourceProvider.Sources => [.. _sources.Values];

    /// <summary>State of every configured server, ordered by id.</summary>
    public IReadOnlyList<McpServerStatus> Statuses => [.. _sources.Values.Select(source => source.Status)];

    /// <summary>Starts a source per configured server and waits up to <paramref name="maxWait"/> for their first connection attempts.</summary>
    public async Task StartAsync(TimeSpan maxWait, CancellationToken cancellationToken)
    {
        _started = true;
        await ReconcileAsync(maxWait, cancellationToken);
    }

    /// <summary>Completes when every reload triggered so far has been applied.</summary>
    public Task WhenIdleAsync()
    {
        lock (_lock)
            return _pending;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _subscription.Dispose();
        await WhenIdleAsync();
        await _gate.WaitAsync();
        try
        {
            foreach (McpToolSource source in _sources.Values)
                await source.DisposeAsync();

            _sources = _sources.Clear();
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }

    /// <summary>True when both definitions connect the same way and expose tools the same way.</summary>
    internal static bool SameDefinition(McpServerDefinition a, McpServerDefinition b) =>
        a.Id == b.Id
        && a.Namespace == b.Namespace
        && a.Transport == b.Transport
        && a.Url == b.Url
        && a.Command == b.Command
        && a.Arguments.SequenceEqual(b.Arguments, StringComparer.Ordinal)
        && a.AuthTokenSecret == b.AuthTokenSecret
        && a.EnvironmentSecrets.Count == b.EnvironmentSecrets.Count
        && a.EnvironmentSecrets.All(pair => b.EnvironmentSecrets.TryGetValue(pair.Key, out SecretName secret) && secret == pair.Value)
        && a.Untrusted == b.Untrusted
        && a.UntrustedTools.SequenceEqual(b.UntrustedTools, StringComparer.Ordinal);

    private void OnCatalogChanged()
    {
        if (!_started)
            return;

        lock (_lock)
            _pending = ReloadAfterAsync(_pending);
    }

    private async Task ReloadAfterAsync(Task previous)
    {
        await previous;
        try
        {
            await ReconcileAsync(_reloadWait, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogReloadFailed(exception);
        }
    }

    private async Task ReconcileAsync(TimeSpan maxWait, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ImmutableSortedDictionary<string, McpToolSource> current = _sources;
            ImmutableSortedDictionary<string, McpToolSource>.Builder next = ImmutableSortedDictionary.CreateBuilder<string, McpToolSource>(StringComparer.Ordinal);
            var started = new List<McpToolSource>();
            var stopped = new List<McpToolSource>();
            foreach (McpServerDefinition server in _catalog.Servers)
            {
                if (current.TryGetValue(server.Id, out McpToolSource? existing) && SameDefinition(existing.Server, server))
                {
                    next[server.Id] = existing;
                    continue;
                }

                if (existing is not null)
                    stopped.Add(existing);

                var source = new McpToolSource(server, _catalog, _transports, _loggers, _reconnect, _time);
                started.Add(source);
                next[server.Id] = source;
            }

            stopped.AddRange(current.Where(pair => !next.ContainsKey(pair.Key)).Select(pair => pair.Value));

            try
            {
                await Task.WhenAll(started.Select(source => source.StartAsync(cancellationToken))).WaitAsync(maxWait, _time, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Slow servers keep connecting in the background; their tools appear once connected.
            }

            _sources = next.ToImmutable();
            foreach (McpToolSource source in stopped)
                await source.DisposeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Applying the MCP server configuration failed")]
    private partial void LogReloadFailed(Exception exception);
}
