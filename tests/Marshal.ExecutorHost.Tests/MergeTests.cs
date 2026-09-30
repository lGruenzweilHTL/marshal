using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Marshal.ExecutorHost.Tests;

public sealed class MergeTests : IDisposable
{
    private readonly HostFactory _host = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Demo => Path.Combine(_host.Repositories, "demo");

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Merge_CompletedSession_MergesIntoItsBaseBranchAndDeletesTheBranch()
    {
        string id = await CompleteSessionAsync(baseBranch: null);

        HttpResponseMessage response = await _host.Authorized().PostAsJsonAsync($"/sessions/{id}/merge", new { repository = "demo" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("change for", HostFactory.Git(Demo, "show", "main:feature.txt"), StringComparison.Ordinal);
        Assert.DoesNotContain($"marshal/{id}", HostFactory.Git(Demo, "branch", "--list"), StringComparison.Ordinal);
        // main is checked out in the repository, so its files follow the merge.
        Assert.True(File.Exists(Path.Combine(Demo, "feature.txt")));
        Assert.Empty(HostFactory.Git(Demo, "status", "--porcelain").Trim());
    }

    [Fact]
    public async Task Merge_SessionStartedFromAnotherBranch_MergesIntoThatBranch()
    {
        HostFactory.Git(Demo, "branch", "develop");
        string id = await CompleteSessionAsync(baseBranch: "develop");

        HttpResponseMessage response = await _host.Authorized().PostAsJsonAsync($"/sessions/{id}/merge", new { repository = "demo" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("change for", HostFactory.Git(Demo, "show", "develop:feature.txt"), StringComparison.Ordinal);
        Assert.DoesNotContain("feature.txt", HostFactory.Git(Demo, "ls-tree", "--name-only", "main"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merge_Conflict_ChangesNothing()
    {
        string id = await CompleteSessionAsync(baseBranch: null);
        await File.WriteAllTextAsync(Path.Combine(Demo, "feature.txt"), "someone else's feature\n", Ct);
        HostFactory.Git(Demo, "add", "-A");
        HostFactory.Git(Demo, "-c", "user.name=Test", "-c", "user.email=test@localhost", "commit", "-q", "-m", "conflicting");
        string before = HostFactory.Git(Demo, "rev-parse", "main").Trim();

        HttpResponseMessage response = await _host.Authorized().PostAsJsonAsync($"/sessions/{id}/merge", new { repository = "demo" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());
        Assert.Equal(before, HostFactory.Git(Demo, "rev-parse", "main").Trim());
        Assert.Contains($"marshal/{id}", HostFactory.Git(Demo, "branch", "--list"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merge_UnknownSessionOrRepository_NotFoundOrBadRequest()
    {
        HttpClient client = _host.Authorized();

        HttpResponseMessage unknown = await client.PostAsJsonAsync($"/sessions/{Guid.NewGuid():N}/merge", new { repository = "demo" }, Ct);
        HttpResponseMessage badRepository = await client.PostAsJsonAsync($"/sessions/{Guid.NewGuid():N}/merge", new { repository = "../demo" }, Ct);
        HttpResponseMessage badId = await client.PostAsJsonAsync("/sessions/..%2Fmain/merge", new { repository = "demo" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRepository.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, badId.StatusCode);
    }

    [Fact]
    public async Task Merge_WithoutTheHostToken_Unauthorized()
    {
        string id = await CompleteSessionAsync(baseBranch: null);

        HttpResponseMessage response = await _host.CreateClient().PostAsJsonAsync($"/sessions/{id}/merge", new { repository = "demo" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains($"marshal/{id}", HostFactory.Git(Demo, "branch", "--list"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discard_DeletesTheBranchOnly()
    {
        string id = await CompleteSessionAsync(baseBranch: null);
        string before = HostFactory.Git(Demo, "rev-parse", "main").Trim();

        HttpResponseMessage response = await _host.Authorized().PostAsJsonAsync($"/sessions/{id}/discard", new { repository = "demo" }, Ct);
        HttpResponseMessage again = await _host.Authorized().PostAsJsonAsync($"/sessions/{id}/discard", new { repository = "demo" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.DoesNotContain($"marshal/{id}", HostFactory.Git(Demo, "branch", "--list"), StringComparison.Ordinal);
        Assert.Equal(before, HostFactory.Git(Demo, "rev-parse", "main").Trim());
    }

    [Fact]
    public async Task Merge_RunningSession_Conflict()
    {
        _host.Sandbox.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string id = await StartAsync(baseBranch: null);
        await WaitUntilAsync(() => !_host.Sandbox.Runs.IsEmpty);

        HttpResponseMessage response = await _host.Authorized().PostAsJsonAsync($"/sessions/{id}/merge", new { repository = "demo" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        _host.Sandbox.Gate.SetResult();
    }

    private async Task<string> CompleteSessionAsync(string? baseBranch)
    {
        string id = await StartAsync(baseBranch);
        await _host.Callbacks.WaitForAsync("completed");
        return id;
    }

    private async Task<string> StartAsync(string? baseBranch)
    {
        HttpResponseMessage response = await _host.Authorized().PostAsJsonAsync("/sessions", new
        {
            repository = "demo",
            prompt = "Add a feature file.",
            baseBranch,
            callbackUrl = "http://marshal.test/api/v1/executor/callbacks/abc",
            callbackToken = "callback-token",
        }, Ct);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20, Ct);
        Assert.True(condition());
    }
}
