using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Api.Tests.Auth;
using Marshal.Data.Audit;
using Marshal.Data.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class ProposalsApiTests : IAsyncLifetime
{
    private readonly RecordingExecutor _executor = new();
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new MarshalApiFactory(AuthFactory.Local().ToSettings(0)) { TestServices = services => services.AddSingleton<IExecutor>(_executor) };
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task List_OpenProposalsOfTheUser_DetailHasTheDiff()
    {
        Guid proposal = await AddAsync(_user);
        (_, OwnerId other) = await Browser.SignInAsync(_factory, "user");
        await AddAsync(other);

        List<JsonElement>? proposals = await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/proposals", Ct);
        JsonElement detail = await _browser.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{proposal}", Ct);

        JsonElement listed = Assert.Single(proposals!);
        Assert.Equal((proposal.ToString(), "Add a feature file", "demo", "open", "code"),
            (listed.GetProperty("id").GetString(), listed.GetProperty("title").GetString(), listed.GetProperty("target").GetString(),
             listed.GetProperty("status").GetString(), listed.GetProperty("kind").GetString()));
        Assert.Equal("+feature\n", detail.GetProperty("diff").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.GetAsync($"/api/v1/proposals/{Guid.NewGuid()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Merge_MergesOnTheExecutorAndIsAudited()
    {
        Guid proposal = await AddAsync(_user);

        HttpResponseMessage merged = await _browser.PostAsync($"/api/v1/proposals/{proposal}/merge", null, Ct);
        HttpResponseMessage again = await _browser.PostAsync($"/api/v1/proposals/{proposal}/merge", null, Ct);

        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        Assert.Equal("merged", (await merged.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal([("merge", "h-1", "demo")], _executor.Calls);
        List<AuditEvent> audit = await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.Where(entry => entry.Action == "proposal.merge").ToListAsync(Ct));
        Assert.Equal(2, audit.Count);
        Assert.All(audit, entry => Assert.Equal((proposal.ToString(), _user.ToString()), (entry.Target, entry.ActorId)));
        Assert.Empty((await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/proposals", Ct))!);
    }

    [Fact]
    public async Task Merge_Conflict_StaysOpen()
    {
        Guid proposal = await AddAsync(_user);
        _executor.MergeError = new Error("executor.merge_conflict", "Does not merge cleanly.", ErrorType.Conflict);

        HttpResponseMessage response = await _browser.PostAsync($"/api/v1/proposals/{proposal}/merge", null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProposalStatus.Open, await _factory.WithDatabaseAsync(_user, db => db.Proposals.Where(row => row.Id == proposal).Select(row => row.Status).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Reject_DiscardsTheBranchAndIsAudited()
    {
        Guid proposal = await AddAsync(_user);

        HttpResponseMessage rejected = await _browser.PostAsync($"/api/v1/proposals/{proposal}/reject", null, Ct);

        Assert.Equal("rejected", (await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        Assert.Equal([("discard", "h-1", "demo")], _executor.Calls);
        Assert.Equal(1, await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.CountAsync(entry => entry.Action == "proposal.reject", Ct)));
    }

    [Fact]
    public async Task Reject_ExecutorHostDown_StillRejected()
    {
        Guid proposal = await AddAsync(_user);
        _executor.DiscardError = new Error("executor.discard_failed", "Unreachable.", ErrorType.Unavailable);

        HttpResponseMessage rejected = await _browser.PostAsync($"/api/v1/proposals/{proposal}/reject", null, Ct);

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
    }

    [Fact]
    public async Task OtherUsersProposal_NotFoundAndNotMerged()
    {
        (_, OwnerId other) = await Browser.SignInAsync(_factory, "user");
        Guid proposal = await AddAsync(other);

        HttpResponseMessage response = await _browser.PostAsync($"/api/v1/proposals/{proposal}/merge", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_executor.Calls);
    }

    private async Task<Guid> AddAsync(OwnerId owner)
    {
        TaskId task;
        using (_factory.Services.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Web }))
        {
            task = (await _factory.Services.GetRequiredService<ITaskStore>().CreateAsync(
                new NewTask { Goal = "Report the change.", Trigger = TaskTrigger.Now(), AwaitsResume = true }, Ct)).Value.Id;
        }

        return await _factory.WithDatabaseAsync(owner, async db =>
        {
            var session = Guid.NewGuid();
            db.ExecutorSessions.Add(new ExecutorSession
            {
                Id = session, OwnerId = owner, TaskId = task, Executor = "recording", Handle = "h-1", Target = "demo",
                Status = ExecutorSessionStatus.Done, StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow,
            });
            var proposal = new Proposal
            {
                Id = Guid.NewGuid(), OwnerId = owner, TaskId = task, ExecutorSessionId = session, Kind = ProposalKind.Code, Target = "demo",
                Branch = "marshal/h-1", Title = "Add a feature file", Diff = "+feature\n", CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Proposals.Add(proposal);
            await db.SaveChangesAsync(Ct);
            return proposal.Id;
        });
    }

    private sealed class RecordingExecutor : IExecutor
    {
        public List<(string Action, string Handle, string Target)> Calls { get; } = [];

        public Error? MergeError { get; set; }

        public Error? DiscardError { get; set; }

        public string Name => "recording";

        public string Description => "Records merges.";

        public Result ValidateSpec(JsonElement spec) => Result.Success();

        public Task<Result<ExecutorStarted>> StartAsync(ExecutorStart start, CancellationToken cancellationToken) =>
            Task.FromResult<Result<ExecutorStarted>>(new ExecutorStarted("h-1", "demo"));

        public Task<Result> CancelAsync(string handle, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result> MergeAsync(string handle, string target, CancellationToken cancellationToken)
        {
            Calls.Add(("merge", handle, target));
            return Task.FromResult(MergeError is { } error ? Result.Failure(error) : Result.Success());
        }

        public Task<Result> DiscardAsync(string handle, string target, CancellationToken cancellationToken)
        {
            Calls.Add(("discard", handle, target));
            return Task.FromResult(DiscardError is { } error ? Result.Failure(error) : Result.Success());
        }
    }
}
