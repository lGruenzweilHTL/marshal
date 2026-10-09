using Marshal.Abstractions;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.GitHub.Tests;

public sealed class GitHubRestClientTests : IAsyncLifetime
{
    internal static readonly GitHubRepo Marshal = new("marshal", "lukas", "marshal");

    private FakeGitHub _github = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _github = await FakeGitHub.StartAsync();

    public async ValueTask DisposeAsync() => await _github.DisposeAsync();

    [Fact]
    public async Task Pulls_ParsedWithTokenAcceptAndPinnedVersion()
    {
        Result<IReadOnlyList<GitHubPullSummary>> pulls = await Client().ListPullsAsync(Marshal, "open", 30, Ct);

        Assert.Equal(new GitHubPullSummary(42, "Add displays", "lukas", false, "feat/displays", "master", DateTimeOffset.Parse("2026-10-09T10:00:00Z", null)), pulls.Value[0]);
        Assert.True(pulls.Value[1].Draft);
        (string Method, string Host, string PathAndQuery, string Authorization, string Accept, string ApiVersion) request = Assert.Single(_github.Requests);
        Assert.Equal(("GET", "api.github.com", "/repos/lukas/marshal/pulls?state=open&sort=updated&direction=desc&per_page=30", $"Bearer {FakeGitHub.Token}", "application/vnd.github+json", GitHubRestClient.ApiVersion),
            (request.Method, request.Host, request.PathAndQuery, request.Authorization, request.Accept, request.ApiVersion));
    }

    [Fact]
    public async Task Pull_ParsedWithHeadSha()
    {
        GitHubPull pull = (await Client().GetPullAsync(Marshal, 42, Ct)).Value;

        Assert.Equal(new GitHubPull(42, "Add displays", "lukas", "open", false, false, "feat/displays", "abc1234def", "master", "Shows artifacts on TVs.", 3, 120, 4), pull);
    }

    [Fact]
    public async Task Issues_LeaveOutPullRequests()
    {
        IReadOnlyList<GitHubIssueSummary> issues = (await Client().ListIssuesAsync(Marshal, "open", Ct)).Value;

        Assert.Equal([44, 40], issues.Select(issue => issue.Number));
        Assert.Equal(["ui", "idea"], issues[1].Labels);
    }

    [Fact]
    public async Task Issue_ThatIsAPullRequest_NotFound()
    {
        Assert.Equal("repos.not_found", (await Client().GetIssueAsync(Marshal, 42, Ct)).Error.Code);
    }

    [Fact]
    public async Task Comments_ReadFromTheLastPage()
    {
        IReadOnlyList<GitHubComment> comments = (await Client().ListCommentsAsync(Marshal, 44, 250, 1, Ct)).Value;

        Assert.Equal("Confirmed.", Assert.Single(comments).Body);
        Assert.EndsWith("/issues/44/comments?per_page=100&page=3", _github.Requests.Last().PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunsAndJobs_Parsed()
    {
        GitHubRun run = Assert.Single((await Client().ListRunsAsync(Marshal, "feat/displays", 20, Ct)).Value);
        IReadOnlyList<GitHubJob> jobs = (await Client().ListJobsAsync(Marshal, 7001, Ct)).Value;

        Assert.Equal((7001L, "CI", "failure", "abc1234def5678"), (run.Id, run.Workflow, run.Conclusion, run.Sha));
        Assert.Contains("branch=feat%2Fdisplays", _github.Requests.First().PathAndQuery, StringComparison.Ordinal);
        Assert.Equal([new GitHubStep("Checkout", "success"), new GitHubStep("Test", "failure")], jobs[0].Steps);
    }

    [Fact]
    public async Task Diff_RequestedAsDiff()
    {
        string diff = await DownloadAsync(GitHubDownload.PullDiff, 42);

        Assert.Equal(_github.Diff, diff);
        Assert.Equal("application/vnd.github.diff", Assert.Single(_github.Requests).Accept);
    }

    [Fact]
    public async Task Log_RedirectFollowedWithoutToken()
    {
        string log = await DownloadAsync(GitHubDownload.JobLog, 900);

        Assert.Equal(_github.Log, log);
        (string Method, string Host, string PathAndQuery, string Authorization, string Accept, string ApiVersion) storage = Assert.Single(_github.Requests, request => request.Host == "storage.test");
        Assert.Equal(("/logs/900?sig=abc", ""), (storage.PathAndQuery, storage.Authorization));
    }

    [Fact]
    public async Task NotFound_ReposNotFound()
    {
        _github.Status = 404;

        Assert.Equal("repos.not_found", (await Client().GetPullAsync(Marshal, 999, Ct)).Error.Code);
    }

    [Fact]
    public async Task Unauthorized_UnavailableSayingTokenRejected()
    {
        _github.Status = 401;

        Error error = (await Client().ListPullsAsync(Marshal, "open", 30, Ct)).Error;

        Assert.Equal("repos.unavailable", error.Code);
        Assert.Contains("rejected the token", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RateLimited_UnavailableNamingTheResetTime()
    {
        _github.Status = 403;
        _github.StatusHeaders["x-ratelimit-remaining"] = "0";
        _github.StatusHeaders["x-ratelimit-reset"] = "1760011200";

        Error error = (await Client().ListPullsAsync(Marshal, "open", 30, Ct)).Error;

        Assert.Equal("repos.unavailable", error.Code);
        Assert.Contains("2025-10-09T12:00:00.0000000+00:00", error.Message, StringComparison.Ordinal);
        Assert.Contains("about 20 min", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSecret_NoRequest()
    {
        var client = new GitHubRestClient(new Http(_github), Options(), new FakeSecretStore(), new FakeTimeProvider());

        Error error = (await client.ListPullsAsync(Marshal, "open", 30, Ct)).Error;

        Assert.Contains("github_pat", error.Message, StringComparison.Ordinal);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task OnlyGetRequests()
    {
        GitHubRestClient client = Client();
        await client.ListPullsAsync(Marshal, "all", 30, Ct);
        await client.GetPullAsync(Marshal, 42, Ct);
        await client.ListIssuesAsync(Marshal, "open", Ct);
        await client.GetIssueAsync(Marshal, 44, Ct);
        await client.ListCommentsAsync(Marshal, 44, 2, 20, Ct);
        await client.ListRunsAsync(Marshal, null, 20, Ct);
        await client.ListJobsAsync(Marshal, 7001, Ct);
        await DownloadAsync(GitHubDownload.JobLog, 900);

        Assert.All(_github.Requests, request => Assert.Equal("GET", request.Method));
    }

    internal static FakeSecretStore Secrets()
    {
        var secrets = new FakeSecretStore();
        secrets.Values["github_pat"] = FakeGitHub.Token;
        return secrets;
    }

    internal static IOptions<GitHubOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new GitHubOptions { TokenSecret = "github_pat", Repos = { ["marshal"] = "lukas/marshal" } });

    private GitHubRestClient Client() =>
        new(new Http(_github), Options(), Secrets(), new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1760011200 - (20 * 60))));

    private async Task<string> DownloadAsync(GitHubDownload download, long id)
    {
        string text = "";
        Result result = await Client().DownloadAsync(Marshal, download, id, async (content, ct) => text = await new StreamReader(content).ReadToEndAsync(ct), Ct);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return text;
    }

    internal sealed class Http(FakeGitHub github) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(github.Server.CreateHandler());
    }
}
