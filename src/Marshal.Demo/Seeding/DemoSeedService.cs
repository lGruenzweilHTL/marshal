using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Demo.Seeding;

/// <summary>
/// Seeds the demo data at startup when demo mode is on and it was never seeded for the instance owner. Until the owner
/// exists it checks again every <see cref="OwnerPoll"/>, so an owner created later is seeded without a restart. A
/// reset is not undone by a restart; <c>marshal demo seed</c> seeds again.
/// </summary>
public sealed partial class DemoSeedService(
    IDemoSeeder seeder,
    IInstanceOwner instanceOwner,
    IOptions<DemoOptions> options,
    TimeProvider time,
    ILogger<DemoSeedService> logger) : BackgroundService
{
    public static readonly TimeSpan OwnerPoll = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        LogDemoOn();
        OwnerId owner;
        while (true)
        {
            try
            {
                owner = await instanceOwner.GetAsync(stoppingToken);
                break;
            }
            catch (InvalidOperationException)
            {
                await Task.Delay(OwnerPoll, time, stoppingToken);
            }
        }

        if (await seeder.WasSeededBeforeAsync(owner, stoppingToken))
            return;

        try
        {
            Result<int> seeded = await seeder.SeedAsync(owner, stoppingToken);
            if (seeded.IsSuccess)
                LogSeeded(seeded.Value);
            else
                LogSeedFailed(seeded.Error.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The demo works without its history; a failed seed must not stop the host.
            LogSeedFailed(exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo mode is on: unconfigured integrations are simulated and demo data is seeded")]
    private partial void LogDemoOn();

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data seeded ({Rows} rows)")]
    private partial void LogSeeded(int rows);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo data not seeded: {Reason}")]
    private partial void LogSeedFailed(string reason);
}
