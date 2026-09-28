using Marshal.Abstractions.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Core.Sessions;

/// <summary>
/// Closes idle sessions of every owner once a minute. Turns also close an idle session on arrival, so this
/// only bounds how long a pending action of an abandoned session survives.
/// </summary>
public sealed partial class IdleSessionCloser(IUserDirectory users, SessionMaintenance maintenance, TimeProvider timeProvider, ILogger<IdleSessionCloser> logger) : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (OwnerId owner in await users.ListAsync(stoppingToken))
            {
                int closed = await maintenance.CloseIdleAsync(owner, stoppingToken);
                if (closed > 0)
                    LogClosed(logger, closed, owner.Value);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Closed {Count} idle sessions of owner {OwnerId}")]
    private static partial void LogClosed(ILogger logger, int count, Guid ownerId);
}
