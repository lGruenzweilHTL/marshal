using Marshal.Abstractions.Context;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.FollowUps;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Tasks.Approvals;
using Marshal.Tasks.Deliveries;
using Marshal.Tasks.Events;
using Marshal.Tasks.FollowUps;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Limits;
using Marshal.Tasks.Notifications;
using Marshal.Tasks.Running;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Marshal.Tasks.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<ITaskSpending, EfTaskSpending>();
        services.AddSingleton<TaskBudgets>();
        services.AddSingleton<TaskPauses>();
        services.AddSingleton<IKillSwitch, EfKillSwitch>();
        services.AddSingleton<ActiveTaskRuns>();
        services.AddSingleton<ITaskRunner, AgentTaskRunner>();
        services.AddSingleton<IContextContributor, TaskPromptContributor>();
        services.AddSingleton<IToolSource, TaskToolSource>();
        services.AddSingleton<TaskSignals>();
        services.AddSingleton<ITaskScheduler>(provider => provider.GetRequiredService<TaskSignals>());
        services.AddSingleton<IHostedService, BackgroundTaskScheduler>();

        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection("notifications"))
            .Validate(options => options.CoalesceWindow >= TimeSpan.Zero && options.CoalesceWindow <= TimeSpan.FromMinutes(30), "notifications.coalesceWindow must be between 0 and 30 minutes.")
            .Validate(options => options.FollowUpTtl > TimeSpan.Zero, "notifications.followUpTtl must be positive.")
            .Validate(options => options.VoicePerHour >= 0, "notifications.voicePerHour cannot be negative.")
            .Validate(options => options.PublicUrl is null || options.PublicUrl.IsAbsoluteUri && (options.PublicUrl.Scheme == Uri.UriSchemeHttps || options.PublicUrl.Scheme == Uri.UriSchemeHttp), "notifications.publicUrl must be an absolute http(s) URL.")
            .ValidateOnStart();
        services.AddSingleton<NotificationRouter>();
        services.AddSingleton<INotificationSender>(provider => provider.GetRequiredService<NotificationRouter>());
        services.AddSingleton<IHostedService, NotificationDueLoop>();
        services.AddSingleton<IFollowUpStore, EfFollowUpStore>();
        services.AddSingleton<INotificationInbox, EfNotificationInbox>();
        services.AddSingleton<IToolSource, NotificationToolSource>();
        services.AddSingleton<IDeliveryDispatcher, DeliveryDispatcher>();
        services.AddSingleton<IHostedService, ConditionalDeliveryTracker>();

        services.AddSingleton<ApprovalQueue>();
        services.AddSingleton<IApprovalQueue>(provider => provider.GetRequiredService<ApprovalQueue>());
        services.AddSingleton<IHostedService, ApprovalExpiryLoop>();
        services.AddSingleton<IHostedService, ApprovalPushActions>();

        services.AddSingleton<ITriggerMatcher, DeclarativeTriggerMatcher>();
        services.AddSingleton<ChannelEventBus>();
        services.AddSingleton<IEventPublisher>(provider => provider.GetRequiredService<ChannelEventBus>());
        services.AddSingleton<IEventSubscriber>(provider => provider.GetRequiredService<ChannelEventBus>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<ChannelEventBus>());
        return services;
    }
}
