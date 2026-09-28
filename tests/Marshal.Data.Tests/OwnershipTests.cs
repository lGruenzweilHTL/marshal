using Marshal.Testing;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Data.Conversations;
using Marshal.Data.Secrets;
using Microsoft.EntityFrameworkCore;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class OwnershipTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sessions_QueriedAsOwnerA_ReturnsOnlyA()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await SeedSessionAsync(database, OwnerA);
        await SeedSessionAsync(database, OwnerB);

        await using MarshalDbContext context = database.CreateContext(OwnerA);
        List<Session> sessions = await context.Sessions.ToListAsync(TestContext.Current.CancellationToken);

        Session session = Assert.Single(sessions);
        Assert.Equal(OwnerA, session.OwnerId);
    }

    [Fact]
    public async Task Query_WithoutExecutionContext_Throws()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await SeedSessionAsync(database, OwnerA);
        database.Accessor.Current = null;

        await using MarshalDbContext context = database.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Sessions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunSteps_ChildRowsCarryOwnerAndAreFiltered()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await SeedRunWithStepAsync(database, OwnerA);
        await SeedRunWithStepAsync(database, OwnerB);

        await using MarshalDbContext context = database.CreateContext(OwnerB);
        List<RunStep> steps = await context.RunSteps.ToListAsync(TestContext.Current.CancellationToken);

        RunStep step = Assert.Single(steps);
        Assert.Equal(OwnerB, step.OwnerId);
    }

    [Fact]
    public async Task Secrets_ReturnOwnRowsAndInstanceRows()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await SeedSecretAsync(database, OwnerA, "mail_a", asOwner: OwnerA);
        await SeedSecretAsync(database, OwnerB, "mail_b", asOwner: OwnerB);
        await SeedSecretAsync(database, null, "printer", asOwner: OwnerA);

        await using MarshalDbContext context = database.CreateContext(OwnerA);
        List<string> names = await context.Secrets.OrderBy(secret => secret.Name).Select(secret => secret.Name).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["mail_a", "printer"], names);
    }

    [Fact]
    public async Task SaveChanges_RowForOtherOwner_Throws()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();

        await using MarshalDbContext context = database.CreateContext(OwnerA);
        context.Sessions.Add(NewSession(OwnerB));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InstanceSecrets_DuplicateName_Rejected()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await SeedSecretAsync(database, null, "printer", asOwner: OwnerA);

        await Assert.ThrowsAsync<DbUpdateException>(() => SeedSecretAsync(database, null, "printer", asOwner: OwnerB));
    }

    [Fact]
    public async Task OwnerSecrets_SameNameForDifferentOwners_Allowed()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await SeedSecretAsync(database, OwnerA, "freshrss", asOwner: OwnerA);
        await SeedSecretAsync(database, OwnerB, "freshrss", asOwner: OwnerB);
        await SeedSecretAsync(database, null, "freshrss", asOwner: OwnerA);

        await Assert.ThrowsAsync<DbUpdateException>(() => SeedSecretAsync(database, OwnerA, "freshrss", asOwner: OwnerA));
    }

    private static Session NewSession(Abstractions.Identity.OwnerId owner) => new()
    {
        Id = SessionId.New(),
        OwnerId = owner,
        Source = SessionSource.Api,
        IdentityEvidence = "{}",
        ResolvedBy = "owner_default",
        CreatedAt = _now,
        LastTurnAt = _now,
    };

    private static async Task SeedSessionAsync(TestDatabase database, Abstractions.Identity.OwnerId owner)
    {
        await using MarshalDbContext context = database.CreateContext(owner);
        context.Sessions.Add(NewSession(owner));
        await context.SaveChangesAsync();
    }

    private static async Task SeedRunWithStepAsync(TestDatabase database, Abstractions.Identity.OwnerId owner)
    {
        await using MarshalDbContext context = database.CreateContext(owner);
        var run = new Run
        {
            Id = RunId.New(),
            OwnerId = owner,
            Source = SessionSource.Api,
            Trigger = "turn",
            StartedAt = _now,
            Outcome = RunOutcome.Ok,
        };
        context.Runs.Add(run);
        context.RunSteps.Add(new RunStep { Id = Guid.NewGuid(), OwnerId = owner, RunId = run.Id, Sequence = 0, Kind = RunStepKind.Context });
        await context.SaveChangesAsync();
    }

    private static async Task SeedSecretAsync(TestDatabase database, Abstractions.Identity.OwnerId? owner, string name, Abstractions.Identity.OwnerId asOwner)
    {
        await using MarshalDbContext context = database.CreateContext(asOwner);
        context.Secrets.Add(new Secret { Id = Guid.NewGuid(), OwnerId = owner, Name = name, Ciphertext = [1, 2, 3], CreatedAt = _now, UpdatedAt = _now });
        await context.SaveChangesAsync();
    }
}
