using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.GitHub.Tests;

public sealed class RepoToolsTests : IAsyncLifetime
{
    private readonly FakeArtifactStore _artifacts = new();
    private FakeGitHub _github = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _github = await FakeGitHub.StartAsync();

    public async ValueTask DisposeAsync() => await _github.DisposeAsync();

    [Fact]
    public async Task Tools_SafeUntrustedWithGithubTaint_RepoEnumOfIds()
    {
        IReadOnlyList<ITool> tools = await Source(("marshal", "lukas/marshal"), ("homelab", "lukas/homelab")).GetToolsAsync(Ct);

        Assert.Equal(["repos.prs", "repos.pr", "repos.issues", "repos.issue", "repos.runs", "repos.run_log"], tools.Select(tool => tool.Descriptor.Id.ToString()));
        Assert.All(tools, tool => Assert.Equal((RiskTier.Safe, true, "github"), (tool.Descriptor.DefaultTier, tool.Descriptor.UntrustedOutput, tool.Descriptor.TaintKind)));
        JsonElement repo = tools[0].Descriptor.ParametersSchema.GetProperty("properties").GetProperty("repo");
        Assert.Equal(["homelab", "marshal"], repo.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("marshal = lukas/marshal", repo.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Equal(["repo", "number"], tools[1].Descriptor.ParametersSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task NoRepos_NoTools()
    {
        Assert.Empty(await Source().GetToolsAsync(Ct));
    }

    [Theory]
    [InlineData("prs")]
    [InlineData("pr")]
    [InlineData("issues")]
    [InlineData("issue")]
    [InlineData("runs")]
    [InlineData("run_log")]
    public async Task RepoOutsideTheAllowlist_InvalidWithoutRequest(string tool)
    {
        Result<ToolOutput> result = await InvokeAsync(tool, new { repo = "torvalds-linux", number = 1, runId = 1 });

        Assert.Equal("repos.invalid_arguments", result.Error.Code);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task Prs_Listed()
    {
        JsonElement prs = await JsonAsync("prs", new { repo = "marshal" });

        Assert.Equal([42, 41], prs.EnumerateArray().Select(pr => pr.GetProperty("number").GetInt32()));
        Assert.Equal(("feat/displays", "master"), (prs[0].GetProperty("head").GetString(), prs[0].GetProperty("base").GetString()));
    }

    [Fact]
    public async Task Pr_SummaryAndDiffArtifact()
    {
        JsonElement pr = await JsonAsync("pr", new { repo = "marshal", number = 42 });

        (ArtifactRecord record, byte[] content) = Assert.Single(_artifacts.Artifacts);
        Assert.Equal(("marshal-pr-42.diff", ArtifactSource.Github, "marshal/pr/42@abc1234def", true), (record.Name, record.Source, record.SourceReference, record.Tainted));
        Assert.Equal(_github.Diff, System.Text.Encoding.UTF8.GetString(content));
        Assert.Equal((record.Id.ToString(), true, 120), (pr.GetProperty("diffArtifactId").GetString(), pr.GetProperty("diffStored").GetBoolean(), pr.GetProperty("additions").GetInt32()));
    }

    [Fact]
    public async Task Pr_SameHeadTwice_ReusesTheArtifact()
    {
        JsonElement first = await JsonAsync("pr", new { repo = "marshal", number = 42 });
        JsonElement second = await JsonAsync("pr", new { repo = "marshal", number = 42 });

        Assert.Single(_artifacts.Artifacts);
        Assert.Equal(first.GetProperty("diffArtifactId").GetString(), second.GetProperty("diffArtifactId").GetString());
        Assert.Single(_github.Requests, request => request.Accept.Contains("diff", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pr_NewCommit_NewArtifact()
    {
        await JsonAsync("pr", new { repo = "marshal", number = 42 });
        _github.HeadSha = "fff9999";

        await JsonAsync("pr", new { repo = "marshal", number = 42 });

        Assert.Equal(["marshal/pr/42@abc1234def", "marshal/pr/42@fff9999"], _artifacts.Artifacts.Select(entry => entry.Record.SourceReference));
    }

    [Fact]
    public async Task Pr_OverQuota_SummaryWithoutDiff()
    {
        _artifacts.Failure = new Error("artifacts.quota_exceeded", "The artifact quota of 5120 MB is used up.", ErrorType.Conflict);

        JsonElement pr = await JsonAsync("pr", new { repo = "marshal", number = 42 });

        Assert.Equal(("Add displays", false, JsonValueKind.Null), (pr.GetProperty("title").GetString(), pr.GetProperty("diffStored").GetBoolean(), pr.GetProperty("diffArtifactId").ValueKind));
        Assert.Contains("quota", pr.GetProperty("diffNotStored").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issues_TitleFilterCaseInsensitive()
    {
        JsonElement issues = await JsonAsync("issues", new { repo = "marshal", query = "PRINTER" });

        Assert.Equal(44, Assert.Single(issues.EnumerateArray()).GetProperty("number").GetInt32());
        Assert.Equal(["bug"], issues[0].GetProperty("labels").EnumerateArray().Select(label => label.GetString()));
    }

    [Fact]
    public async Task Issue_BodyCutAndComments()
    {
        JsonElement issue = await JsonAsync("issue", new { repo = "marshal", number = 44 });

        Assert.Equal(RepoToolSource.BodyLimit + 2, issue.GetProperty("body").GetString()!.Length);
        Assert.Equal(["Happens on Firefox.", "Confirmed."], issue.GetProperty("comments").EnumerateArray().Select(comment => comment.GetProperty("body").GetString()));
    }

    [Fact]
    public async Task Runs_ShortCommit()
    {
        JsonElement run = Assert.Single((await JsonAsync("runs", new { repo = "marshal" })).EnumerateArray());

        Assert.Equal((7001L, "failure", "abc1234"), (run.GetProperty("id").GetInt64(), run.GetProperty("conclusion").GetString(), run.GetProperty("commit").GetString()));
    }

    [Fact]
    public async Task RunLog_FailedJobWithStepsTailAndArtifact()
    {
        JsonElement log = await JsonAsync("run_log", new { repo = "marshal", runId = 7001 });

        JsonElement job = Assert.Single(log.GetProperty("failedJobs").EnumerateArray());
        Assert.Equal(("build (dotnet)", "Test"), (job.GetProperty("job").GetString(), Assert.Single(job.GetProperty("failedSteps").EnumerateArray()).GetString()));
        string[] tail = [.. job.GetProperty("logTail").EnumerateArray().Select(line => line.GetString()!)];
        Assert.Equal(RepoToolSource.TailLines, tail.Length);
        Assert.Equal(("line 31", "line 80"), (tail[0], tail[^1]));
        (ArtifactRecord record, _) = Assert.Single(_artifacts.Artifacts);
        Assert.Equal(("marshal-run-7001-build-dotnet.log", "marshal/job/900", ArtifactKind.Code), (record.Name, record.SourceReference, record.Kind));
        Assert.Equal(record.Id.ToString(), job.GetProperty("logArtifactId").GetString());
    }

    [Fact]
    public async Task RunLog_Twice_ReusesTheLogWithTail()
    {
        await JsonAsync("run_log", new { repo = "marshal", runId = 7001 });
        JsonElement again = await JsonAsync("run_log", new { repo = "marshal", runId = 7001 });

        Assert.Single(_artifacts.Artifacts);
        Assert.Single(_github.Requests, request => request.Host == "storage.test");
        Assert.Equal("line 80", again.GetProperty("failedJobs")[0].GetProperty("logTail").EnumerateArray().Last().GetString());
    }

    [Fact]
    public async Task RunLog_TooLarge_TailWithoutArtifact()
    {
        _artifacts.Failure = new Error("artifacts.too_large", "The file is larger than 200 MB.", ErrorType.Validation);

        JsonElement job = (await JsonAsync("run_log", new { repo = "marshal", runId = 7001 })).GetProperty("failedJobs")[0];

        Assert.False(job.GetProperty("logStored").GetBoolean());
        Assert.Equal(RepoToolSource.TailLines, job.GetProperty("logTail").GetArrayLength());
    }

    private async Task<JsonElement> JsonAsync(string tool, object arguments)
    {
        Result<ToolOutput> result = await InvokeAsync(tool, arguments);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return JsonDocument.Parse(result.Value.Content).RootElement.Clone();
    }

    private async Task<Result<ToolOutput>> InvokeAsync(string tool, object arguments)
    {
        ITool found = (await Source(("marshal", "lukas/marshal")).GetToolsAsync(Ct)).Single(candidate => candidate.Descriptor.Id.Name == tool);
        return await found.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), TestExecutionContext.Text()), Ct);
    }

    private RepoToolSource Source(params (string Id, string FullName)[] repos)
    {
        var options = new GitHubOptions { TokenSecret = "github_pat" };
        foreach ((string id, string fullName) in repos)
            options.Repos[id] = fullName;
        IOptions<GitHubOptions> wrapped = Options.Create(options);
        var client = new GitHubRestClient(new GitHubRestClientTests.Http(_github), wrapped, GitHubRestClientTests.Secrets(), new FakeTimeProvider());
        return new RepoToolSource(client, _artifacts, wrapped);
    }
}
