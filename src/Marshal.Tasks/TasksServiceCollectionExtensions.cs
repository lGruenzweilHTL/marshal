using Marshal.Abstractions.Context;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.FollowUps;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Media;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.AiTasks;
using Marshal.Core.Conversation;
using Marshal.Tasks.Approvals;
using Marshal.Tasks.Control;
using Marshal.Tasks.Deliveries;
using Marshal.Tasks.Events;
using Marshal.Tasks.Executors;
using Marshal.Tasks.FollowUps;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Limits;
using Marshal.Tasks.Maintenance;
using Marshal.Tasks.Notifications;
using Marshal.Tasks.Running;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Marshal.Tasks.Tools;
using Marshal.Tasks.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Marshal.Tasks;

/// <summary>
/// Registers the task system (<c>tasks</c> in <c>marshal.yaml</c>): the store, the scheduler with its worker pool,
/// the task runners, the event bus with its trigger matcher, notifications and the approval queue.
/// </summary>
public static class TasksServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalTasks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TaskOptions>().Bind(configuration.GetSection("tasks"))
            .Validate(options => options.Workers is >= 1 and <= 16, "tasks.workers must be between 1 and 16.")
            .Validate(options => options.MaxDepth is >= 0 and <= 10, "tasks.maxDepth must be between 0 and 10.")
            .Validate(options => options.MaxActiveChildren >= 0, "tasks.maxActiveChildren cannot be negative.")
            .Validate(options => options.MinWakeupInterval >= TimeSpan.FromMinutes(1), "tasks.minWakeupInterval must be at least one minute.")
            .Validate(options => options.DailyCostCap >= 0, "tasks.dailyCostCap cannot be negative.")
            .Validate(options => options.ApprovalTtl >= TimeSpan.FromMinutes(1), "tasks.approvalTtl must be at least one minute.")
            .ValidateOnStart();

        services.AddSingleton<EfTaskStore>();
        services.AddSingleton<ITaskStore, LiveTaskStore>();
        services.AddSingleton(new LiveTopic(TaskLiveEvents.Tasks));
        services.AddSingleton(new LiveTopic(TaskLiveEvents.Notifications));
        services.AddSingleton(new LiveTopic(ConversationLiveEvents.Conversations));
        services.AddSingleton<ITaskSpending, EfTaskSpending>();
        services.AddSingleton<TaskBudgets>();
        services.AddSingleton<IDailyCostCap>(provider => provider.GetRequiredService<TaskBudgets>());
        services.AddSingleton<TaskPauses>();
        services.AddSingleton<IKillSwitch, EfKillSwitch>();
        services.AddSingleton<ITaskControl, TaskControl>();
        services.AddSingleton<ActiveTaskRuns>();
        services.AddSingleton<ITaskRunner, AgentTaskRunner>();
        services.AddSingleton<TaskContinuations>();
        services.AddSingleton<IBackgroundHandoff, TaskHandoff>();
        services.AddSingleton<ITool, MoveToBackgroundTool>();
        services.AddSingleton<IContextContributor, TaskPromptContributor>();
        services.AddSingleton<IToolSource, TaskToolSource>();
        services.AddSingleton<TaskSignals>();
        services.AddSingleton<ITaskScheduler>(provider => provider.GetRequiredService<TaskSignals>());
        services.AddSingleton<IHostedService, BackgroundTaskScheduler>();

        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection("notifications"))
            .Validate(options => options.CoalesceWindow >= TimeSpan.Zero && options.CoalesceWindow <= TimeSpan.FromMinutes(30), "notifications.coalesceWindow must be between 0 and 30 minutes.")
            .Validate(options => options.FollowUpTtl > TimeSpan.Zero, "notifications.followUpTtl must be positive.")
            .Validate(options => options.FollowUpReplyWindow >= TimeSpan.Zero && options.FollowUpReplyWindow <= options.FollowUpTtl, "notifications.followUpReplyWindow must be between 0 and followUpTtl.")
            .Validate(options => options.VoicePerHour >= 0, "notifications.voicePerHour cannot be negative.")
            .Validate(options => options.PublicUrl is null || options.PublicUrl.IsAbsoluteUri && (options.PublicUrl.Scheme == Uri.UriSchemeHttps || options.PublicUrl.Scheme == Uri.UriSchemeHttp), "notifications.publicUrl must be an absolute http(s) URL.")
            .ValidateOnStart();
        services.AddSingleton<NotificationRouter>();
        services.AddSingleton<INotificationSender>(provider => provider.GetRequiredService<NotificationRouter>());
        services.AddSingleton<IHostedService, NotificationDueLoop>();
        services.AddSingleton<IFollowUpStore, EfFollowUpStore>();
        services.AddSingleton<IFollowUpReplies, FollowUpReplies>();
        services.AddSingleton<INotificationInbox, EfNotificationInbox>();
        services.AddSingleton<INotificationHistory, EfNotificationHistory>();
        services.AddSingleton<IToolSource, NotificationToolSource>();
        services.AddSingleton<IDeliveryDispatcher, DeliveryDispatcher>();
        services.AddSingleton<IHostedService, ConditionalDeliveryTracker>();

        services.AddSingleton<ApprovalQueue>();
        services.AddSingleton<IApprovalQueue>(provider => provider.GetRequiredService<ApprovalQueue>());
        services.AddSingleton<IHostedService, ApprovalExpiryLoop>();
        services.AddSingleton<IHostedService, ApprovalPushActions>();

        services.AddOptions<ExecutorOptions>().Bind(configuration.GetSection("executors"))
            .Validate(options => options.CallbackBaseUrl is null || options.CallbackBaseUrl.IsAbsoluteUri, "executors.callbackBaseUrl must be an absolute URL.")
            .Validate(options => options.ClaudeCode.Url is null
                || (options.ClaudeCode.Url.IsAbsoluteUri && SecretName.IsValid(options.ClaudeCode.TokenSecret) && options.CallbackBaseUrl is not null && options.ClaudeCode.Repositories.Count > 0),
                "executors.claudeCode needs an absolute url, a tokenSecret name, at least one repository and executors.callbackBaseUrl.")
            .ValidateOnStart();
        services.AddHttpClient(ClaudeCodeExecutor.ClientName);
        if (!string.IsNullOrWhiteSpace(configuration["executors:claudeCode:url"]))
            services.AddSingleton<IExecutor, ClaudeCodeExecutor>();
        services.AddSingleton<ExecutorService>();
        services.AddSingleton<IExecutorCallbacks>(provider => provider.GetRequiredService<ExecutorService>());
        // tasks.run_executor is a built-in tool next to the task tool source; both share the namespace description.
        services.AddSingleton(TaskToolSource.Namespace);
        services.AddSingleton<ITool, RunExecutorTool>();
        services.AddSingleton<IExecutorSessions, EfExecutorSessions>();
        services.AddSingleton<IProposalReview, ProposalReview>();

        services.AddOptions<MaintenanceOptions>().Bind(configuration.GetSection("maintenance"))
            .Validate(options => IsCron(options.BackupSchedule), "maintenance.backupSchedule must be a five-field cron expression.")
            .Validate(options => IsCron(options.ConsolidationSchedule), "maintenance.consolidationSchedule must be a five-field cron expression.")
            .Validate(options => IsCron(options.RetentionSchedule), "maintenance.retentionSchedule must be a five-field cron expression.")
            .Validate(options => options.BackupCopies >= 1, "maintenance.backupCopies must be at least 1.")
            .Validate(options => options.PayloadRetention >= TimeSpan.FromDays(1) && options.MetadataRetention >= options.PayloadRetention,
                "maintenance.payloadRetention must be at least a day and metadataRetention at least as long.")
            .ValidateOnStart();
        services.AddSingleton<MemoryExportJob>();
        services.AddSingleton<ISystemJob, BackupJob>();
        services.AddSingleton<ISystemJob, RetentionPurgeJob>();
        services.AddSingleton<ITaskRunner, SystemTaskRunner>();
        services.AddSingleton<IHostedService, SystemTaskSeeder>();

        services.AddOptions<WebhookOptions>().Bind(configuration.GetSection("webhooks"))
            .Validate(options => options.DebounceSeconds is >= 0 and <= 3600, "webhooks.debounceSeconds must be between 0 and 3600.")
            .Validate(options => options.RequestsPerMinute is >= 1 and <= 6000, "webhooks.requestsPerMinute must be between 1 and 6000.")
            .ValidateOnStart();
        services.AddWebhookSource<UptimeKumaNormalizer>(UptimeKumaNormalizer.SourceName);
        services.AddWebhookSource<ProxmoxNormalizer>(ProxmoxNormalizer.SourceName);
        services.AddWebhookSource<SeerrNormalizer>(SeerrNormalizer.SourceName);
        services.TryAddSingleton<IJellyfinLinks, NoJellyfinLinks>();
        services.AddSingleton<WebhookIngest>();
        services.AddSingleton<IWebhookIngest>(provider => provider.GetRequiredService<WebhookIngest>());

        services.AddSingleton<ITriggerMatcher, DeclarativeTriggerMatcher>();
        services.AddSingleton<ChannelEventBus>();
        services.AddSingleton<IEventPublisher>(provider => provider.GetRequiredService<ChannelEventBus>());
        services.AddSingleton<IEventSubscriber>(provider => provider.GetRequiredService<ChannelEventBus>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<ChannelEventBus>());
        return services;
    }

    // A webhook source: its normalizer and its entry in the event sources list (the ingest tracks its last webhook).
    private static void AddWebhookSource<TNormalizer>(this IServiceCollection services, string source) where TNormalizer : class, IWebhookNormalizer
    {
        services.AddSingleton<IWebhookNormalizer, TNormalizer>();
        services.AddSingleton<IEventSource>(provider => provider.GetRequiredService<WebhookIngest>().EventSources.Single(state => state.Source == source));
    }

    private static bool IsCron(string? schedule) => TaskTriggers.Validate(TaskTrigger.Cron(schedule ?? "")).IsSuccess;
}
