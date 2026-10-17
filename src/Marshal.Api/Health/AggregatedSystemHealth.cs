using Marshal.Abstractions.Health;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.LiveEvents;

namespace Marshal.Api.Health;

/// <summary>
/// Collects the conditions of every <see cref="IHealthWarningProvider"/>, remembers when each warning (component and
/// code) first appeared, and publishes the list on the <c>health</c> topic whenever it changes. A failing provider
/// is logged and skipped so one broken component never hides the others.
/// </summary>
internal sealed partial class AggregatedSystemHealth(
    IEnumerable<IHealthWarningProvider> providers,
    ILiveEventPublisher live,
    IUserDirectory users,
    TimeProvider time,
    ILogger<AggregatedSystemHealth> logger) : ISystemHealth
{
    public const string Topic = "health";

    public const string ChangedEvent = "health.changed";

    private readonly Lock _lock = new();
    private Dictionary<(string Component, string Code), DateTimeOffset> _since = [];
    private IReadOnlyList<HealthWarning> _last = [];

    public IReadOnlyList<HealthWarning> Last
    {
        get
        {
            lock (_lock)
                return _last;
        }
    }

    public async Task<IReadOnlyList<HealthWarning>> GetWarningsAsync(CancellationToken cancellationToken)
    {
        List<HealthCondition> conditions = [];
        foreach (IHealthWarningProvider provider in providers)
        {
            try
            {
                conditions.AddRange(await provider.GetConditionsAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogProviderFailed(provider.GetType().Name, exception);
            }
        }

        HealthWarning[] sorted;
        bool changed;
        lock (_lock)
        {
            DateTimeOffset now = time.GetUtcNow();
            Dictionary<(string, string), DateTimeOffset> since = [];
            List<HealthWarning> warnings = [];
            foreach (HealthCondition condition in conditions)
            {
                (string, string) key = (condition.Component, condition.Code);
                if (since.ContainsKey(key))
                    continue;

                since[key] = _since.GetValueOrDefault(key, now);
                warnings.Add(new HealthWarning(condition.Component, condition.Code, condition.Severity, condition.Message, condition.Link, since[key]));
            }

            sorted = [.. warnings
                .OrderByDescending(warning => warning.Severity)
                .ThenBy(warning => warning.Component, StringComparer.Ordinal)
                .ThenBy(warning => warning.Code, StringComparer.Ordinal)];
            _since = since;
            changed = !sorted.SequenceEqual(_last);
            _last = sorted;
        }

        if (changed)
            await PublishAsync(sorted, cancellationToken);

        return sorted;
    }

    private async Task PublishAsync(IReadOnlyList<HealthWarning> warnings, CancellationToken cancellationToken)
    {
        var view = SystemHealthView.From(warnings);
        foreach (OwnerId owner in await users.ListAsync(cancellationToken))
            live.Publish(new LiveEvent(Topic, ChangedEvent, Guid.CreateVersion7().ToString(), owner, view));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Health warning provider {Provider} failed")]
    private partial void LogProviderFailed(string provider, Exception exception);
}
