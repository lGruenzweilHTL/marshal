using System.Net;
using System.Text;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Http;
using Marshal.Data.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Hooks;

/// <summary>
/// "Tell me when the NAS goes down": a watch on Uptime Kuma's <c>monitor.down</c> for <c>NAS</c>, and Uptime Kuma
/// posting the heartbeat. Everything but the model is real: endpoint, ingest, event bus, trigger matcher, scheduler.
/// </summary>
public sealed class HookWatchEndToEndTests : IDisposable
{
    private readonly MarshalApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task KumaDown_FiresTheWatchOnce_UntaintedWithTheUnwrappedPayload()
    {
        _factory.Model.ThenText("The NAS is down.").ThenText("Again?");
        OwnerId owner = await OwnerAsync();
        TaskRecord watch = await CreateWatchAsync(owner);
        HttpClient kuma = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);
        string body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Webhooks", "Fixtures", "kuma-down.json"));

        Assert.Equal(HttpStatusCode.Accepted, (await kuma.PostAsync("/api/v1/hooks/uptime-kuma", new StringContent(body, Encoding.UTF8, "application/json"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await kuma.PostAsync("/api/v1/hooks/uptime-kuma", new StringContent(body, Encoding.UTF8, "application/json"), Ct)).StatusCode);

        Run run = await WaitForRunAsync(owner, watch.Id);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        Assert.Single(_factory.Model.Requests);
        string input = _factory.Model.Requests[0].Messages[1].Text;
        Assert.Contains("\"type\":\"monitor.down\"", input, StringComparison.Ordinal);
        Assert.Contains("\"entity\":\"NAS\"", input, StringComparison.Ordinal);
        Assert.DoesNotContain("<untrusted-data", input, StringComparison.Ordinal);
        Assert.DoesNotContain("ECONNREFUSED", input, StringComparison.Ordinal);
        Assert.False(run.Tainted);
        Assert.Equal(1, await _factory.WithDatabaseAsync(owner, db => db.Runs.CountAsync(entry => entry.TaskId == watch.Id, Ct)));
    }

    private async Task<OwnerId> OwnerAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
    }

    private async Task<TaskRecord> CreateWatchAsync(OwnerId owner)
    {
        IServiceProvider services = _factory.Services;
        using (services.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Web }))
        {
            var task = new NewTask { Goal = "Tell me when the NAS goes down.", Trigger = TaskTrigger.Event("""{"source":"uptime-kuma","type":"monitor.down","entity":"NAS"}""") };
            return (await services.GetRequiredService<ITaskStore>().CreateAsync(task, Ct)).Value;
        }
    }

    private async Task<Run> WaitForRunAsync(OwnerId owner, TaskId task)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            Run? run = await _factory.WithDatabaseAsync(owner, db => db.Runs.FirstOrDefaultAsync(entry => entry.TaskId == task && entry.EndedAt != null, Ct));
            if (run is not null)
                return run;
            await Task.Delay(100, Ct);
        }

        throw new TimeoutException("The watch did not run.");
    }
}
