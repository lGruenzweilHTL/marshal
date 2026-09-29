using Marshal.Abstractions.Context;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Running;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marshal.Tasks;

/// <summary>
/// Registers the task system (<c>tasks</c> in <c>marshal.yaml</c>): the store, the scheduler with its worker pool
/// and the task runners.
/// </summary>
public static class TasksServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalTasks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TaskOptions>().Bind(configuration.GetSection("tasks"))
            .Validate(options => options.Workers is >= 1 and <= 16, "tasks.workers must be between 1 and 16.")
            .ValidateOnStart();

        services.AddSingleton<ITaskStore, EfTaskStore>();
        services.AddSingleton<ActiveTaskRuns>();
        services.AddSingleton<ITaskRunner, AgentTaskRunner>();
        services.AddSingleton<IContextContributor, TaskPromptContributor>();
        services.AddSingleton<BackgroundTaskScheduler>();
        services.AddSingleton<ITaskScheduler>(provider => provider.GetRequiredService<BackgroundTaskScheduler>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<BackgroundTaskScheduler>());
        return services;
    }
}
