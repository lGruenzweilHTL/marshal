using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Notes;

/// <summary>
/// Keeps the notes current: on start and every <see cref="NotesOptions.PullInterval"/> it pulls a remote repository
/// and updates the index for the instance owner (the notes repository belongs to the instance).
/// </summary>
public sealed partial class NotesSyncService(
    INotesSource source,
    INotesIndex index,
    IInstanceOwner owner,
    IExecutionContextAccessor executionContext,
    IOptions<NotesOptions> options,
    TimeProvider time,
    ILogger<NotesSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (source.IsConfigured)
                await RunOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(options.Value.PullInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One pull and index update.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await source.SyncAsync(cancellationToken);
            OwnerId instanceOwner = await owner.GetAsync(cancellationToken);
            using (executionContext.Use(new MarshalExecutionContext { OwnerId = instanceOwner, Source = SessionSource.Task, IsInteractive = false }))
            {
                Result<NotesIndexUpdate> update = await index.UpdateAsync(source, cancellationToken);
                if (update.IsFailure)
                    LogIndexFailed(update.Error.Message);
                else if (update.Value.Reindexed.Count + update.Value.Removed.Count > 0)
                    LogIndexed(update.Value.Reindexed.Count, update.Value.Removed.Count);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogIndexFailed(exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notes index updated: {Reindexed} files re-indexed, {Removed} removed")]
    private partial void LogIndexed(int reindexed, int removed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notes index update failed: {Reason}")]
    private partial void LogIndexFailed(string reason);
}
