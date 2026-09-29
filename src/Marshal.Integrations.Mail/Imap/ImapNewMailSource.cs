using System.Collections.Concurrent;
using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Integrations.Mail.Imap;

/// <summary>
/// Publishes <c>new_mail</c> events for every IMAP account of every owner: one connection per account watches the
/// inbox with IDLE (renewed every few minutes) or, when the server has no IDLE, polls it. The inbox is opened
/// read-only, so watching never marks mail as seen. Mail that is already there when the watch starts produces no
/// events; mail that arrived while disconnected does, after the reconnect. Events belong to the account's owner and
/// carry sender and subject only (third-party text, untrusted).
/// </summary>
public sealed partial class ImapNewMailSource(
    IUserDirectory users,
    IMailAccountStore accounts,
    IExecutionContextAccessor executionContext,
    IEnumerable<ICredentialSource> credentials,
    IEventPublisher publisher,
    MailWatchTimings timings,
    TimeProvider time,
    ILogger<ImapNewMailSource> logger) : BackgroundService, IEventSource
{
    public const string Name = "mail";
    public const string NewMail = "new_mail";

    private const int MaxPerCheck = 50;

    private readonly ImapConnector _connector = new(credentials);
    private readonly ConcurrentDictionary<MailAccountId, bool> _connected = new();

    public string Source => Name;

    public bool IsConnected => _connected.Values.Any(connected => connected);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watches = new Dictionary<MailAccountId, (CancellationTokenSource Stop, Task Run)>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<MailAccount>? current = null;
                try
                {
                    current = await AccountsAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogAccountsFailed(exception);
                }

                if (current is not null)
                {
                    HashSet<MailAccountId> ids = [.. current.Select(account => account.Id)];
                    foreach (MailAccountId removed in watches.Keys.Where(id => !ids.Contains(id)).ToList())
                    {
                        await watches[removed].Stop.CancelAsync();
                        watches.Remove(removed);
                    }

                    foreach (MailAccount account in current.Where(account => account.Provider == MailProviderKind.Imap && !watches.ContainsKey(account.Id)))
                    {
                        var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        watches[account.Id] = (stop, Task.Run(() => WatchAsync(account, stop.Token), CancellationToken.None));
                    }
                }

                await Task.Delay(timings.AccountRefresh, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            foreach ((CancellationTokenSource stop, _) in watches.Values)
                await stop.CancelAsync();
            await Task.WhenAll(watches.Values.Select(watch => watch.Run));
            foreach ((CancellationTokenSource stop, _) in watches.Values)
                stop.Dispose();
        }
    }

    private async Task<IReadOnlyList<MailAccount>> AccountsAsync(CancellationToken cancellationToken)
    {
        var all = new List<MailAccount>();
        foreach (OwnerId owner in await users.ListAsync(cancellationToken))
        {
            using (executionContext.Use(OwnerContext(owner)))
                all.AddRange(await accounts.ListAsync(cancellationToken));
        }

        return all;
    }

    private async Task WatchAsync(MailAccount account, CancellationToken cancellationToken)
    {
        // Credential sources read the owner's secrets.
        using IDisposable scope = executionContext.Use(OwnerContext(account.Owner));
        uint? validity = null;
        uint last = 0;
        int attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Result<ImapClient> connected = await _connector.ConnectAsync(account, cancellationToken);
                if (connected.IsFailure)
                {
                    LogUnavailable(account.Alias, connected.Error.Message);
                }
                else
                {
                    using ImapClient client = connected.Value;
                    IMailFolder inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                    if (validity != inbox.UidValidity)
                    {
                        validity = inbox.UidValidity;
                        last = await HighestUidAsync(inbox, cancellationToken);
                    }
                    else
                    {
                        last = await PublishNewAsync(account, inbox, last, cancellationToken);
                    }

                    _connected[account.Id] = true;
                    attempt = 0;
                    await WatchInboxAsync(account, client, inbox, last, value => last = value, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (ImapConnector.IsConnectionFailure(exception))
            {
                LogDisconnected(account.Alias, exception.Message);
            }
            catch (Exception exception)
            {
                LogWatchFailed(account.Alias, exception);
            }
            finally
            {
                _connected[account.Id] = false;
            }

            try
            {
                await Task.Delay(timings.Backoff(attempt++), time, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _connected.TryRemove(account.Id, out _);
    }

    private async Task WatchInboxAsync(MailAccount account, ImapClient client, IMailFolder inbox, uint last, Action<uint> seen, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (timings.UseIdle && client.Capabilities.HasFlag(ImapCapabilities.Idle))
            {
                using var done = new CancellationTokenSource(timings.IdleRenewal, time);
                void Arrived(object? sender, EventArgs arguments) => done.Cancel();
                inbox.CountChanged += Arrived;
                try
                {
                    await client.IdleAsync(done.Token, cancellationToken);
                }
                finally
                {
                    inbox.CountChanged -= Arrived;
                }
            }
            else
            {
                await Task.Delay(timings.PollInterval, time, cancellationToken);
                await client.NoOpAsync(cancellationToken);
            }

            last = await PublishNewAsync(account, inbox, last, cancellationToken);
            seen(last);
        }
    }

    private static async Task<uint> HighestUidAsync(IMailFolder inbox, CancellationToken cancellationToken)
    {
        if (inbox.UidNext is { } next)
            return next.Id - 1;
        IList<UniqueId> all = await inbox.SearchAsync(SearchQuery.All, cancellationToken);
        return all.Count == 0 ? 0 : all.Max(uid => uid.Id);
    }

    private async Task<uint> PublishNewAsync(MailAccount account, IMailFolder inbox, uint last, CancellationToken cancellationToken)
    {
        // "n:*" always includes the highest message, even when its uid is below n.
        IList<UniqueId> found = await inbox.SearchAsync(SearchQuery.Uids(new UniqueIdRange(new UniqueId(last + 1), UniqueId.MaxValue)), cancellationToken);
        List<UniqueId> fresh = [.. found.Where(uid => uid.Id > last).OrderBy(uid => uid.Id)];
        if (fresh.Count == 0)
            return last;

        IList<IMessageSummary> summaries = await inbox.FetchAsync(fresh.TakeLast(MaxPerCheck).ToList(), MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate, cancellationToken);
        foreach (IMessageSummary summary in summaries.OrderBy(summary => summary.UniqueId.Id))
        {
            JsonElement payload = JsonSerializer.SerializeToElement(new
            {
                account = account.Alias,
                id = ImapMessageIds.Format(inbox.FullName, inbox.UidValidity, summary.UniqueId.Id),
                from = summary.Envelope?.From.Mailboxes.FirstOrDefault()?.Address ?? "",
                subject = summary.Envelope?.Subject ?? "",
                date = summary.Envelope?.Date ?? summary.InternalDate,
            });
            publisher.Publish(new MarshalEvent(Name, NewMail, account.Alias, payload, account.Owner, time.GetUtcNow()));
        }

        return fresh[^1].Id;
    }

    private static MarshalExecutionContext OwnerContext(OwnerId owner) =>
        new() { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listing mail accounts for the new-mail watch failed")]
    private partial void LogAccountsFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mail account {Alias} cannot be watched: {Reason}")]
    private partial void LogUnavailable(string alias, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mail watch of {Alias} disconnected: {Reason}")]
    private partial void LogDisconnected(string alias, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mail watch of {Alias} failed")]
    private partial void LogWatchFailed(string alias, Exception exception);
}
