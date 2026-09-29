using Marshal.Abstractions.Context;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Tasks.Events;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Limits;
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
/// the task runners and the event bus with its trigger matcher.
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
            .ValidateOnStart();

        services.AddSingleton<ITaskStore, EfTaskStore>();
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

        services.AddSingleton<ITriggerMatcher, DeclarativeTriggerMatcher>();
        services.AddSingleton<ChannelEventBus>();
        services.AddSingleton<IEventPublisher>(provider => provider.GetRequiredService<ChannelEventBus>());
        services.AddSingleton<IEventSubscriber>(provider => provider.GetRequiredService<ChannelEventBus>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<ChannelEventBus>());
        return services;
    }
}
