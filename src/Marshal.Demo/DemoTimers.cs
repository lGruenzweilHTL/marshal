namespace Marshal.Demo;

/// <summary>A timer started on a device through the timer intents.</summary>
public sealed record DemoTimer(string? Name, string? DeviceId, TimeSpan Duration, DateTimeOffset StartedAt)
{
    public DateTimeOffset EndsAt => StartedAt + Duration;
}

/// <summary>The running timers of the simulated home.</summary>
public sealed class DemoTimers
{
    private readonly Lock _gate = new();
    private readonly List<DemoTimer> _timers = [];

    public void Add(DemoTimer timer)
    {
        lock (_gate)
            _timers.Add(timer);
    }

    /// <summary>The timers of <paramref name="deviceId"/> still running at <paramref name="now"/>.</summary>
    public IReadOnlyList<DemoTimer> Running(string? deviceId, DateTimeOffset now)
    {
        lock (_gate)
        {
            _timers.RemoveAll(timer => timer.EndsAt <= now);
            return [.. _timers.Where(timer => timer.DeviceId == deviceId)];
        }
    }

    public bool Remove(DemoTimer timer)
    {
        lock (_gate)
            return _timers.Remove(timer);
    }
}
