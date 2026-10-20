using System.Collections.Concurrent;
using Marshal.Abstractions.Apps;
using Marshal.Abstractions.Execution;

namespace Marshal.Testing;

/// <summary>App instances in memory; reads see the accessor's current owner only (all owners without an accessor).</summary>
public sealed class InMemoryAppInstanceStore(IExecutionContextAccessor? accessor = null) : IAppInstanceStore
{
    private readonly ConcurrentDictionary<AppInstanceId, NewAppInstance> _instances = new();

    /// <summary>Created instances by id.</summary>
    public IReadOnlyDictionary<AppInstanceId, NewAppInstance> Instances => _instances;

    public Task<AppInstanceId> CreateAsync(NewAppInstance instance, CancellationToken cancellationToken)
    {
        var id = AppInstanceId.New();
        _instances[id] = instance;
        return Task.FromResult(id);
    }

    public Task<AppInstanceRecord?> GetAsync(AppInstanceId id, CancellationToken cancellationToken)
    {
        if (!_instances.TryGetValue(id, out NewAppInstance? instance) || (accessor?.Current is { } current && current.OwnerId != instance.OwnerId))
            return Task.FromResult<AppInstanceRecord?>(null);

        McpAppOutput app = instance.App;
        return Task.FromResult<AppInstanceRecord?>(new AppInstanceRecord(id, app.Server, app.Tool, instance.ToolCallId, app.ResourceUri, app.Html, instance.Arguments, instance.Result, DateTimeOffset.UnixEpoch));
    }
}
