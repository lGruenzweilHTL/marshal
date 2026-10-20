using Marshal.Abstractions.Apps;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Apps;

/// <summary>
/// App instances in <c>app_instances</c>, scoped to the current owner.
/// </summary>
public sealed class EfAppInstanceStore(IDbContextFactory<MarshalDbContext> contexts, TimeProvider time) : IAppInstanceStore
{
    public async Task<AppInstanceId> CreateAsync(NewAppInstance instance, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = new AppInstance
        {
            Id = AppInstanceId.New(),
            OwnerId = instance.OwnerId,
            SessionId = instance.SessionId,
            RunId = instance.RunId,
            Server = instance.App.Server,
            Tool = instance.App.Tool,
            ToolCallId = instance.ToolCallId,
            ResourceUri = instance.App.ResourceUri,
            Html = instance.App.Html,
            Args = instance.Arguments,
            Result = instance.Result,
            CreatedAt = time.GetUtcNow(),
        };
        db.AppInstances.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<AppInstanceRecord?> GetAsync(AppInstanceId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        AppInstance? row = await db.AppInstances.AsNoTracking().SingleOrDefaultAsync(instance => instance.Id == id, cancellationToken);
        return row is null ? null : new AppInstanceRecord(row.Id, row.Server, row.Tool, row.ToolCallId, row.ResourceUri, row.Html, row.Args, row.Result, row.CreatedAt);
    }
}
