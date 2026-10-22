using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Data;
using Marshal.Data.Calendar;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Integrations.CalDav.Sync;

/// <summary>Poll state in <c>caldav_sync_state</c>, scoped to the current owner by the context's query filter.</summary>
public sealed class EfCalDavSyncStore(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext) : ICalDavSyncStore
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<CalDavCalendarSync?> GetAsync(string account, string calendar, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        CalDavSyncState? row = await db.CalDavSyncStates.AsNoTracking().FirstOrDefaultAsync(row => row.Account == account && row.Calendar == calendar, cancellationToken);
        if (row is null)
            return null;

        Dictionary<string, CalDavSeenResource> resources = JsonSerializer.Deserialize<Dictionary<string, CalDavSeenResource>>(row.EtagMapJson, _json) ?? [];
        return new CalDavCalendarSync(row.Account, row.Calendar, row.SyncToken, resources.ToDictionary(entry => new Uri(entry.Key), entry => entry.Value), row.LastPoll);
    }

    public async Task SaveAsync(CalDavCalendarSync state, CancellationToken cancellationToken)
    {
        string map = JsonSerializer.Serialize(state.Resources.ToDictionary(entry => entry.Key.AbsoluteUri, entry => entry.Value), _json);
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        CalDavSyncState? row = await db.CalDavSyncStates.FirstOrDefaultAsync(row => row.Account == state.Account && row.Calendar == state.Calendar, cancellationToken);
        if (row is null)
        {
            db.CalDavSyncStates.Add(new CalDavSyncState
            {
                OwnerId = executionContext.Current!.OwnerId,
                Account = state.Account,
                Calendar = state.Calendar,
                SyncToken = state.Token,
                EtagMapJson = map,
                LastPoll = state.LastPoll,
            });
        }
        else
        {
            row.SyncToken = state.Token;
            row.EtagMapJson = map;
            row.LastPoll = state.LastPoll;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
