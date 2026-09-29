using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Api.Tests.Auth;
using Marshal.Data.Audit;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class ApprovalsApiTests : IAsyncLifetime
{
    private readonly FakeTool _unlock = FakeTool.Confirm("test.unlock").Returns("Unlocked.");
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new MarshalApiFactory(AuthFactory.Local().ToSettings(0), _unlock);
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
        _factory.Model.ThenText("Done.").ThenText("Done.");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task List_PendingApprovalsWithReasonsTaintAndRun_OwnerScoped()
    {
        (TaskId task, PendingActionRecord queued) = await QueueAsync(_user);
        (HttpClient other, _) = await Browser.SignInAsync(_factory, "user");

        List<JsonElement>? approvals = await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/approvals", Ct);
        List<JsonElement>? foreign = await other.GetFromJsonAsync<List<JsonElement>>("/api/v1/approvals", Ct);

        JsonElement approval = Assert.Single(approvals!);
        Assert.Equal(queued.Id.ToString(), approval.GetProperty("id").GetString());
        Assert.Equal(task.ToString(), approval.GetProperty("taskId").GetString());
        Assert.Equal("test.unlock", approval.GetProperty("tool").GetString());
        Assert.Equal("front", approval.GetProperty("arguments").GetProperty("door").GetString());
        Assert.Equal(["Unlocking needs your approval.", "This run read an email, which could contain injected instructions."],
            approval.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetProperty("sentence").GetString()));
        JsonElement taint = Assert.Single(approval.GetProperty("taint").EnumerateArray());
        Assert.Equal(("mail", 2), (taint.GetProperty("kind").GetString(), taint.GetProperty("firstStep").GetInt32()));
        Assert.Equal(queued.ExpiresAt, approval.GetProperty("expiresAt").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));
        Assert.Empty(foreign!);
    }

    [Fact]
    public async Task Approve_ExecutesOnceAndIsAudited()
    {
        (_, PendingActionRecord queued) = await QueueAsync(_user);

        HttpResponseMessage approved = await _browser.PostAsync($"/api/v1/approvals/{queued.Id}/approve", null, Ct);
        HttpResponseMessage again = await _browser.PostAsync($"/api/v1/approvals/{queued.Id}/approve", null, Ct);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("approved", (await approved.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Single(_unlock.Invocations);
        List<AuditEvent> audit = await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.Where(entry => entry.Action == "approval.approve").ToListAsync(Ct));
        Assert.Equal(2, audit.Count); // the refused second attempt is audited too
        Assert.All(audit, entry => Assert.Equal((queued.Id.ToString(), _user.ToString()), (entry.Target, entry.ActorId)));
    }

    [Fact]
    public async Task Reject_NothingExecutes()
    {
        (_, PendingActionRecord queued) = await QueueAsync(_user);

        HttpResponseMessage rejected = await _browser.PostAsync($"/api/v1/approvals/{queued.Id}/reject", null, Ct);

        Assert.Equal("rejected", (await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        Assert.Empty(_unlock.Invocations);
        Assert.Equal(1, await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.CountAsync(entry => entry.Action == "approval.reject", Ct)));
        Assert.Empty((await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/approvals", Ct))!);
    }

    [Fact]
    public async Task Approve_OtherUsersApproval_NotFound()
    {
        (_, PendingActionRecord queued) = await QueueAsync(_user);
        (HttpClient other, _) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage response = await other.PostAsync($"/api/v1/approvals/{queued.Id}/approve", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_unlock.Invocations);
    }

    // A task waiting for the decision, and its queued approval, as the background run would leave them.
    private async Task<(TaskId Task, PendingActionRecord Approval)> QueueAsync(OwnerId owner)
    {
        IServiceProvider services = _factory.Services;
        var context = new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false };
        using (services.GetRequiredService<IExecutionContextAccessor>().Use(context))
        {
            ITaskStore tasks = services.GetRequiredService<ITaskStore>();
            TaskRecord task = (await tasks.CreateAsync(new NewTask { Goal = "Let the plumber in.", Trigger = TaskTrigger.At(DateTimeOffset.UtcNow.AddDays(1)) }, Ct)).Value;
            task = (await tasks.UpdateAsync(task with { Status = AgentTaskStatus.Waiting, NextRunAt = null }, Ct)).Value;
            PendingActionRecord queued = await services.GetRequiredService<IApprovalQueue>().EnqueueAsync(new FreezeRequest(
                null,
                null,
                _unlock.Descriptor.Id,
                """{"door":"front"}""",
                "Unlock the front door",
                [
                    new ConfirmationReason(ConfirmationReasonCode.Rule, "Unlocking needs your approval."),
                    new ConfirmationReason(ConfirmationReasonCode.Taint, "This run read an email, which could contain injected instructions."),
                ],
                "en",
                DateTimeOffset.UtcNow.AddMinutes(2),
                task.Id,
                null,
                [new TaintSource("mail", 2)]), Ct);
            return (task.Id, queued);
        }
    }
}
