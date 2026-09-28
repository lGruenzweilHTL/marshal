using Marshal.Testing;
using Marshal.Abstractions.Audit;
using Marshal.Data.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class DbAuditLogTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Record_StoresEventForCurrentOwner()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);

        await new DbAuditLog(database.Factory, database.Accessor, _time).RecordAsync(
            new AuditEntry(AuditActorKind.System, "cli", "token.create", "token-id", new { name = "ha", scopes = new[] { "conversation:write" } }), Ct);

        AuditEvent row = await context.AuditEvents.SingleAsync(Ct);
        Assert.Equal(OwnerA, row.OwnerId);
        Assert.Equal(AuditActorKind.System, row.ActorKind);
        Assert.Equal("cli", row.ActorId);
        Assert.Equal("token.create", row.Action);
        Assert.Equal("token-id", row.Target);
        Assert.Equal("""{"name":"ha","scopes":["conversation:write"]}""", row.Details);
        Assert.Equal(_time.GetUtcNow(), row.CreatedAt);
    }

    [Fact]
    public async Task Events_NotVisibleToOtherOwner()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using (MarshalDbContext asA = database.CreateContext(OwnerA))
            await new DbAuditLog(database.Factory, database.Accessor, _time).RecordAsync(new AuditEntry(AuditActorKind.User, "a", "secret.set"), Ct);

        await using MarshalDbContext asB = database.CreateContext(OwnerB);

        Assert.Empty(await asB.AuditEvents.ToListAsync(Ct));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
