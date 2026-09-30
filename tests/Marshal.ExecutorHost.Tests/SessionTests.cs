using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.ExecutorHost.Callbacks;
using Marshal.ExecutorHost.Sandbox;

namespace Marshal.ExecutorHost.Tests;

public sealed class SessionTests : IDisposable
{
    private readonly HostFactory _host = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token")]
    public async Task Sessions_WithoutTheHostToken_Unauthorized(string? token)
    {
        HttpClient client = _host.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.PostAsJsonAsync("/sessions", Request(), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_host.Sandbox.Runs);
    }

    [Fact]
    public async Task Start_RunsInItsOwnWorktree_PostsCompletionWithBranchAndDiff()
    {
        HttpResponseMessage response = await _host.Authorized().PostAsJsonAsync("/sessions", Request(), Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        JsonElement started = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        string id = started.GetProperty("id").GetString()!;
        ExecutorCallback completed = await _host.Callbacks.WaitForAsync("completed");
        Assert.Equal($"marshal/{id}", completed.Branch);
        Assert.Contains("+change for", completed.Diff, StringComparison.Ordinal);
        Assert.Equal("Added feature.txt with a note.", completed.Summary);
        (Uri url, string token, _) = _host.Callbacks.Sent.First();
        Assert.Equal((new Uri("http://marshal.test/api/v1/executor/callbacks/abc"), "callback-token"), (url, token));
        Assert.Contains(_host.Callbacks.Sent, sent => sent.Callback is { Type: "progress", Message: "Added feature.txt." });

        string demo = Path.Combine(_host.Repositories, "demo");
        Assert.Contains($"marshal/{id}", HostFactory.Git(demo, "branch", "--list"), StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(_host.Worktrees, id), HostFactory.Git(demo, "worktree", "list"), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_host.Worktrees, id)));
        Assert.Equal("main", HostFactory.Git(demo, "rev-parse", "--abbrev-ref", "HEAD").Trim());
    }

    [Fact]
    public async Task TwoSessionsOnOneRepository_SeparateWorktreesAndBranches()
    {
        _host.Sandbox.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HttpClient client = _host.Authorized();

        await client.PostAsJsonAsync("/sessions", Request(), Ct);
        await client.PostAsJsonAsync("/sessions", Request(), Ct);
        await WaitUntilAsync(() => _host.Sandbox.Runs.Count == 2);

        SandboxRun[] runs = [.. _host.Sandbox.Runs];
        Assert.NotEqual(runs[0].WorkDirectory, runs[1].WorkDirectory);
        string worktrees = HostFactory.Git(Path.Combine(_host.Repositories, "demo"), "worktree", "list");
        Assert.All(runs, run => Assert.Contains(run.WorkDirectory, worktrees, StringComparison.Ordinal));
        _host.Sandbox.Gate.SetResult();
    }

    [Fact]
    public async Task Delete_CancelsTheRunAndDropsItsBranch()
    {
        _host.Sandbox.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HttpClient client = _host.Authorized();
        JsonElement started = await (await client.PostAsJsonAsync("/sessions", Request(), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        string id = started.GetProperty("id").GetString()!;
        await WaitUntilAsync(() => !_host.Sandbox.Runs.IsEmpty);

        HttpResponseMessage deleted = await client.DeleteAsync($"/sessions/{id}", Ct);

        Assert.Equal(HttpStatusCode.Accepted, deleted.StatusCode);
        await _host.Callbacks.WaitForAsync("cancelled");
        Assert.DoesNotContain(_host.Callbacks.Sent, sent => sent.Callback.Type == "completed");
        await WaitUntilAsync(() => !HostFactory.Git(Path.Combine(_host.Repositories, "demo"), "branch", "--list").Contains($"marshal/{id}", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/sessions/unknown", Ct)).StatusCode);
    }

    [Fact]
    public async Task SandboxReportsAnError_Failed()
    {
        _host.Sandbox.IsError = true;
        _host.Sandbox.ExitCode = 1;

        await _host.Authorized().PostAsJsonAsync("/sessions", Request(), Ct);

        ExecutorCallback failed = await _host.Callbacks.WaitForAsync("failed");
        Assert.Equal("Added feature.txt with a note.", failed.Message);
    }

    [Theory]
    [InlineData("../demo", "http://marshal.test/api/v1/executor/callbacks/abc")]
    [InlineData("missing", "http://marshal.test/api/v1/executor/callbacks/abc")]
    [InlineData("demo", "http://attacker.example/steal")]
    [InlineData("demo", "http://marshal.test.attacker.example/api/v1/executor/callbacks/abc")]
    public async Task Start_InvalidRepositoryOrForeignCallback_BadRequest(string repository, string callbackUrl)
    {
        HttpResponseMessage response = await _host.Authorized().PostAsJsonAsync("/sessions", Request() with { Repository = repository, CallbackUrl = callbackUrl }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_host.Sandbox.Runs);
    }

    private static SessionStart Request() => new("demo", "Add a feature file.", null, "http://marshal.test/api/v1/executor/callbacks/abc", "callback-token");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20, Ct);
        Assert.True(condition());
    }

    private sealed record SessionStart(string Repository, string Prompt, string? BaseBranch, string CallbackUrl, string CallbackToken);
}
