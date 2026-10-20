using System.Linq.Expressions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tokens;
using Marshal.Abstractions.Tools;
using Marshal.Data.Apps;
using Marshal.Data.Artifacts;
using Marshal.Data.Audit;
using Marshal.Data.Auth;
using Marshal.Data.Conversations;
using Marshal.Data.Demo;
using Marshal.Data.Identity;
using Marshal.Data.InputRequests;
using Marshal.Data.Mail;
using Marshal.Data.Mcp;
using Marshal.Data.Memory;
using Marshal.Data.Notes;
using Marshal.Data.Notifications;
using Marshal.Data.Persistence;
using Marshal.Data.Secrets;
using Marshal.Data.Tasks;
using Marshal.Data.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Marshal.Data;

/// <summary>
/// Marshal's database. Every user-owned table is filtered to the current execution context's owner;
/// a query without an execution context fails instead of returning unfiltered data. The Identity tables are the user
/// store itself and are only accessed through ASP.NET Core Identity; everything else refers to users by <see cref="OwnerId"/>.
/// </summary>
public sealed class MarshalDbContext(DbContextOptions<MarshalDbContext> options, IExecutionContextAccessor executionContext)
    : IdentityDbContext<User, Role, OwnerId>(options)
{
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<RunStep> RunSteps => Set<RunStep>();

    public DbSet<PendingAction> PendingActions => Set<PendingAction>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Secret> Secrets => Set<Secret>();

    public DbSet<AuthTicket> AuthTickets => Set<AuthTicket>();

    public DbSet<Chunk> Chunks => Set<Chunk>();

    public DbSet<ChunkVersion> ChunkVersions => Set<ChunkVersion>();

    public DbSet<ChunkEdge> ChunkEdges => Set<ChunkEdge>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<StoredMailAccount> MailAccounts => Set<StoredMailAccount>();

    public DbSet<NoteIndexEntry> NoteIndexEntries => Set<NoteIndexEntry>();

    public DbSet<ProjectAlias> ProjectAliases => Set<ProjectAlias>();

    public DbSet<ProjectFeedback> ProjectFeedback => Set<ProjectFeedback>();

    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();

    public DbSet<ExecutorSession> ExecutorSessions => Set<ExecutorSession>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Proposal> Proposals => Set<Proposal>();

    public DbSet<FollowUp> FollowUps => Set<FollowUp>();

    public DbSet<StoredArtifact> Artifacts => Set<StoredArtifact>();

    public DbSet<DemoSeed> DemoSeeds => Set<DemoSeed>();

    public DbSet<PendingInputRequest> PendingInputRequests => Set<PendingInputRequest>();

    /// <summary>Pinned MCP tool definitions; instance configuration without an owner column.</summary>
    public DbSet<McpToolPinRow> McpToolPins => Set<McpToolPinRow>();

    /// <summary>Server tasks of MCP tool calls that are polled until they end.</summary>
    public DbSet<McpTaskHandle> McpTaskHandles => Set<McpTaskHandle>();

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
        IncrementRowVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureWritesBelongToCurrentOwner();
        IncrementRowVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<OwnerId>().HaveConversion<GuidIdConverter<OwnerId>>();
        configurationBuilder.Properties<SessionId>().HaveConversion<GuidIdConverter<SessionId>>();
        configurationBuilder.Properties<RunId>().HaveConversion<GuidIdConverter<RunId>>();
        configurationBuilder.Properties<PendingActionId>().HaveConversion<GuidIdConverter<PendingActionId>>();
        configurationBuilder.Properties<InputRequestId>().HaveConversion<GuidIdConverter<InputRequestId>>();
        configurationBuilder.Properties<ApiTokenId>().HaveConversion<GuidIdConverter<ApiTokenId>>();
        configurationBuilder.Properties<ProjectId>().HaveConversion<GuidIdConverter<ProjectId>>();
        configurationBuilder.Properties<MailAccountId>().HaveConversion<GuidIdConverter<MailAccountId>>();
        configurationBuilder.Properties<ArtifactId>().HaveConversion<GuidIdConverter<ArtifactId>>();
        configurationBuilder.Properties<TaskId>().HaveConversion<GuidIdConverter<TaskId>>();
        configurationBuilder.Properties<ChunkId>().HaveConversion<ChunkIdConverter>();

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
        configurationBuilder.Properties<ChunkScope>().HaveConversion<SnakeCaseEnumConverter<ChunkScope>>();
        configurationBuilder.Properties<ChunkType>().HaveConversion<SnakeCaseEnumConverter<ChunkType>>();
        configurationBuilder.Properties<ChunkEdgeKind>().HaveConversion<SnakeCaseEnumConverter<ChunkEdgeKind>>();
        configurationBuilder.Properties<ChunkAuthor>().HaveConversion<SnakeCaseEnumConverter<ChunkAuthor>>();
        configurationBuilder.Properties<ProjectAliasStatus>().HaveConversion<SnakeCaseEnumConverter<ProjectAliasStatus>>();
        configurationBuilder.Properties<ProjectFeedbackKind>().HaveConversion<SnakeCaseEnumConverter<ProjectFeedbackKind>>();
        configurationBuilder.Properties<MailProviderKind>().HaveConversion<SnakeCaseEnumConverter<MailProviderKind>>();
        configurationBuilder.Properties<MailCredentialStrategy>().HaveConversion<SnakeCaseEnumConverter<MailCredentialStrategy>>();
        configurationBuilder.Properties<MailSecurity>().HaveConversion<SnakeCaseEnumConverter<MailSecurity>>();
        configurationBuilder.Properties<TaskCreator>().HaveConversion<SnakeCaseEnumConverter<TaskCreator>>();
        configurationBuilder.Properties<TaskKind>().HaveConversion<SnakeCaseEnumConverter<TaskKind>>();
        configurationBuilder.Properties<TaskTriggerKind>().HaveConversion<SnakeCaseEnumConverter<TaskTriggerKind>>();
        configurationBuilder.Properties<AgentTaskStatus>().HaveConversion<SnakeCaseEnumConverter<AgentTaskStatus>>();
        configurationBuilder.Properties<ExecutorSessionStatus>().HaveConversion<SnakeCaseEnumConverter<ExecutorSessionStatus>>();
        configurationBuilder.Properties<ProposalKind>().HaveConversion<SnakeCaseEnumConverter<ProposalKind>>();
        configurationBuilder.Properties<ProposalStatus>().HaveConversion<SnakeCaseEnumConverter<ProposalStatus>>();
        configurationBuilder.Properties<NotificationPriority>().HaveConversion<SnakeCaseEnumConverter<NotificationPriority>>();
        configurationBuilder.Properties<NotificationOutcome>().HaveConversion<SnakeCaseEnumConverter<NotificationOutcome>>();
        configurationBuilder.Properties<ArtifactKind>().HaveConversion<SnakeCaseEnumConverter<ArtifactKind>>();
        configurationBuilder.Properties<ArtifactSource>().HaveConversion<SnakeCaseEnumConverter<ArtifactSource>>();
        configurationBuilder.Properties<InputRequestMode>().HaveConversion<SnakeCaseEnumConverter<InputRequestMode>>();
        configurationBuilder.Properties<InputRequestStatus>().HaveConversion<SnakeCaseEnumConverter<InputRequestStatus>>();
        configurationBuilder.Properties<ToolPinState>().HaveConversion<SnakeCaseEnumConverter<ToolPinState>>();

        // SQLite cannot compare or order DateTimeOffset / decimal values natively; binary and REAL storage keeps
        // range queries and sums on the server.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<decimal>().HaveConversion<double>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity picks its schema version from the host's IdentityOptions, which design-time and test contexts do not
        // have. The base call configures schema 1; the schema 3 additions (passkeys, key lengths) are applied here.
        base.OnModelCreating(builder);
        builder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.Property(entity => entity.PhoneNumber).HasMaxLength(256);
            user.HasMany<IdentityUserPasskey<OwnerId>>().WithOne().HasForeignKey(passkey => passkey.UserId).IsRequired();
        });
        builder.Entity<IdentityUserPasskey<OwnerId>>(passkey =>
        {
            passkey.HasKey(entity => entity.CredentialId);
            passkey.Property(entity => entity.CredentialId).HasMaxLength(1024);
            passkey.OwnsOne(entity => entity.Data).ToJson();
        });
        builder.Entity<IdentityUserLogin<OwnerId>>(login =>
        {
            login.Property(entity => entity.LoginProvider).HasMaxLength(128);
            login.Property(entity => entity.ProviderKey).HasMaxLength(128);
        });
        builder.Entity<IdentityUserToken<OwnerId>>(token =>
        {
            token.Property(entity => entity.LoginProvider).HasMaxLength(128);
            token.Property(entity => entity.Name).HasMaxLength(128);
        });
        builder.Entity<Role>().ToTable("roles");
        builder.Entity<IdentityUserRole<OwnerId>>().ToTable("user_roles");
        builder.Entity<IdentityUserLogin<OwnerId>>().ToTable("user_logins");
        builder.Entity<IdentityUserClaim<OwnerId>>().ToTable("user_claims");
        builder.Entity<IdentityUserToken<OwnerId>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<OwnerId>>().ToTable("role_claims");
        builder.Entity<IdentityUserPasskey<OwnerId>>().ToTable("user_passkeys");

        builder.Entity<UserPreferences>(preferences =>
        {
            preferences.HasKey(entity => entity.UserId);
            preferences.HasOne<User>().WithOne().HasForeignKey<UserPreferences>(entity => entity.UserId);
        });

        builder.Entity<AuthTicket>(ticket =>
        {
            ticket.HasKey(entity => entity.Id);
            ticket.HasIndex(entity => entity.ExpiresAt);
            ticket.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            OwnerFilter(ticket);
        });

        builder.Entity<ApiToken>(token =>
        {
            token.HasKey(entity => entity.Id);
            token.HasIndex(entity => entity.TokenHash).IsUnique();
            token.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            token.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.TaskId);
            OwnerFilter(token);
        });

        builder.Entity<Session>(session =>
        {
            session.HasKey(entity => entity.Id);
            // Unique among open sessions only: after an idle close the same client id starts a new session.
            session.HasIndex(entity => new { entity.ApiTokenId, entity.ExternalId }).IsUnique().HasFilter("closed_at IS NULL");
            session.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            session.HasOne<ApiToken>().WithMany().HasForeignKey(entity => entity.ApiTokenId);
            OwnerFilter(session);
        });

        builder.Entity<Run>(run =>
        {
            run.HasKey(entity => entity.Id);
            run.HasIndex(entity => new { entity.OwnerId, entity.StartedAt });
            run.HasIndex(entity => entity.SessionId);
            run.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            run.HasOne<Session>().WithMany().HasForeignKey(entity => entity.SessionId);
            run.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.TaskId);
            OwnerFilter(run);
        });

        builder.Entity<RunStep>(step =>
        {
            step.HasKey(entity => entity.Id);
            step.HasIndex(entity => new { entity.RunId, entity.Sequence }).IsUnique();
            step.HasOne<Run>().WithMany().HasForeignKey(entity => entity.RunId);
            OwnerFilter(step);
        });

        builder.Entity<PendingAction>(action =>
        {
            action.HasKey(entity => entity.Id);
            action.HasIndex(entity => new { entity.SessionId, entity.Status });
            action.HasIndex(entity => new { entity.Status, entity.ExpiresAt }).HasFilter("queued = 1");
            action.HasIndex(entity => entity.Nonce).IsUnique();
            action.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            action.HasOne<Session>().WithMany().HasForeignKey(entity => entity.SessionId);
            action.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.TaskId);
            OwnerFilter(action);
        });

        builder.Entity<PendingInputRequest>(request =>
        {
            request.ToTable("pending_input_requests");
            request.HasKey(entity => entity.Id);
            request.HasIndex(entity => new { entity.Status, entity.ExpiresAt });
            request.HasIndex(entity => entity.Nonce).IsUnique();
            request.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            request.HasOne<Session>().WithMany().HasForeignKey(entity => entity.SessionId);
            request.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.TaskId);
            request.Property(entity => entity.Status).IsConcurrencyToken();
            OwnerFilter(request);
        });

        builder.Entity<AuditEvent>(audit =>
        {
            audit.HasKey(entity => entity.Id);
            audit.HasIndex(entity => new { entity.OwnerId, entity.CreatedAt });
            audit.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            audit.HasOne<Run>().WithMany().HasForeignKey(entity => entity.RunId);
            OwnerFilter(audit);
        });

        builder.Entity<Secret>(secret =>
        {
            secret.HasKey(entity => entity.Id);
            secret.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            // SQLite treats NULLs as distinct, so a plain unique (owner_id, name) would allow duplicate instance names.
            secret.HasIndex(entity => entity.Name).IsUnique().HasFilter("owner_id IS NULL").HasDatabaseName("ix_secrets_instance_name");
            secret.HasIndex(entity => new { entity.OwnerId, entity.Name }).IsUnique().HasFilter("owner_id IS NOT NULL").HasDatabaseName("ix_secrets_owner_id_name");
            secret.HasQueryFilter(entity => entity.OwnerId == null || entity.OwnerId == CurrentOwnerIdOrNull);
        });

        builder.Entity<Project>(project =>
        {
            project.HasKey(entity => entity.Id);
            project.HasIndex(entity => new { entity.OwnerId, entity.Name });
            project.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            OwnerFilter(project);
        });

        builder.Entity<StoredArtifact>(artifact =>
        {
            artifact.ToTable("artifacts");
            artifact.HasKey(entity => entity.Id);
            artifact.HasIndex(entity => new { entity.OwnerId, entity.CreatedAt });
            artifact.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            OwnerFilter(artifact);
        });

        builder.Entity<DemoSeed>(seed =>
        {
            seed.ToTable("demo_seed");
            seed.HasKey(entity => entity.Id);
            seed.HasIndex(entity => new { entity.OwnerId, entity.Entity });
            seed.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            OwnerFilter(seed);
        });

        // Instance configuration, like the MCP server files: no owner, no filter.
        builder.Entity<McpToolPinRow>(pin =>
        {
            pin.ToTable("mcp_tool_pins");
            pin.HasKey(entity => new { entity.ServerId, entity.ToolName });
            pin.HasIndex(entity => entity.State);
        });

        builder.Entity<McpTaskHandle>(handle =>
        {
            handle.ToTable("mcp_task_handles");
            handle.HasKey(entity => entity.Id);
            handle.HasIndex(entity => entity.NextPollAt);
            handle.HasIndex(entity => new { entity.ServerId, entity.ServerTaskId }).IsUnique();
            handle.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            handle.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.TaskId);
            OwnerFilter(handle);
        });

        builder.Entity<StoredMailAccount>(account =>
        {
            account.ToTable("mail_accounts");
            account.HasKey(entity => entity.Id);
            account.HasIndex(entity => new { entity.OwnerId, entity.Alias }).IsUnique();
            account.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            OwnerFilter(account);
        });

        builder.Entity<NoteIndexEntry>(entry =>
        {
            entry.ToTable("note_index_entries");
            entry.HasKey(entity => entity.Id);
            entry.HasIndex(entity => new { entity.OwnerId, entity.Path });
            entry.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            OwnerFilter(entry);
        });

        builder.Entity<ProjectAlias>(alias =>
        {
            alias.HasKey(entity => entity.Id);
            alias.HasIndex(entity => new { entity.ProjectId, entity.Phrase }).IsUnique();
            alias.HasOne<Project>().WithMany().HasForeignKey(entity => entity.ProjectId);
            OwnerFilter(alias);
        });

        builder.Entity<ProjectFeedback>(feedback =>
        {
            feedback.HasKey(entity => entity.Id);
            feedback.HasIndex(entity => new { entity.ProjectId, entity.CreatedAt });
            feedback.HasOne<Project>().WithMany().HasForeignKey(entity => entity.ProjectId);
            feedback.HasOne<Run>().WithMany().HasForeignKey(entity => entity.RunId);
            OwnerFilter(feedback);
        });

        builder.Entity<Chunk>(chunk =>
        {
            chunk.HasKey(entity => entity.Id);
            chunk.Property(entity => entity.Id).HasMaxLength(ChunkId.Length);
            chunk.HasIndex(entity => new { entity.OwnerId, entity.Pinned });
            chunk.HasIndex(entity => entity.ProjectId);
            chunk.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            chunk.HasOne<Project>().WithMany().HasForeignKey(entity => entity.ProjectId);
            chunk.ToTable(table => table.HasCheckConstraint("ck_chunks_scope_project", "(scope = 'project') = (project_id IS NOT NULL)"));
            OwnerFilter(chunk);
        });

        builder.Entity<ChunkVersion>(version =>
        {
            version.HasKey(entity => entity.Id);
            version.HasIndex(entity => new { entity.ChunkId, entity.ReplacedAt });
            version.HasOne<Chunk>().WithMany().HasForeignKey(entity => entity.ChunkId);
            OwnerFilter(version);
        });

        builder.Entity<ChunkEdge>(edge =>
        {
            edge.HasKey(entity => new { entity.FromId, entity.ToId, entity.Kind });
            edge.HasIndex(entity => entity.ToId);
            edge.HasOne<Chunk>().WithMany().HasForeignKey(entity => entity.FromId);
            edge.HasOne<Chunk>().WithMany().HasForeignKey(entity => entity.ToId);
            edge.ToTable(table => table.HasCheckConstraint("ck_chunk_edges_no_self_supersede", "kind <> 'supersedes' OR from_id <> to_id"));
            OwnerFilter(edge);
        });

        builder.Entity<AgentTask>(task =>
        {
            task.ToTable("agent_tasks");
            task.HasKey(entity => entity.Id);
            task.HasIndex(entity => new { entity.OwnerId, entity.Name }).IsUnique().HasFilter("name IS NOT NULL");
            task.HasIndex(entity => new { entity.Status, entity.NextRunAt });
            task.HasIndex(entity => entity.ParentId);
            task.HasIndex(entity => entity.DeliveriesPending).HasFilter("deliveries_pending");
            task.HasIndex(entity => entity.OriginSessionId);
            task.Property(entity => entity.RowVersion).IsConcurrencyToken();
            task.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            task.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.ParentId);
            task.HasOne<Project>().WithMany().HasForeignKey(entity => entity.ProjectId);
            // No foreign key to sessions: SQLite would rebuild agent_tasks for it; the index serves the session lookups.
            OwnerFilter(task);
        });

        builder.Entity<ExecutorSession>(session =>
        {
            session.HasKey(entity => entity.Id);
            session.HasIndex(entity => entity.TaskId);
            session.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            session.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.TaskId);
            session.HasOne<ApiToken>().WithOne().HasForeignKey<ExecutorSession>(entity => entity.CallbackTokenId);
            OwnerFilter(session);
        });

        builder.Entity<Notification>(notification =>
        {
            notification.HasKey(entity => entity.Id);
            notification.HasIndex(entity => new { entity.OwnerId, entity.CreatedAt });
            notification.HasIndex(entity => new { entity.OwnerId, entity.Outcome });
            notification.HasIndex(entity => entity.DeliverAfter).HasFilter("deliver_after IS NOT NULL");
            notification.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            notification.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.TaskId);
            notification.HasOne<Notification>().WithMany().HasForeignKey(entity => entity.CoalescedInto);
            OwnerFilter(notification);
        });

        builder.Entity<Proposal>(proposal =>
        {
            proposal.HasKey(entity => entity.Id);
            proposal.HasIndex(entity => new { entity.OwnerId, entity.Status });
            proposal.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            proposal.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.TaskId);
            proposal.HasOne<ExecutorSession>().WithMany().HasForeignKey(entity => entity.ExecutorSessionId);
            OwnerFilter(proposal);
        });

        builder.Entity<FollowUp>(followUp =>
        {
            followUp.HasKey(entity => entity.Id);
            followUp.HasIndex(entity => entity.Nonce).IsUnique();
            followUp.HasIndex(entity => new { entity.SatelliteId, entity.ExpiresAt });
            followUp.HasOne<User>().WithMany().HasForeignKey(entity => entity.OwnerId);
            followUp.HasOne<PendingAction>().WithMany().HasForeignKey(entity => entity.PendingActionId);
            followUp.HasOne<AgentTask>().WithMany().HasForeignKey(entity => entity.TaskId);
            followUp.HasOne<Session>().WithMany().HasForeignKey(entity => entity.LinkedSessionId);
            OwnerFilter(followUp);
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

    private void IncrementRowVersions()
    {
        foreach (EntityEntry<IVersioned> entry in ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.RowVersion++;
        }
    }
}
