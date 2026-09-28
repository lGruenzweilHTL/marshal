using System.Linq.Expressions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;
using Marshal.Abstractions.Tools;
using Marshal.Data.Audit;
using Marshal.Data.Conversations;
using Marshal.Data.Identity;
using Marshal.Data.Persistence;
using Marshal.Data.Secrets;
using Marshal.Data.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Marshal.Data;

/// <summary>
/// Marshal's database. Every user-owned table is filtered to the current execution context's owner;
/// a query without an execution context fails instead of returning unfiltered data.
/// </summary>
public sealed class MarshalDbContext(DbContextOptions<MarshalDbContext> options, IExecutionContextAccessor executionContext)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<RunStep> RunSteps => Set<RunStep>();

    public DbSet<PendingAction> PendingActions => Set<PendingAction>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Secret> Secrets => Set<Secret>();

    /// <summary>The current owner. Throws when there is no execution context (fail closed).</summary>
    internal OwnerId CurrentOwnerId =>
        executionContext.Current?.OwnerId
        ?? throw new InvalidOperationException("Owner-scoped data accessed without an execution context.");

    /// <summary>The current owner, or null without an execution context. Only for the Secret filter, which then
    /// returns instance rows only (they are visible to every owner anyway).</summary>
    internal OwnerId? CurrentOwnerIdOrNull => executionContext.Current?.OwnerId;

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureWritesBelongToCurrentOwner();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureWritesBelongToCurrentOwner();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<OwnerId>().HaveConversion<GuidIdConverter<OwnerId>>();
        configurationBuilder.Properties<SessionId>().HaveConversion<GuidIdConverter<SessionId>>();
        configurationBuilder.Properties<RunId>().HaveConversion<GuidIdConverter<RunId>>();
        configurationBuilder.Properties<PendingActionId>().HaveConversion<GuidIdConverter<PendingActionId>>();
        configurationBuilder.Properties<ApiTokenId>().HaveConversion<GuidIdConverter<ApiTokenId>>();

        configurationBuilder.Properties<SessionSource>().HaveConversion<SnakeCaseEnumConverter<SessionSource>>();
        configurationBuilder.Properties<InputMode>().HaveConversion<SnakeCaseEnumConverter<InputMode>>();
        configurationBuilder.Properties<RunOutcome>().HaveConversion<SnakeCaseEnumConverter<RunOutcome>>();
        configurationBuilder.Properties<RunStepKind>().HaveConversion<SnakeCaseEnumConverter<RunStepKind>>();
        configurationBuilder.Properties<RiskTier>().HaveConversion<SnakeCaseEnumConverter<RiskTier>>();
        configurationBuilder.Properties<GuardDecision>().HaveConversion<SnakeCaseEnumConverter<GuardDecision>>();
        configurationBuilder.Properties<PendingActionStatus>().HaveConversion<SnakeCaseEnumConverter<PendingActionStatus>>();
        configurationBuilder.Properties<DecidedBy>().HaveConversion<SnakeCaseEnumConverter<DecidedBy>>();
        configurationBuilder.Properties<ApiTokenKind>().HaveConversion<SnakeCaseEnumConverter<ApiTokenKind>>();
        configurationBuilder.Properties<AuditActorKind>().HaveConversion<SnakeCaseEnumConverter<AuditActorKind>>();

        // SQLite cannot compare or order DateTimeOffset / decimal values natively; binary and REAL storage keeps
        // range queries and sums on the server.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<decimal>().HaveConversion<double>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.HasKey(entity => entity.Id);
            user.HasIndex(entity => entity.UserName).IsUnique();
        });

        modelBuilder.Entity<UserPreferences>(preferences =>
        {
            preferences.HasKey(entity => entity.UserId);
            preferences.HasOne<User>().WithOne().HasForeignKey<UserPreferences>(entity => entity.UserId);
        });

        modelBuilder.Entity<ApiToken>(token =>
        {
            token.HasKey(entity => entity.Id);
            token.HasIndex(entity => entity.TokenHash).IsUnique();
            token.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            OwnerFilter(token);
        });

        modelBuilder.Entity<Session>(session =>
        {
            session.HasKey(entity => entity.Id);
            session.HasIndex(entity => new { entity.ApiTokenId, entity.ExternalId }).IsUnique();
            session.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            session.HasOne<ApiToken>().WithMany().HasForeignKey(entity => entity.ApiTokenId);
            OwnerFilter(session);
        });

        modelBuilder.Entity<Run>(run =>
        {
            run.HasKey(entity => entity.Id);
            run.HasIndex(entity => new { entity.OwnerId, entity.StartedAt });
            run.HasIndex(entity => entity.SessionId);
            run.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            run.HasOne<Session>().WithMany().HasForeignKey(entity => entity.SessionId);
            OwnerFilter(run);
        });

        modelBuilder.Entity<RunStep>(step =>
        {
            step.HasKey(entity => entity.Id);
            step.HasIndex(entity => new { entity.RunId, entity.Sequence }).IsUnique();
            step.HasOne<Run>().WithMany().HasForeignKey(entity => entity.RunId);
            OwnerFilter(step);
        });

        modelBuilder.Entity<PendingAction>(action =>
        {
            action.HasKey(entity => entity.Id);
            action.HasIndex(entity => new { entity.SessionId, entity.Status });
            action.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            action.HasOne<Session>().WithMany().HasForeignKey(entity => entity.SessionId);
            OwnerFilter(action);
        });

        modelBuilder.Entity<AuditEvent>(audit =>
        {
            audit.HasKey(entity => entity.Id);
            audit.HasIndex(entity => new { entity.OwnerId, entity.CreatedAt });
            audit.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            audit.HasOne<Run>().WithMany().HasForeignKey(entity => entity.RunId);
            OwnerFilter(audit);
        });

        modelBuilder.Entity<Secret>(secret =>
        {
            secret.HasKey(entity => entity.Id);
            secret.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            // SQLite treats NULLs as distinct, so a plain unique (owner_id, name) would allow duplicate instance names.
            secret.HasIndex(entity => entity.Name).IsUnique().HasFilter("owner_id IS NULL").HasDatabaseName("ix_secrets_instance_name");
            secret.HasIndex(entity => new { entity.OwnerId, entity.Name }).IsUnique().HasFilter("owner_id IS NOT NULL").HasDatabaseName("ix_secrets_owner_id_name");
            secret.HasQueryFilter(entity => entity.OwnerId == null || entity.OwnerId == CurrentOwnerIdOrNull);
        });
    }

    private void OwnerFilter<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IOwned
    {
        Expression<Func<TEntity, bool>> filter = entity => entity.OwnerId == CurrentOwnerId;
        builder.HasQueryFilter(filter);
    }

    private void EnsureWritesBelongToCurrentOwner()
    {
        foreach (EntityEntry entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            OwnerId? owner = entry.Entity switch
            {
                IOwned owned => owned.OwnerId,
                Secret secret => secret.OwnerId,
                _ => null,
            };
            // Instance secrets (null owner) may be written in any context; admin checks happen in the API.
            if (owner is not null && owner != CurrentOwnerId)
                throw new InvalidOperationException($"Refusing to write a {entry.Metadata.ClrType.Name} row of another owner.");
        }
    }
}
