using Marshal.Abstractions.Context;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Tasks.Running;
using Marshal.Tasks.Store;
using Marshal.Tasks.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marshal.Tasks.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void AddMarshalTasks_RegistersStoreSchedulerAndAgentRunner()
    {
        var services = new ServiceCollection();

        services.AddMarshalTasks(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITaskStore) && descriptor.ImplementationType == typeof(EfTaskStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITaskRunner) && descriptor.ImplementationType == typeof(AgentTaskRunner));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IContextContributor) && descriptor.ImplementationType == typeof(TaskPromptContributor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITaskScheduler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IToolSource) && descriptor.ImplementationType == typeof(TaskToolSource));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IKillSwitch));
    }
}
