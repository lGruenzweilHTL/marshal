using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tokens;
using Marshal.Data.Tasks;
using Marshal.Tasks.Executors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class ExecutorCallbackApiTests : IAsyncLifetime
{
    private readonly StubExecutor _executor = new();
    private MarshalApiFactory _factory = null!;
    private OwnerId _owner;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new MarshalApiFactory(new Dictionary<string, string?> { ["executors:callbackBaseUrl"] = "http://marshal.test/" })
        {
            TestServices = services => services.AddSingleton<IExecutor>(_executor),
        };
        _ = _factory.Server;
        using IServiceScope scope = _factory.Services.CreateScope();
        _owner = await scope.ServiceProvider.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Completed_WithTheSessionsToken_NoContentAndProposal()
    {
        (Guid session, string token) = await StartAsync();

        HttpResponseMessage response = await PostAsync(session, token, new { type = "completed", branch = "marshal/s-1", diff = "+change", summary = "Added a feature file.", costUsd = 0.12 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Proposal proposal = await _factory.WithDatabaseAsync(_owner, db => db.Proposals.SingleAsync(Ct));
        Assert.Equal(("marshal/s-1", "+change", "Added a feature file."), (proposal.Branch, proposal.Diff, proposal.Title));
    }

    [Fact]
    public async Task TokenOfAnotherSession_Forbidden()
    {
        (_, string token) = await StartAsync();
        (Guid other, _) = await StartAsync();

        HttpResponseMessage response = await PostAsync(other, token, new { type = "completed", branch = "b", diff = "d" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ExecutorSessionStatus.Running, await _factory.WithDatabaseAsync(_owner, db => db.ExecutorSessions.Where(row => row.Id == other).Select(row => row.Status).SingleAsync(Ct)));
    }

    [Fact]
    public async Task TokenReusedAfterTheSessionEnded_Unauthorized()
    {
        (Guid session, string token) = await StartAsync();
        await PostAsync(session, token, new { type = "failed", message = "Exited with code 1." });

        HttpResponseMessage response = await PostAsync(session, token, new { type = "completed", branch = "b", diff = "d" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StandardApiToken_Unauthorized()
    {
        (Guid session, _) = await StartAsync();
        string token = await _factory.CreateTokenAsync(ApiScopes.Admin, ApiScopes.TasksWrite);

        HttpResponseMessage response = await PostAsync(session, token, new { type = "completed", branch = "b", diff = "d" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CallbackToken_OnOtherEndpoints_Unauthorized()
    {
        (_, string token) = await StartAsync();
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage runs = await client.GetAsync("/api/v1/runs", Ct);
        HttpResponseMessage approvals = await client.GetAsync("/api/v1/approvals", Ct);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (runs.StatusCode, approvals.StatusCode));
    }

    [Fact]
    public async Task Upload_StoresATaintedExecutorArtifactOfTheTasksOwner()
    {
        (Guid session, string token) = await StartAsync();

        HttpResponseMessage response = await UploadAsync(session, token, "out/case.scad", "cube(10);"u8.ToArray());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Marshal.Data.Artifacts.StoredArtifact stored = await _factory.WithDatabaseAsync(_owner, db => db.Artifacts.SingleAsync(Ct));
        TaskId task = await _factory.WithDatabaseAsync(_owner, db => db.ExecutorSessions.Where(row => row.Id == session).Select(row => row.TaskId).SingleAsync(Ct));
        Assert.Equal(("case.scad", ArtifactSource.Executor, true, $"task:{task}"), (stored.Name, stored.Source, stored.Tainted, stored.SourceReference));
    }

    [Fact]
    public async Task Upload_TokenOfAnotherSession_ForbiddenAndNothingStored()
    {
        (_, string token) = await StartAsync();
        (Guid other, _) = await StartAsync();

        HttpResponseMessage response = await UploadAsync(other, token, "a.txt", "a"u8.ToArray());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await _factory.WithDatabaseAsync(_owner, db => db.Artifacts.AnyAsync(Ct)));
    }

    [Fact]
    public async Task Upload_AfterTheSessionEnded_Unauthorized()
    {
        (Guid session, string token) = await StartAsync();
        await PostAsync(session, token, new { type = "failed", message = "Exited with code 1." });

        HttpResponseMessage response = await UploadAsync(session, token, "a.txt", "a"u8.ToArray());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_OverTheLimitOrQuota_ProblemWithTheReason()
    {
        await using var factory = new MarshalApiFactory(new Dictionary<string, string?>
        {
            ["executors:callbackBaseUrl"] = "http://marshal.test/",
            ["artifacts:maxUploadMb"] = "1",
            ["artifacts:quotaMb"] = "1",
        })
        {
            TestServices = services => services.AddSingleton<IExecutor>(_executor),
        };
        (Guid session, string token) = await StartAsync(factory);

        HttpResponseMessage tooLarge = await UploadAsync(factory, session, token, "big.bin", new byte[(1024 * 1024) + 1]);
        await UploadAsync(factory, session, token, "one.bin", new byte[700 * 1024]);
        HttpResponseMessage overQuota = await UploadAsync(factory, session, token, "two.bin", new byte[700 * 1024]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Contains("1 MB", (await tooLarge.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, overQuota.StatusCode);
        Assert.Equal("artifacts.quota_exceeded", (await overQuota.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task UploadThenCompleted_ArtifactExistsWhenTheFollowUpRunStarts()
    {
        var seen = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Model.Then(async (_, _, ct) =>
        {
            seen.TrySetResult(await _factory.WithDatabaseAsync(_owner, db => db.Artifacts.Select(row => row.Name).ToArrayAsync(ct)));
            return new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "The bracket is ready."));
        });
        (Guid session, string token) = await StartAsync();

        await UploadAsync(session, token, "bracket.stl", "solid bracket\nfacet normal 0 0 1\nendsolid bracket\n"u8.ToArray());
        await PostAsync(session, token, new { type = "completed", branch = "marshal/s-1", diff = "+change", summary = "Modelled a bracket." });

        Assert.Equal(["bracket.stl"], await seen.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct));
    }

    private Task<HttpResponseMessage> UploadAsync(Guid session, string token, string name, byte[] content) => UploadAsync(_factory, session, token, name, content);

    private static async Task<HttpResponseMessage> UploadAsync(MarshalApiFactory factory, Guid session, string token, string name, byte[] content)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var body = new ByteArrayContent(content);
        body.Headers.Add("X-Artifact-Name", Uri.EscapeDataString(name));
        return await client.PostAsync($"/api/v1/executor/callbacks/{session}/artifacts", body, Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(Guid session, string token, object body)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.PostAsJsonAsync($"/api/v1/executor/callbacks/{session}", body, Ct);
    }

    private Task<(Guid Session, string Token)> StartAsync() => StartAsync(_factory);

    private async Task<(Guid Session, string Token)> StartAsync(MarshalApiFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        OwnerId owner = await scope.ServiceProvider.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
        using (scope.ServiceProvider.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Api }))
        {
            Result<StartedSession> started = await scope.ServiceProvider.GetRequiredService<ExecutorService>().StartAsync(
                "stub", JsonSerializer.SerializeToElement(new { repository = "demo" }), new NewTask { Goal = "Report.", Trigger = TaskTrigger.Now() }, Ct);
            return (started.Value.SessionId, _executor.Starts[^1].CallbackToken);
        }
    }

    private sealed class StubExecutor : IExecutor
    {
        public List<ExecutorStart> Starts { get; } = [];

        public string Name => "stub";

        public string Description => "Does stub work.";

        public Result ValidateSpec(JsonElement spec) => Result.Success();

        public Task<Result<ExecutorStarted>> StartAsync(ExecutorStart start, CancellationToken cancellationToken)
        {
            Starts.Add(start);
            return Task.FromResult(Result.Success(new ExecutorStarted($"s-{Starts.Count}", "demo")));
        }

        public Task<Result> CancelAsync(string handle, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result> MergeAsync(string handle, string target, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result> DiscardAsync(string handle, string target, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }
}
