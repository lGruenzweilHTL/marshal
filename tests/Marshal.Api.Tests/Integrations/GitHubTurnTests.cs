using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Integrations.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Integrations;

public sealed class GitHubTurnTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WebTurn_ReadsAPullRequest_StoresTheDiffAndTaintsTheRun()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["github:tokenSecret"] = "github_pat";
        settings["github:repos:marshal"] = "lukas/marshal";
        await using var factory = new MarshalApiFactory(settings) { TestServices = services => services.AddSingleton<IGitHubClient>(new OnePullRequest()) };
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");
        factory.Model.ThenToolCall("repos_pr", new { repo = "marshal", number = 42 }).ThenToolCall("artifacts_list", new { source = "github" }).ThenText("PR 42 adds displays.");

        HttpResponseMessage response = await browser.PostAsJsonAsync("/api/v1/conversation/turns", new { text = "what's in PR 42?" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement artifact = Assert.Single((await browser.GetFromJsonAsync<JsonElement>("/api/v1/artifacts?source=github", Ct)).GetProperty("items").EnumerateArray());
        Assert.Equal(("marshal-pr-42.diff", "diff", true), (artifact.GetProperty("name").GetString(), artifact.GetProperty("kind").GetString(), artifact.GetProperty("tainted").GetBoolean()));
        string runId = (await browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/runs", Ct))![0].GetProperty("id").GetString()!;
        JsonElement run = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}", Ct);
        Assert.Contains("\"github\"", run.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class OnePullRequest : IGitHubClient
    {
        public Task<Result<GitHubPull>> GetPullAsync(GitHubRepo repo, int number, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new GitHubPull(42, "Add displays", "lukas", "open", false, false, "feat/displays", "abc123", "master", "Shows artifacts on TVs.", 1, 2, 1)));

        public async Task<Result> DownloadAsync(GitHubRepo repo, GitHubDownload download, long id, Func<Stream, CancellationToken, Task> read, CancellationToken cancellationToken)
        {
            await read(new MemoryStream("diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1 +1 @@\n-old\n+new\n"u8.ToArray()), cancellationToken);
            return Result.Success();
        }

        public Task<Result<IReadOnlyList<GitHubPullSummary>>> ListPullsAsync(GitHubRepo repo, string state, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<GitHubIssueSummary>>> ListIssuesAsync(GitHubRepo repo, string state, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<GitHubIssue>> GetIssueAsync(GitHubRepo repo, int number, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<GitHubComment>>> ListCommentsAsync(GitHubRepo repo, int number, int total, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<GitHubRun>>> ListRunsAsync(GitHubRepo repo, string? branch, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<GitHubJob>>> ListJobsAsync(GitHubRepo repo, long runId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
